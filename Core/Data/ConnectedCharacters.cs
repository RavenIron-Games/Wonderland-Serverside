using System.Collections.Generic;
using UnityEngine;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// One connected player as the dedicated server actually sees them: the peer plus their character
    /// ZDO. Position and rotation on that ZDO are kept fresh every physics tick by the owning client's
    /// ZSyncTransform; identity (s_playerID / s_playerName) is written into it once by Player.SetPlayerID;
    /// s_maxHealth / s_stamina / s_health are mirrored there by the owning Player. That ZDO is the ONLY
    /// server-side view of a player - their inventory, skills and stats never leave their own client.
    /// </summary>
    public readonly struct ConnectedCharacter
    {
        public readonly ZNetPeer Peer;
        public readonly ZDO Zdo;

        public ConnectedCharacter(ZNetPeer peer, ZDO zdo)
        {
            Peer = peer;
            Zdo = zdo;
        }

        /// <summary>
        /// The stable identity - PlayerProfile.m_playerID, copied into the world as s_playerID. This is
        /// the number to persist (PLAYER-IDENTITY-FACTS.md): never Peer.m_uid (a connection, new every
        /// session) and never Zdo.m_uid.UserID (the ZDOMan session that minted the ZDO). 0 until the
        /// owning client's Player.SetPlayerID has run, which lands shortly after the peer is ready.
        /// </summary>
        public long PlayerId => Zdo.GetLong(ZDOVars.s_playerID, 0L);

        public string Name
        {
            get
            {
                string fromZdo = Zdo.GetString(ZDOVars.s_playerName, "");
                return string.IsNullOrEmpty(fromZdo) ? (Peer.m_playerName ?? "") : fromZdo;
            }
        }

        public Vector3 Position => Zdo.GetPosition();
    }

    /// <summary>
    /// Replaces Player.GetAllPlayers() everywhere in this mod. That method walks the LOCAL instance
    /// list, and a dedicated server never instantiates a Player: Game.FixedUpdate pins ZNet's reference
    /// position at (1e6, 0, 1e6) every physics tick and ZNetScene.CreateDestroyObjects only ever creates
    /// objects around that point (read from the 1.0.7 server decompile; VALHEIM-DEDICATED-SERVER-FACTS.md
    /// "Player visibility server-side" says the same from live testing). So on the one machine this mod
    /// runs on, GetAllPlayers() is always empty and anything iterating it silently does nothing - which is
    /// exactly what the first version of six subsystems here did. What the server does hold for every
    /// connected player is the character ZDO behind ZNetPeer.m_characterID, and that is what this yields.
    /// </summary>
    public static class ConnectedCharacters
    {
        public static List<ConnectedCharacter> All()
        {
            var result = new List<ConnectedCharacter>();
            if (ZNet.instance == null || ZDOMan.instance == null)
            {
                return result;
            }

            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                // m_characterID arrives via RPC_CharacterID after the peer is otherwise ready, so a
                // ready peer with no character yet is normal for the first moments of a connection.
                if (peer == null || !peer.IsReady() || peer.m_characterID.IsNone())
                {
                    continue;
                }
                ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (zdo == null || !zdo.IsValid())
                {
                    continue;
                }
                result.Add(new ConnectedCharacter(peer, zdo));
            }
            return result;
        }
    }
}
