using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Wonderland.Subsystems.DiscordNotify
{
    /// <summary>
    /// Join/leave detection. Verified against the decompile: ZNet.RPC_PeerInfo only reaches
    /// "peer.m_playerName = text" (IL_04a4) after every rejection path (bad version, blacklist,
    /// full server, wrong password, duplicate connection) has already returned early - so a
    /// postfix that checks m_playerName is non-empty only fires for connections that actually
    /// completed the handshake. ZNet.Disconnect(ZNetPeer) is the vanilla teardown point for every
    /// peer, already used the same way by the bundled ServerSync.cs (RemoveDisconnected prefix).
    /// GetPeer(ZRpc) is private, so it's resolved by reflection exactly as ServerSync.cs does.
    /// </summary>
    [HarmonyPatch]
    public static class PeerJoinLeaveHook
    {
        private static readonly MethodInfo GetPeerMethod =
            AccessTools.DeclaredMethod(typeof(ZNet), "GetPeer", new[] { typeof(ZRpc) });

        // Peers announced as joined, so a leave only announces for a peer whose join we actually
        // reported - guards against a rejected/duplicate connection producing a spurious "left".
        private static readonly HashSet<ZNetPeer> AnnouncedJoins = new HashSet<ZNetPeer>();

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        [HarmonyPostfix]
        public static void OnPeerInfo(ZRpc rpc, ZNet __instance)
        {
            if (!__instance.IsServer() || GetPeerMethod == null)
            {
                return;
            }

            if (!(GetPeerMethod.Invoke(__instance, new object[] { rpc }) is ZNetPeer peer) || string.IsNullOrEmpty(peer.m_playerName))
            {
                return;
            }

            if (!AnnouncedJoins.Add(peer))
            {
                return;
            }

            DiscordNotifySubsystem.AnnounceJoin(peer.m_playerName);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPrefix]
        public static void OnDisconnect(ZNetPeer peer, ZNet __instance)
        {
            if (!__instance.IsServer() || !AnnouncedJoins.Remove(peer))
            {
                return;
            }

            DiscordNotifySubsystem.AnnounceLeave(peer.m_playerName);
        }
    }
}
