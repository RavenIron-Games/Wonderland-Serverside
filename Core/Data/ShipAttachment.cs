using System.Collections.Generic;
using UnityEngine;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// Detects whether a connected player is CURRENTLY STEERING a ship (actively holding the wheel),
    /// purely from ZDO data - no live GameObject needed. Replaces an earlier approach based on
    /// Character.GetRelativePosition's generic "standing on any moving platform" ground-detection,
    /// which could not be confirmed reliable in live testing (granted once, then never again).
    ///
    /// Mechanism (decompile-confirmed, ShipControlls.RPC_RequestControl/RPC_ReleaseControl ~141325-141345):
    /// interacting with a ship's wheel sends a routed RPC to the SHIP's own ZNetView - ShipControlls
    /// reuses the Ship's own ZDO rather than having a separate one ("m_nview = m_ship.GetComponent
    /// &lt;ZNetView&gt;()") - which on success writes the steering player's stable PlayerID directly onto
    /// that ZDO as ZDOVars.s_user (a plain long, hash of "user"). Releasing the wheel resets it to 0.
    /// This is the exact signal every client already uses to know who's driving, so reading it
    /// server-side needs nothing more than ZDO.GetLong(ZDOVars.s_user) on a nearby Ship-prefab ZDO -
    /// no ownership, no live GameObject, no guessing at ground-contact physics.
    /// </summary>
    public static class ShipAttachment
    {
        /// <summary>Generous margin around the player's own position - the ship's ZDO (and therefore
        /// its s_user field) is what's being searched for, not the player's exact standing spot, and a
        /// ship's own pivot can be tens of metres from where its wheel actually is on a large hull.</summary>
        private const float SearchRadius = 40f;

        private static readonly Dictionary<long, bool> LastKnownState = new Dictionary<long, bool>();
        private static readonly List<ZDO> Scratch = new List<ZDO>();

        /// <summary>Speed above which a hull counts as moving: 1 m/s (squared). Water bob on a moored ship
        /// stays well under this (0.1-0.3 m/s in weather), a hull under sail - steered or not, a sail stays
        /// set after the pilot lets go - is well over it. 0.10.8 used 0.03 m/s, which any floating hull
        /// exceeds, so a moored ship near its crew was "underway" for as long as they stayed (raised in
        /// 0.10.9).</summary>
        private const float UnderwaySpeedSqr = 1f;

        /// <summary>
        /// True when a connected client is simulating this ship right now: it owns the ZDO and stands
        /// within its active area of it. Everything the server writes to such a hull races that client's
        /// own revision stream (ZDOMan.RPC_ZDOData discards anything at or below the revision it holds -
        /// ITEMDROP-OWNERSHIP-AND-PICKUP-SYNC-FACTS.md section 6/8), and a floating hull never sleeps:
        /// Ship.CustomFixedUpdate wakes the body and applies buoyancy every physics step, and OwnerSync
        /// bumps the revision on any sub-millimetre move. So a plain write here is lost, and a forced one
        /// (a revision lead) can drop the owner's in-flight cargo change - item loss. Such a hull is never
        /// written directly: the rows anchor borrows it first (HullBorrow), and a move-then-destroy
        /// (vacuum, production supply, grid overflow) leaves it alone altogether.
        ///
        /// Ownership alone is not enough (0.10.8 review): ReleaseNearbyZDOS only releases what a peer
        /// walks away from; a portal, respawn or admin teleport leaves a far-away hull owned by a
        /// connected peer that no longer instantiates it, and the server's write to it is then the only
        /// one there is. Hence the ZNetScene.InActiveArea check against the owner's reference position.
        /// </summary>
        public static bool IsSimulatedByClient(ZDO shipZdo)
        {
            if (shipZdo == null || ZNet.instance == null)
            {
                return false;
            }
            long owner = shipZdo.GetOwner();
            if (owner == 0L || owner == ZDOMan.GetSessionID())
            {
                return false;
            }
            ZNetPeer? peer = FindConnectedPeer(owner);
            return peer != null && ZNetScene.InActiveArea(shipZdo.GetPosition(), peer.GetRefPos());
        }

        /// <summary>
        /// True when a client is simulating this ship AND it is under sail - steered, or moving faster than a
        /// bob. Such a hull is never borrowed for a write (HullBorrow): for the round trip of the borrow the
        /// owner's client neither simulates nor moves it, a pause nobody notices on a moored hull and a
        /// visible hitch for the passengers of a sailing one. Ownership is the gate, not the velocity fields alone (0.10.8):
        /// ZSyncTransform.OwnerSync writes s_velHash / s_bodyVelHash only while a client owns the ZDO, and
        /// when that client leaves the server drops the owner within 2 s and the fields freeze at the last
        /// bob. 0.10.3-0.10.7 read those frozen values as "hull in motion" and never wrote a moored ship
        /// again - the "boat storage is no longer expanded" regression. A steered hull is one whose s_user
        /// is a player who is actually connected: s_user is only cleared by RPC_ReleaseControl, which a
        /// crash at the wheel never sends.
        /// </summary>
        public static bool IsUnderway(ZDO shipZdo)
        {
            if (!IsSimulatedByClient(shipZdo))
            {
                return false;
            }
            long user = shipZdo.GetLong(ZDOVars.s_user, 0L);
            if (user != 0L && IsConnectedPlayerId(user))
            {
                return true;
            }
            return shipZdo.GetVec3(ZDOVars.s_velHash, Vector3.zero).sqrMagnitude > UnderwaySpeedSqr
                || shipZdo.GetVec3(ZDOVars.s_bodyVelHash, Vector3.zero).sqrMagnitude > UnderwaySpeedSqr;
        }

        /// <summary>True when a connected client owns this ZDO - see IsSimulatedByClient for why "connected"
        /// alone does not mean "in range".</summary>
        public static bool IsConnectedPeer(long peerId)
        {
            return FindConnectedPeer(peerId) != null;
        }

        private static ZNetPeer? FindConnectedPeer(long peerId)
        {
            if (peerId == 0L || ZNet.instance == null)
            {
                return null;
            }
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer != null && peer.m_uid == peerId)
                {
                    return peer;
                }
            }
            return null;
        }

        private static bool IsConnectedPlayerId(long playerId)
        {
            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                if (character.PlayerId == playerId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True when any Ship-prefab ZDO sits within SearchRadius of the position - a passenger,
        /// a swimmer beside the hull, someone standing on the deck of a moored ship. Used by PositionWatch:
        /// a character at sea reports Y ~ 30 over a seabed the height map puts at 0, and 0.9-0.10.7 flagged
        /// that as fly/noclip on every sample (280 lines in one 2026-09-20 sail).</summary>
        public static bool IsNearShip(Vector3 position)
        {
            if (ZNetScene.instance == null)
            {
                return false;
            }
            ZdoSpatialQuery.FindNear(position, SearchRadius, Scratch);
            foreach (ZDO zdo in Scratch)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab != null && prefab.GetComponent<Ship>() != null)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsSteeringShip(ConnectedCharacter character)
        {
            bool result = Evaluate(character, out string detail);

            long playerId = character.PlayerId;
            if (!LastKnownState.TryGetValue(playerId, out bool last) || last != result)
            {
                LastKnownState[playerId] = result;
                WonderlandDebug.LogInfo($"[ShipAttachment] {character.Name}: steering-ship state -> {result} ({detail}).");
            }

            return result;
        }

        private static bool Evaluate(ConnectedCharacter character, out string detail)
        {
            if (ZNetScene.instance == null)
            {
                detail = "ZNetScene not ready";
                return false;
            }

            long playerId = character.PlayerId;
            ZdoSpatialQuery.FindNear(character.Position, SearchRadius, Scratch);

            foreach (ZDO zdo in Scratch)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab == null || prefab.GetComponent<Ship>() == null)
                {
                    continue;
                }

                long steeringUser = zdo.GetLong(ZDOVars.s_user, 0L);
                if (steeringUser == playerId)
                {
                    detail = $"steering '{prefab.name}' (s_user matches)";
                    return true;
                }
            }

            detail = $"not the s_user of any Ship ZDO within {SearchRadius:0}m";
            return false;
        }
    }
}
