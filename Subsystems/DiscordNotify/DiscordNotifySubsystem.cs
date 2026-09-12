using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;

namespace Wonderland.Subsystems.DiscordNotify
{
    public class DiscordNotifySubsystem : IWonderlandSubsystem
    {
        public string Name => "DiscordNotify";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(PeerJoinLeaveHook));
            SubsystemRegistry.SafePatch(harmony, typeof(BossDefeatWatch));
        }

        public void OnWorldReady()
        {
            BossDefeatWatch.SnapshotExistingBossKeys();

            if (WonderlandConfig.DiscordNotifyServerStatus?.Value == true)
            {
                DiscordWebhook.Send(FormatServerMessage(WonderlandConfig.DiscordServerOnlineMessage?.Value));
            }
        }

        public void OnUpdate()
        {
            PlayerLifecycleWatch.OnUpdate(UnityEngine.Time.deltaTime);
        }

        // Called from WonderlandPlugin.OnDestroy - the process may exit right after this returns,
        // so the offline message is sent with SendBlocking rather than fire-and-forget.
        public void Shutdown()
        {
            if (WonderlandConfig.DiscordNotifyServerStatus?.Value == true)
            {
                DiscordWebhook.SendBlocking(FormatServerMessage(WonderlandConfig.DiscordServerOfflineMessage?.Value));
            }
        }

        public static void AnnounceJoin(string playerName)
        {
            WonderlandDebug.LogAlways($"[DiscordNotify] '{playerName}' connected.");
            if (WonderlandConfig.DiscordNotifyLogins?.Value == true)
            {
                DiscordWebhook.Send(FormatPlayerMessage(WonderlandConfig.DiscordJoinMessage?.Value, playerName));
            }
        }

        public static void AnnounceLeave(string playerName)
        {
            WonderlandDebug.LogAlways($"[DiscordNotify] '{playerName}' disconnected.");
            if (WonderlandConfig.DiscordNotifyLogins?.Value == true)
            {
                DiscordWebhook.Send(FormatPlayerMessage(WonderlandConfig.DiscordLeaveMessage?.Value, playerName));
            }
        }

        public static void AnnounceDeath(string playerName)
        {
            // Always logged locally, regardless of the Discord toggle below or whether a webhook is even
            // configured - otherwise a server with no webhook set up would have zero record of this at all.
            WonderlandDebug.LogAlways($"[DiscordNotify] '{playerName}' died.");
            if (WonderlandConfig.DiscordNotifyDeaths?.Value == true)
            {
                DiscordWebhook.Send(FormatPlayerWorldMessage(WonderlandConfig.DiscordDeathMessage?.Value, "{player}", playerName));
            }
        }

        public static void AnnounceFirstJoin(string playerName)
        {
            WonderlandDebug.LogAlways($"[DiscordNotify] '{playerName}' joined this world for the first time.");
            if (WonderlandConfig.DiscordNotifyFirstJoin?.Value == true)
            {
                DiscordWebhook.Send(FormatPlayerWorldMessage(WonderlandConfig.DiscordFirstJoinMessage?.Value, "{player}", playerName));
            }
        }

        public static void AnnounceBossDefeat(string bossName)
        {
            WonderlandDebug.LogAlways($"[DiscordNotify] boss defeated: {bossName}.");
            if (WonderlandConfig.DiscordNotifyBossDefeats?.Value == true)
            {
                DiscordWebhook.Send(FormatPlayerWorldMessage(WonderlandConfig.DiscordBossDefeatMessage?.Value, "{boss}", bossName));
            }
        }

        /// <summary>Called from Heartbeat on its own interval (section 1's HeartbeatIntervalMinutes) - this
        /// does not run its own timer, so there is nothing here to gate against spam beyond the toggle.</summary>
        public static void AnnounceHeartbeat(string uptime, int playerCount, string playerNames)
        {
            if (WonderlandConfig.DiscordNotifyHeartbeat?.Value != true)
            {
                return;
            }

            string template = WonderlandConfig.DiscordHeartbeatMessage?.Value ?? "";
            string worldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            string message = template
                .Replace("{world}", worldName)
                .Replace("{uptime}", uptime)
                .Replace("{playercount}", playerCount.ToString())
                .Replace("{players}", playerCount > 0 ? playerNames : "none");
            DiscordWebhook.Send(message);
        }

        private static string FormatPlayerMessage(string template, string playerName)
        {
            return (template ?? "").Replace("{player}", playerName);
        }

        private static string FormatServerMessage(string template)
        {
            string worldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            return (template ?? "").Replace("{world}", worldName);
        }

        /// <summary>Fills {world} plus one caller-supplied placeholder ({player} or {boss}).</summary>
        private static string FormatPlayerWorldMessage(string template, string placeholder, string value)
        {
            string worldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            return (template ?? "").Replace(placeholder, value).Replace("{world}", worldName);
        }
    }
}
