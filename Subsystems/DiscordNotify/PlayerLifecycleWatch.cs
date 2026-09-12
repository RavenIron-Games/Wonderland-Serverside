using System.Collections.Generic;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.DiscordNotify
{
    /// <summary>
    /// Detects two player-lifecycle events purely from server-visible ZDO/global-key state, the same
    /// ConnectedCharacters sweep pattern PositionWatch and FirstSpawnGrant already use:
    ///
    /// Death: the character ZDO's own ZDOVars.s_dead flag (Character.OnDeath sets it true, respawn
    /// sets it back false - the exact signal PositionWatch already reads to suppress its own
    /// respawn-teleport false positive). A false-&gt;true edge, tracked per stable playerID, is a death.
    ///
    /// First time playing: a dedicated world global key ("wonderland_discord_seen_&lt;playerID&gt;"),
    /// the same once-only-flag pattern FirstSpawnGrant uses for its starter kit - kept as its own
    /// independent key rather than reusing FirstSpawnGrant's, so this still works with
    /// StarterGrantEnabled turned off.
    /// </summary>
    public static class PlayerLifecycleWatch
    {
        private const string SeenKeyPrefix = "wonderland_discord_seen_";

        private static readonly Dictionary<long, bool> LastDead = new Dictionary<long, bool>();
        private static readonly HashSet<long> CheckedFirstJoin = new HashSet<long>();
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
                CheckFirstJoin(character, playerId);
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
            }
        }

        private static void CheckFirstJoin(ConnectedCharacter character, long playerId)
        {
            if (!CheckedFirstJoin.Add(playerId))
            {
                return; // already resolved this process - no need to re-read the global key every sweep.
            }

            string key = SeenKeyPrefix + playerId;
            if (ZoneSystem.instance.GetGlobalKey(key))
            {
                return;
            }

            ZoneSystem.instance.SetGlobalKey(key);
            DiscordNotifySubsystem.AnnounceFirstJoin(character.Name);
        }
    }
}
