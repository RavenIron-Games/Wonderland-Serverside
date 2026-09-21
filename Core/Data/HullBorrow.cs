using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// Borrows a hull that a client is simulating, for one server-side cargo write, through vanilla's own
    /// container-open handshake - so that the write can never race the owner.
    ///
    /// A ship's cargo lives on the ship's ZDO, and the client nearest a floating hull owns that ZDO and
    /// bumps its DataRevision a few times every frame (ZSyncTransform.OwnerSync). A plain server write to
    /// it is discarded at the owner and replaced by the owner's next packet (0.10.8: "parked" in the log,
    /// never on a screen). Forcing it through with a revision lead (0.10.9 shipped that way; withdrawn in 0.10.10) is
    /// worse: a deposit or withdrawal the owner made in the round trip before the write lands is dropped
    /// at the server and then reloaded away on the client - an item lost or duplicated. A write to a ZDO
    /// another client is actively rewriting is never safe; the ZDO has to change hands first, and the
    /// hand-over has to originate at the client, so that its final packet is the last word.
    ///
    /// Vanilla already has that hand-over: Container.RPC_RequestOpen. The OWNER's client runs it, refuses
    /// while its cargo is open (its own m_inUse, the authority), and otherwise does ForceSendZDO(requester)
    /// + SetOwner(requester) itself. From that instant it neither simulates nor writes the hull; the packet
    /// carrying the ownership change also carries its final s_items. The server, as the new owner, is the
    /// sole writer: it loads the fresh copy, writes, and hands the hull straight back (SetOwner(owner) +
    /// ForceSendZDO). On the owner's client the hull is dead-reckoned for the round trip - a non-owner's
    /// ZSyncTransform.ClientSync carries it on at its last synced velocity and OwnerSync snaps it to the
    /// ZDO position when ownership returns; Ship.CustomFixedUpdate skips buoyancy for a non-owner. Under
    /// the 1 m/s gate (ShipAttachment.IsUnderway) that is under 10 cm over a tenth of a second on a moored
    /// hull, the collider never leaves the passengers; a hull under sail is never borrowed. RPC_OpenResponse
    /// coming back is a no-op on a dedicated server (no Player.m_localPlayer, and no instance for the hull -
    /// Game.FixedUpdate pins the server's reference position far outside the world).
    ///
    /// The hand-over packet is only the client's last word if its DataRevision is above the server's copy
    /// (RPC_ZDOData applies the ownership either way but the data only when newer), so the server's copy
    /// must not have been bumped since the request: no engine writes a client-simulated hull directly (the
    /// rows anchor and the integrity sweep borrow it, the rest leave it alone), and a grant whose revision
    /// has not moved is handed back unwritten. ZDOMan.ReleaseZDOS (every 2 s) would give a server-owned
    /// hull in a peer's area back to that peer on its own; ShouldBlockOwnerChange holds that off while a
    /// borrow is pending. A borrow the owner has not granted after TimeoutSeconds (cargo open, packet lost,
    /// lag) stays known for GraceSeconds more so a late grant is still written and handed back at once,
    /// rather than sitting server-owned until vanilla's next pass. One request per hull per
    /// RequestCooldownSeconds, whoever asks. Nothing is written unless the server actually owns the hull
    /// at the moment of writing.
    /// </summary>
    public static class HullBorrow
    {
        private const float TimeoutSeconds = 2f;
        private const float GraceSeconds = 10f;
        private const float RequestCooldownSeconds = 60f;
        private const string RequestOpenRpc = "RPC_RequestOpen";

        private sealed class Pending
        {
            public long Owner;
            public float RequestedAt;
            public uint RevisionAtRequest;
            public Action<ZDO> Write = null!;
        }

        private static readonly Dictionary<ZDOID, Pending> _pending = new Dictionary<ZDOID, Pending>();
        private static readonly Dictionary<ZDOID, float> _lastRequestAt = new Dictionary<ZDOID, float>();
        private static readonly List<ZDOID> _done = new List<ZDOID>();
        private static bool _handingBack;

        public static int PendingCount => _pending.Count;

        public static bool IsPending(ZDOID uid)
        {
            return _pending.ContainsKey(uid);
        }

        /// <summary>
        /// Asks the hull's owning client for the ZDO. <paramref name="write"/> runs on a later frame, once
        /// the server owns it (or never, if the owner refuses). Returns false when nothing was asked: not
        /// client-simulated (write it directly), under sail, already pending, or asked for within the last
        /// RequestCooldownSeconds by anyone.
        /// </summary>
        public static bool Request(ZDO hull, Action<ZDO> write)
        {
            if (hull == null || ZRoutedRpc.instance == null || _pending.ContainsKey(hull.m_uid))
            {
                return false;
            }
            float now = Time.time;
            if (_lastRequestAt.TryGetValue(hull.m_uid, out float last) && now - last < RequestCooldownSeconds)
            {
                return false;
            }
            if (!ShipAttachment.IsSimulatedByClient(hull) || ShipAttachment.IsUnderway(hull))
            {
                return false;
            }
            long owner = hull.GetOwner();
            // One long parameter (the player id CheckAccess judges; a ship's cargo is Public). The requester
            // that RPC_RequestOpen hands the ZDO to is the routed RPC's sender - this server's session id.
            ZRoutedRpc.instance.InvokeRoutedRPC(owner, hull.m_uid, RequestOpenRpc, 0L);
            _lastRequestAt[hull.m_uid] = now;
            if (_lastRequestAt.Count > 256)
            {
                PruneCooldowns(now);
            }
            _pending[hull.m_uid] = new Pending { Owner = owner, RequestedAt = now, RevisionAtRequest = hull.DataRevision, Write = write };
            return true;
        }

        private static void PruneCooldowns(float now)
        {
            _done.Clear();
            foreach (KeyValuePair<ZDOID, float> entry in _lastRequestAt)
            {
                if (now - entry.Value >= RequestCooldownSeconds)
                {
                    _done.Add(entry.Key);
                }
            }
            for (int i = 0; i < _done.Count; i++)
            {
                _lastRequestAt.Remove(_done[i]);
            }
            _done.Clear();
        }

        /// <summary>Every frame: a borrowed hull that has arrived is written and handed back at once.</summary>
        public static void OnUpdate()
        {
            if (_pending.Count == 0 || ZDOMan.instance == null)
            {
                return;
            }

            long session = ZDOMan.GetSessionID();
            float now = Time.time;
            _done.Clear();
            foreach (KeyValuePair<ZDOID, Pending> entry in _pending)
            {
                ZDO? hull = ZDOMan.instance.GetZDO(entry.Key);
                if (hull == null)
                {
                    _done.Add(entry.Key);
                    continue;
                }
                if (hull.GetOwner() == session)
                {
                    try
                    {
                        if (hull.DataRevision == entry.Value.RevisionAtRequest)
                        {
                            // Ownership arrived but the packet's data did not (its revision was not above the
                            // server's copy): the copy here may be behind the client's. Not written.
                            WonderlandDebug.LogInfo($"[HullBorrow] hull {hull.m_uid} arrived with its data at the revision we already held - handed back unwritten.");
                        }
                        else
                        {
                            entry.Value.Write(hull);
                        }
                    }
                    catch (Exception ex)
                    {
                        WonderlandDebug.LogWarning($"[HullBorrow] write to {hull.m_uid} failed, handing the hull back untouched: {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        HandBack(hull, entry.Value.Owner);
                    }
                    _done.Add(entry.Key);
                }
                else if (now - entry.Value.RequestedAt > TimeoutSeconds + GraceSeconds)
                {
                    _done.Add(entry.Key); // refused (cargo open) or never delivered; the cooldown decides when it is asked again
                }
            }
            for (int i = 0; i < _done.Count; i++)
            {
                _pending.Remove(_done[i]);
            }
        }

        /// <summary>For the ZDO.SetOwner prefix: vanilla's ReleaseZDOS must not hand a hull back before the
        /// borrowed write has run. Our own hand-back goes through.</summary>
        public static bool ShouldBlockOwnerChange(ZDO zdo, long newOwner)
        {
            if (_handingBack || _pending.Count == 0 || zdo == null)
            {
                return false;
            }
            return _pending.ContainsKey(zdo.m_uid) && zdo.GetOwner() == ZDOMan.GetSessionID() && newOwner != ZDOMan.GetSessionID();
        }

        private static void HandBack(ZDO hull, long owner)
        {
            if (!ShipAttachment.IsConnectedPeer(owner))
            {
                owner = 0L; // left meanwhile: unowned, and ReleaseZDOS gives it to whoever is nearest
            }
            _handingBack = true;
            try
            {
                hull.SetOwner(owner);
            }
            finally
            {
                _handingBack = false;
            }
            if (owner != 0L)
            {
                ZDOMan.instance.ForceSendZDO(owner, hull.m_uid);
            }
        }
    }
}
