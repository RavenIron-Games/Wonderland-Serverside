using Splatform;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// Where a connected player is playing from, read from the one place the handshake carries it: the
    /// socket's host name, which is the peer's PlatformUserID. Over PlayFab (-crossplay) that is the
    /// "Steam_7656...", "Xbox_2533...", "PlayStation_..." or "Nintendo_..." string the client sent
    /// (ZPlayFabSocket.GetHostName returns m_platformPlayerId.ToString()); over Steamworks alone it is the
    /// bare Steam64 and every peer is Steam by construction (ZSteamSocket.GetHostName). Read from the
    /// server decompile: RPC_PeerInfo sends Version.CurrentVersion.ToString() WITHOUT
    /// Version.GetPlatformPrefix()'s "l"/"dw"/"ms"/"sw2", and neither SimulationDistance nor m_playfabId
    /// says anything about the device, so this is the ACCOUNT platform and no finer - "Xbox" is an Xbox
    /// Live account whether it sits in a console or the Microsoft Store / Game Pass build on a PC, and
    /// "PC" is any Steam client (Windows, Linux, Steam Deck). Nintendo reads as "Switch 2" because
    /// Version.Platforms has no other Nintendo target. The id itself (Steam64, XUID) never leaves this
    /// class; only the label does. The same ID construction PeerJoinLeaveHook compares against
    /// ZNet.World.m_playerHistory lives here too, so the two can never drift apart.
    /// </summary>
    public static class PeerPlatform
    {
        /// <summary>Mirror of the m_onlineBackend switch in ZNet.UpdatePlayerList, so the ID is byte-for-byte
        /// the one vanilla stores in the player history. None when there is no socket or it does not parse
        /// (TryParse rather than the string constructor, which Debug.Logs every failure - this runs on every
        /// roster). Every failure mode resolves to None, i.e. "unknown", never to a wrong platform.</summary>
        public static PlatformUserID Id(ZNetPeer peer)
        {
            if (peer?.m_socket == null || ZNet.instance == null)
            {
                return PlatformUserID.None;
            }

            switch (ZNet.m_onlineBackend)
            {
                case OnlineBackendType.Steamworks:
                    return new PlatformUserID(ZNet.instance.m_steamPlatform, peer.m_socket.GetHostName());
                case OnlineBackendType.PlayFab:
                    return PlatformUserID.TryParse(peer.m_socket.GetHostName(), out PlatformUserID id) ? id : PlatformUserID.None;
                default:
                    return PlatformUserID.None;
            }
        }

        /// <summary>"PC", "Xbox", "PlayStation", "Switch 2" - or "" when the game did not say.</summary>
        public static string Label(ZNetPeer peer)
        {
            return LabelFor(Id(peer).m_platform);
        }

        /// <summary>Label for a stored "Steam_7656..." style id (the BarrkBOT registry keeps that string);
        /// "" for anything that does not parse.</summary>
        public static string LabelForId(string platformUserId)
        {
            return PlatformUserID.TryParse(platformUserId, out PlatformUserID id) ? LabelFor(id.m_platform) : "";
        }

        public static string LabelFor(Platform platform)
        {
            if (!platform.IsValid)
            {
                return "";
            }

            // The platform names Splatform itself knows (PlatformUserID.s_platformToDisplayPrefixes): Steam,
            // Xbox, PlayStation, Nintendo, GameCenter. Anything it grows later is shown by its own name
            // rather than hidden.
            switch (platform.ToString())
            {
                case "Steam": return "PC";
                case "Xbox": return "Xbox";
                case "PlayStation": return "PlayStation";
                case "Nintendo": return "Switch 2";
                default: return platform.ToString();
            }
        }

        /// <summary>"Alice (PC)", or just "Alice" when the platform is unknown - the one spelling every roster
        /// and log line uses, so the heartbeat, Discord and the export all read the same way.</summary>
        public static string WithLabel(string name, ZNetPeer peer)
        {
            string label = Label(peer);
            return string.IsNullOrEmpty(label) ? name ?? "" : $"{name} ({label})";
        }
    }
}
