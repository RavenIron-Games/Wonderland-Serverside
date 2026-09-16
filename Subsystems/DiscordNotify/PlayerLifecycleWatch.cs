using System.Collections.Generic;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.DiscordNotify
{
    /// <summary>
    /// Detects player deaths purely from server-visible ZDO state, the same ConnectedCharacters sweep
    /// pattern PositionWatch and FirstSpawnGrant already use: the character ZDO's own ZDOVars.s_dead
    /// flag (Character.OnDeath sets it true, respawn sets it back false - the exact signal PositionWatch
    /// already reads to suppress its own respawn-teleport false positive). A false-&gt;true edge, tracked
    /// per stable playerID, is a death.
    ///
    /// First-join detection used to live here too, keyed on a mod-private global key that had no history
    /// before the mod was installed (so every veteran was welcomed as new once). It now lives in
    /// PeerJoinLeaveHook, decided per account from vanilla's own persisted ZNet.World.m_playerHistory.
    /// </summary>
    public static class PlayerLifecycleWatch
    {
        private static readonly Dictionary<long, bool> LastDead = new Dictionary<long, bool>();
        private static float _timer;

        public static void OnUpdate(float dt)
        {
            _timer += dt;
            float interval = WonderlandConfig.DiscordLifecycleInterval?.Value ?? 3f;
            if (_timer < interval || ZoneSystem.instance == null)
            {
                return;
            }
            _timer = 0f;

            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                long playerId = character.PlayerId;
                if (playerId == 0L)
                {
                    continue; // Player.SetPlayerID hasn't written the identity into the ZDO yet - next sweep.
                }

                CheckDeath(character, playerId);
            }
        }

        private static void CheckDeath(ConnectedCharacter character, long playerId)
        {
            bool isDead = character.Zdo.GetBool(ZDOVars.s_dead);

            // No baseline yet for this playerID (first sighting since this process started, e.g. server
            // just booted or this player just connected) - record it without announcing, so a player who
            // happens to already be in the dead/respawning state doesn't get a spurious death post for
            // something that already happened before this watch was looking.
            if (!LastDead.TryGetValue(playerId, out bool wasDead))
            {
                LastDead[playerId] = isDead;
                return;
            }

            LastDead[playerId] = isDead;
            if (isDead && !wasDead)
            {
                DiscordNotifySubsystem.AnnounceDeath(character.Name);
                BarrkBot.BarrkBotStats.OnDeath(playerId, character.Name);
            }
        }
    }
}
