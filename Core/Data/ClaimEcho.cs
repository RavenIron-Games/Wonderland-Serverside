using System.Collections.Generic;
using UnityEngine;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// Removes the ghost that a server-side claim can leave on a client.
    ///
    /// Every claim this mod makes on an item a client owns - the buoyancy lift, the vacuum's settle hold,
    /// a pickup grant - is SetOwner + a revision lead + ForceSendZDO. If that client picks the item up in
    /// the same instant, it destroys its own copy and sends DestroyZDO, and the server's packet, already in
    /// flight, then reaches a client that no longer has the ZDO: ZDOMan.RPC_ZDOData creates it (the
    /// m_deadZDOs guard against exactly this runs only on the server), ZNetScene instantiates it, and the
    /// client has an item that exists nowhere else - "owned" by the server, which has nothing to hand back,
    /// so E plays the animation and RequestOwn goes nowhere, until the next log-in. No item is lost or
    /// duplicated (the destroy landed; the inventory has the stack), but the world shows a stack that is
    /// not there.
    ///
    /// The server learns of the pickup a moment later, when the destroy batch arrives. Every ZDO this mod
    /// force-sends is remembered here for a few seconds; a client destroy batch naming one is echoed back
    /// to that client as the same "DestroyZDO" RPC, and its own ZDOMan.HandleDestroyedZDO removes the
    /// ghost (ZNetScene.OnZDODestroyed destroys the instance) - a no-op when there is none. The echo
    /// travels the same reliable, ordered connection as the stale packet, and a destroyed ZDO is never
    /// force-sent again (AddForceSendZdos null-checks), so the echo always lands last.
    /// </summary>
    public static class ClaimEcho
    {
        private const float RememberSeconds = 15f;
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<ZDOID, float> _recent = new Dictionary<ZDOID, float>();
        private static readonly List<ZDOID> _scratch = new List<ZDOID>();
        private static readonly List<ZDOID> _echo = new List<ZDOID>();

        /// <summary>Call right after force-sending an item ZDO a client may be about to pick up.</summary>
        public static void Note(ZDOID uid)
        {
            float now = Time.time;
            _recent[uid] = now;
            if (_recent.Count > MaxRemembered)
            {
                Prune(now);
            }
        }

        /// <summary>For the RPC_DestroyZDO prefix: read-only on the package (position restored).</summary>
        public static void OnClientDestroyBatch(long sender, ZPackage pkg)
        {
            if (_recent.Count == 0 || pkg == null || ZRoutedRpc.instance == null || sender == ZDOMan.GetSessionID())
            {
                return;
            }
            _echo.Clear();
            int pos = pkg.GetPos();
            try
            {
                pkg.SetPos(0);
                int count = pkg.ReadInt();
                for (int i = 0; i < count && i < 4096; i++)
                {
                    ZDOID uid = pkg.ReadZDOID();
                    if (_recent.Remove(uid))
                    {
                        _echo.Add(uid);
                    }
                }
            }
            finally
            {
                pkg.SetPos(pos);
            }
            if (_echo.Count == 0)
            {
                return;
            }

            var echo = new ZPackage();
            echo.Write(_echo.Count);
            for (int i = 0; i < _echo.Count; i++)
            {
                echo.Write(_echo[i]);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, "DestroyZDO", echo);
            WonderlandDebug.LogInfo($"[ClaimEcho] echoed the destroy of {_echo.Count} recently claimed item(s) back to peer {sender} so no ghost is left on its screen.");
        }

        private static void Prune(float now)
        {
            _scratch.Clear();
            foreach (KeyValuePair<ZDOID, float> entry in _recent)
            {
                if (now - entry.Value > RememberSeconds)
                {
                    _scratch.Add(entry.Key);
                }
            }
            for (int i = 0; i < _scratch.Count; i++)
            {
                _recent.Remove(_scratch[i]);
            }
            _scratch.Clear();
        }
    }
}
