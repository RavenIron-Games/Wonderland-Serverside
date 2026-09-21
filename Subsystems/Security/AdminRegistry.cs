using System.Collections.Generic;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Centralized admin identification and bypass tracking for Wonderland security systems.
    /// Recognizes authenticated server admins from adminlist.txt and caches their PlayerID
    /// so admin-placed pieces and containers are also recognized.
    /// </summary>
    public static class AdminRegistry
    {
        private static readonly HashSet<long> _adminPlayerIds = new HashSet<long>();

        public static bool IsAdmin(ConnectedCharacter character)
        {
            if (character.IsAdmin)
            {
                if (character.PlayerId != 0L)
                {
                    _adminPlayerIds.Add(character.PlayerId);
                }
                return true;
            }
            return false;
        }

        public static bool IsAdmin(ZNetPeer? peer)
        {
            if (peer?.m_socket == null || ZNet.instance == null) return false;
            string host = peer.m_socket.GetHostName();
            if (string.IsNullOrEmpty(host)) return false;
            bool admin = ZNet.instance.IsAdmin(host);
            if (admin && peer.m_characterID.UserID != 0L)
            {
                _adminPlayerIds.Add(peer.m_characterID.UserID);
            }
            return admin;
        }

        public static bool IsAdminPlayerId(long playerId)
        {
            if (playerId == 0L) return false;
            if (_adminPlayerIds.Contains(playerId)) return true;
            foreach (ConnectedCharacter ch in ConnectedCharacters.All())
            {
                if (ch.IsAdmin && ch.PlayerId == playerId)
                {
                    _adminPlayerIds.Add(playerId);
                    return true;
                }
            }
            return false;
        }

        public static void RegisterAdmin(long playerId)
        {
            if (playerId != 0L)
            {
                _adminPlayerIds.Add(playerId);
            }
        }
    }
}
