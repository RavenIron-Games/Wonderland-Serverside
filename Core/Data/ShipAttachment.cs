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
