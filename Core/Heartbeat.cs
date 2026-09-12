using System.Linq;
using UnityEngine;
using Wonderland.Core.Data;
using Wonderland.Subsystems.DiscordNotify;

namespace Wonderland.Core
{
    /// <summary>
    /// One periodic summary - a server log line always, and optionally a matching Discord post - so an
    /// admin (or their community) can confirm Wonderland is still alive and see who's currently online,
    /// without needing VerboseLogging's full per-event detail turned on. Ticked directly from
    /// WonderlandPlugin.Update, independent of the subsystem registry - this is a mod-wide concern, not
    /// any one subsystem's. The log heartbeat (HeartbeatEnabled, section 1) and the Discord heartbeat
    /// (DiscordNotifyHeartbeat, section 14) share this one timer/interval rather than running two.
    /// </summary>
    public static class Heartbeat
    {
        private static float _uptimeSeconds;
        private static float _timer;

        public static void OnUpdate(float dt)
        {
            _uptimeSeconds += dt;

            bool logHeartbeat = WonderlandConfig.HeartbeatEnabled?.Value == true;
            bool discordHeartbeat = WonderlandConfig.DiscordNotifyHeartbeat?.Value == true;
            if (!logHeartbeat && !discordHeartbeat)
            {
                return;
            }

            _timer += dt;
            float intervalSeconds = (WonderlandConfig.HeartbeatIntervalMinutes?.Value ?? 15f) * 60f;
            if (_timer < intervalSeconds)
            {
                return;
            }
            _timer = 0f;

            var characters = ZNet.instance != null ? ConnectedCharacters.All() : new System.Collections.Generic.List<ConnectedCharacter>();
            int playerCount = characters.Count;
            string playerNames = string.Join(", ", characters.Select(c => c.Name));
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "(no world)";
            string uptime = FormatUptime(_uptimeSeconds);

            if (logHeartbeat)
            {
                string who = playerCount > 0 ? playerNames : "none";
                WonderlandDebug.LogAlways($"[Heartbeat] '{world}' up {uptime} | {playerCount} player(s) online: {who} | Wonderland {WonderlandPlugin.ModVersion} running normally.");
            }

            if (discordHeartbeat)
            {
                DiscordNotifySubsystem.AnnounceHeartbeat(uptime, playerCount, playerNames);
            }
        }

        private static string FormatUptime(float seconds)
        {
            int totalMinutes = Mathf.FloorToInt(seconds / 60f);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return hours > 0 ? $"{hours}h{minutes}m" : $"{minutes}m";
        }
    }
}
