using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Splatform;

namespace Wonderland.Subsystems.DiscordNotify
{
    /// <summary>
    /// Join/leave/first-join detection. Verified against the server decompile: ZNet.RPC_PeerInfo only
    /// reaches "peer.m_playerName = text" (IL_04a4) after every rejection path (bad version, blacklist,
    /// full server, wrong password, duplicate connection) has already returned early - so a postfix
    /// that checks m_playerName is non-empty only fires for connections that actually completed the
    /// handshake. ZNet.Disconnect(ZNetPeer) is the vanilla teardown point for every peer, already used
    /// the same way by the bundled ServerSync.cs (RemoveDisconnected prefix). GetPeer(ZRpc) is private,
    /// so it's resolved by reflection exactly as ServerSync.cs does.
    ///
    /// First join is decided per ACCOUNT from vanilla's own persisted record, ZNet.World.m_playerHistory
    /// (List&lt;ZNet.CrossNetworkUserInfo&gt;, keyed by PlatformUserID, saved in the .fwl since
    /// Version.World.DeepNorth - World.Load / World.SaveWorldFWLData). The server tail of RPC_PeerInfo calls
    /// SendPlayerList() -&gt; UpdatePlayerList() -&gt; UpdatePlayerHistory(), which appends any account
    /// not yet in that list - so by the time a postfix runs the new account is ALREADY in the history
    /// and the question has to be asked in a prefix. The prefix builds the PlatformUserID exactly the
    /// way UpdatePlayerList does (Steamworks: new PlatformUserID(m_steamPlatform, socket host name);
    /// PlayFab: new PlatformUserID(host name); anything else: None) and hands "already known" to the
    /// postfix through __state. Every failure mode (not the server, no peer, no world, invalid ID)
    /// resolves to "known", so a fault can only ever suppress a welcome, never spam one.
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
        [HarmonyPrefix]
        public static void BeforePeerInfo(ZRpc rpc, ZNet __instance, out bool __state)
        {
            __state = true; // "already known" unless proven otherwise below
            if (!__instance.IsServer() || GetPeerMethod == null || ZNet.World == null)
            {
                return;
            }

            if (!(GetPeerMethod.Invoke(__instance, new object[] { rpc }) is ZNetPeer peer) || peer.m_socket == null)
            {
                return;
            }

            PlatformUserID id = ResolvePlatformUserID(__instance, peer);
            if (!id.IsValid)
            {
                return;
            }

            __state = ZNet.World.m_playerHistory.FindIndex(h => h.m_id == id) >= 0;
        }

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        [HarmonyPostfix]
        public static void OnPeerInfo(ZRpc rpc, ZNet __instance, bool __state)
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
            if (!__state)
            {
                DiscordNotifySubsystem.AnnounceFirstJoin(peer.m_playerName);
            }
            PlatformUserID id = peer.m_socket != null ? ResolvePlatformUserID(__instance, peer) : PlatformUserID.None;
            BarrkBot.BarrkBotStats.OnPeerJoined(peer.m_uid, id.IsValid ? id.ToString() : "", !__state);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPrefix]
        public static void OnDisconnect(ZNetPeer peer, ZNet __instance)
        {
            if (!__instance.IsServer() || peer == null)
            {
                return;
            }

            // Every runtime disconnect path (timeout, kick, RPC_Disconnect, handshake failure) comes through
            // here; shutdown does not (ZNet.StopAll disposes peers directly), BarrkBot handles that itself.
            BarrkBot.BarrkBotStats.OnPeerLeft(peer.m_uid);
            if (!AnnouncedJoins.Remove(peer))
            {
                return;
            }

            DiscordNotifySubsystem.AnnounceLeave(peer.m_playerName, peer);
        }

        /// <summary>Mirror of the m_onlineBackend switch in ZNet.UpdatePlayerList, so the ID compared
        /// against m_playerHistory is byte-for-byte the one vanilla stores there.</summary>
        private static PlatformUserID ResolvePlatformUserID(ZNet znet, ZNetPeer peer)
        {
            switch (ZNet.m_onlineBackend)
            {
                case OnlineBackendType.Steamworks:
                    return new PlatformUserID(znet.m_steamPlatform, peer.m_socket.GetHostName());
                case OnlineBackendType.PlayFab:
                    return new PlatformUserID(peer.m_socket.GetHostName());
                default:
                    return PlatformUserID.None;
            }
        }
    }
}
