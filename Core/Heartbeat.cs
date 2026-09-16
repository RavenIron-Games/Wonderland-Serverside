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
    /// (DiscordNotifyHeartbeat, section 14) share one interval by default; DiscordHeartbeatIntervalMinutes
    /// (section 14, 0 = shared) gives the Discord post its own, so a channel can get an hourly roster
    /// while the log keeps its 15-minute pulse. Two timers, one roster read per tick that fires.
    /// </summary>
    public static class Heartbeat
    {
        private static float _uptimeSeconds;
        private static readonly string _startedAtUtc = System.DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        private static float _logTimer;

        public static float UptimeSeconds => _uptimeSeconds;
        public static string StartedAtUtc => _startedAtUtc;
        private static float _discordTimer;

        public static void OnUpdate(float dt)
        {
            _uptimeSeconds += dt;

            bool logHeartbeat = WonderlandConfig.HeartbeatEnabled?.Value == true;
            bool discordHeartbeat = WonderlandConfig.DiscordNotifyHeartbeat?.Value == true;
            _logTimer = logHeartbeat ? _logTimer + dt : 0f;
            _discordTimer = discordHeartbeat ? _discordTimer + dt : 0f;
            if (!logHeartbeat && !discordHeartbeat)
            {
                return;
            }

            float logIntervalSeconds = (WonderlandConfig.HeartbeatIntervalMinutes?.Value ?? 15f) * 60f;
            float discordMinutes = WonderlandConfig.DiscordHeartbeatIntervalMinutes?.Value ?? 0f;
            float discordIntervalSeconds = discordMinutes > 0f ? Mathf.Max(discordMinutes, 1f) * 60f : logIntervalSeconds;

            bool fireLog = logHeartbeat && _logTimer >= logIntervalSeconds;
            bool fireDiscord = discordHeartbeat && _discordTimer >= discordIntervalSeconds;
            if (!fireLog && !fireDiscord)
            {
                return;
            }

            var characters = ZNet.instance != null ? ConnectedCharacters.All() : new System.Collections.Generic.List<ConnectedCharacter>();
            int playerCount = characters.Count;
            string playerNames = string.Join(", ", characters.Select(c => c.Name));
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "(no world)";
            string uptime = FormatUptime(_uptimeSeconds);

            if (fireLog)
            {
                _logTimer = 0f;
                string who = playerCount > 0 ? playerNames : "none";
                WonderlandDebug.LogAlways($"[Heartbeat] '{world}' up {uptime} | {playerCount} player(s) online: {who} | Wonderland {WonderlandPlugin.ModVersion} running normally.");
            }

            if (fireDiscord)
            {
                _discordTimer = 0f;
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
