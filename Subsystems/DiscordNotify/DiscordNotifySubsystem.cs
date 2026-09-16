using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.DiscordNotify
{
    public class DiscordNotifySubsystem : IWonderlandSubsystem
    {
        public string Name => "DiscordNotify";
        public bool IsEnabled => true;

        // Global keys the pre-0.8.0 first-join detector wrote into the world; removed once on load now
        // that first join is decided from vanilla's own ZNet.World.m_playerHistory instead.
        private const string LegacySeenKeyPrefix = "wonderland_discord_seen_";

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(PeerJoinLeaveHook));
            SubsystemRegistry.SafePatch(harmony, typeof(BossDefeatWatch));
            SubsystemRegistry.SafePatch(harmony, typeof(WorldLoadedHook));
        }

        /// <summary>ZNetScene.Awake - the save has NOT been read yet at this point (ZNet.Start runs
        /// ServerLoadWorld later), so nothing that depends on world state belongs here; see OnWorldLoaded.</summary>
        public void OnWorldReady()
        {
        }

        /// <summary>ZNet.ServerLoadWorld postfix (WorldLoadedHook): every persisted global key and the
        /// player history are in memory now, so this is where boot-time state is sampled and where the
        /// "world finished loading" message the config promises is actually sent.</summary>
        public static void OnWorldLoaded()
        {
            BossDefeatWatch.SnapshotExistingBossKeys();
            RemoveLegacySeenKeys();
            LogSetupSummary();

            if (ZNet.m_loadError)
            {
                WonderlandDebug.LogWarning("[DiscordNotify] world load reported an error - skipping the server-online post.");
                return;
            }

            if (WonderlandConfig.DiscordNotifyServerStatus?.Value == true && ZoneSystem.instance != null)
            {
                // ZoneSystem.GenerateLocationsCompleted's add-accessor invokes the handler immediately when
                // locations already exist (every existing world) and otherwise queues it until the
                // time-sliced generation coroutine finishes (a brand-new world) - the same event vanilla's
                // own ServerLoadWorld uses to decide when to OpenServer, subscribed just before this one.
                ZoneSystem.instance.GenerateLocationsCompleted += SendServerOnline;
            }
        }

        private static void SendServerOnline()
        {
            if (ZoneSystem.instance != null)
            {
                ZoneSystem.instance.GenerateLocationsCompleted -= SendServerOnline;
            }
            DiscordWebhook.Send(Fill(WonderlandConfig.DiscordServerOnlineMessage?.Value));
        }

        private static void RemoveLegacySeenKeys()
        {
            try
            {
                if (ZoneSystem.instance == null)
                {
                    return;
                }

                int removed = 0;
                foreach (string key in ZoneSystem.instance.GetGlobalKeys())
                {
                    if (key.StartsWith(LegacySeenKeyPrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        ZoneSystem.instance.RemoveGlobalKey(key);
                        removed++;
                    }
                }

                if (removed > 0)
                {
                    WonderlandDebug.LogAlways($"[DiscordNotify] removed {removed} stale '{LegacySeenKeyPrefix}*' global key(s) left by the old first-join tracker.");
                }
            }
            catch (System.Exception ex)
            {
                WonderlandDebug.LogWarning($"[DiscordNotify] legacy seen-key cleanup failed: {ex.Message}");
            }
        }

        /// <summary>One line so the log itself answers "why is nothing / only some of it reaching Discord".</summary>
        private static void LogSetupSummary()
        {
            bool enabled = WonderlandConfig.DiscordNotifyEnabled?.Value == true;
            bool hasUrl = !string.IsNullOrWhiteSpace(WonderlandConfig.DiscordWebhookUrl?.Value);
            string webhook = !hasUrl
                ? "NOT SET - nothing will be posted until DiscordWebhookUrl is filled in" + (enabled ? "" : " (and DiscordNotifyEnabled is also false)")
                : (enabled ? "yes" : "set but DiscordNotifyEnabled = false");

            WonderlandDebug.LogAlways(
                $"[DiscordNotify] webhook configured: {webhook}"
                + $" | server status: {OnOff(WonderlandConfig.DiscordNotifyServerStatus, "DiscordNotifyServerStatus")}"
                + $" | logins: {OnOff(WonderlandConfig.DiscordNotifyLogins, "DiscordNotifyLogins")}"
                + $" | deaths: {OnOff(WonderlandConfig.DiscordNotifyDeaths, "DiscordNotifyDeaths")}"
                + $" | first join: {OnOff(WonderlandConfig.DiscordNotifyFirstJoin, "DiscordNotifyFirstJoin")}"
                + $" | boss defeats: {OnOff(WonderlandConfig.DiscordNotifyBossDefeats, "DiscordNotifyBossDefeats")}"
                + $" | heartbeat: {OnOff(WonderlandConfig.DiscordNotifyHeartbeat, "DiscordNotifyHeartbeat")}"
                + $" | interval: {DescribeHeartbeatInterval()}"
                + $" | avatar: {(string.IsNullOrWhiteSpace(WonderlandConfig.DiscordAvatarUrl?.Value) ? "webhook default" : "custom")}"
                + $" | mention: {(string.IsNullOrWhiteSpace(WonderlandConfig.DiscordMention?.Value) ? "none" : WonderlandConfig.DiscordMention!.Value.Trim())}");
        }

        private static string DescribeHeartbeatInterval()
        {
            float discordMinutes = WonderlandConfig.DiscordHeartbeatIntervalMinutes?.Value ?? 0f;
            return discordMinutes > 0f
                ? $"{discordMinutes:0.#} min (DiscordHeartbeatIntervalMinutes)"
                : $"{WonderlandConfig.HeartbeatIntervalMinutes?.Value ?? 15f:0.#} min (HeartbeatIntervalMinutes, shared with the log heartbeat)";
        }

        private static string OnOff(ConfigEntry<bool>? entry, string settingName)
        {
            return entry?.Value == true ? "on" : $"off ({settingName})";
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
                DiscordWebhook.SendBlocking(Fill(WonderlandConfig.DiscordServerOfflineMessage?.Value));
            }
        }

        /// <summary>platform is PeerPlatform.Label for the peer - "PC", "Xbox", "PlayStation", "Switch 2" or ""
        /// when the game did not say - shown in the log line and offered to the templates as {platform}.</summary>
        public static void AnnounceJoin(string playerName, string platform)
        {
            WonderlandDebug.LogAlways($"[DiscordNotify] {Who(playerName, platform)} connected.");
            if (WonderlandConfig.DiscordNotifyLogins?.Value == true)
            {
                DiscordWebhook.Send(Fill(WonderlandConfig.DiscordJoinMessage?.Value, player: playerName, platform: platform));
            }
        }

        /// <summary>The leaving peer is still in ZNet's peer list when ZNet.Disconnect's prefix runs, so
        /// it is excluded from the roster explicitly - "{playercount} online" means after they've gone.</summary>
        public static void AnnounceLeave(string playerName, string platform, ZNetPeer leavingPeer)
        {
            WonderlandDebug.LogAlways($"[DiscordNotify] {Who(playerName, platform)} disconnected.");
            if (WonderlandConfig.DiscordNotifyLogins?.Value == true)
            {
                DiscordWebhook.Send(Fill(WonderlandConfig.DiscordLeaveMessage?.Value, player: playerName, platform: platform, excludePeer: leavingPeer));
            }
        }

        public static void AnnounceDeath(string playerName, string platform)
        {
            // Always logged locally, regardless of the Discord toggle below or whether a webhook is even
            // configured - otherwise a server with no webhook set up would have zero record of this at all.
            WonderlandDebug.LogAlways($"[DiscordNotify] {Who(playerName, platform)} died.");
            if (WonderlandConfig.DiscordNotifyDeaths?.Value == true)
            {
                DiscordWebhook.Send(Fill(WonderlandConfig.DiscordDeathMessage?.Value, player: playerName, platform: platform));
            }
        }

        public static void AnnounceFirstJoin(string playerName, string platform)
        {
            WonderlandDebug.LogAlways($"[DiscordNotify] {Who(playerName, platform)} joined this world for the first time.");
            if (WonderlandConfig.DiscordNotifyFirstJoin?.Value == true)
            {
                DiscordWebhook.Send(Fill(WonderlandConfig.DiscordFirstJoinMessage?.Value, player: playerName, platform: platform));
            }
        }

        /// <summary>'Alice' (PC) - or just 'Alice' when the platform is unknown - for the log lines.</summary>
        private static string Who(string playerName, string platform)
        {
            return string.IsNullOrEmpty(platform) ? $"'{playerName}'" : $"'{playerName}' ({platform})";
        }

        public static void AnnounceBossDefeat(string bossName)
        {
            WonderlandDebug.LogAlways($"[DiscordNotify] boss defeated: {bossName}.");
            if (WonderlandConfig.DiscordNotifyBossDefeats?.Value == true)
            {
                DiscordWebhook.Send(Fill(WonderlandConfig.DiscordBossDefeatMessage?.Value, boss: bossName));
            }
        }

        /// <summary>Called from Heartbeat on its timer (HeartbeatIntervalMinutes, or DiscordHeartbeatIntervalMinutes
        /// when that is set) - this does not run its own timer, so there is nothing here to gate against spam
        /// beyond the toggle. The roster is the one Heartbeat already computed for its log line, so the two agree.</summary>
        public static void AnnounceHeartbeat(string uptime, int playerCount, string playerNames)
        {
            if (WonderlandConfig.DiscordNotifyHeartbeat?.Value != true)
            {
                return;
            }

            DiscordWebhook.Send(Fill(WonderlandConfig.DiscordHeartbeatMessage?.Value, uptime: uptime, playerCount: playerCount, players: playerNames));
        }

        /// <summary>
        /// Fills every placeholder any template may use - {player} {platform} {boss} {world} {uptime}
        /// {playercount} {players} {time} {version} {mention} - so an admin can put any of them in any message.
        /// {time} is a Discord timestamp markup (&lt;t:unix:R&gt;), which the client renders as a live "5 minutes
        /// ago" in the reader's own timezone. {platform} is where {player} plays from (PeerPlatform.Label), and
        /// every name in {players} carries its own in brackets. The roster comes from ZNet's peer list rather
        /// than ConnectedCharacters because a join is announced from RPC_PeerInfo, before the joining player's
        /// character ZDO exists - the peer is already ready and named at that point, so "{playercount}
        /// online" includes them. Lines are right-trimmed so an empty {mention} leaves no dangling space, and
        /// when the platform is unknown the " ({platform})" the default headlines carry collapses to nothing
        /// rather than posting "**Rohan** () joined".
        /// Substitution is a single pass over the template, so a value is never re-scanned - a character
        /// named "{mention}" or "{world}" stays literal text - and every player-supplied string has a
        /// zero-width space inserted after each '@' before insertion, which turns "@everyone", "@here"
        /// and "&lt;@&amp;id&gt;" into plain text Discord will not parse as a mention. ZNet.RPC_PeerInfo stores
        /// m_playerName verbatim from the client, so names are untrusted input here.
        /// </summary>
        private static string Fill(string? template, string? player = null, string? platform = null, string? boss = null,
            string? uptime = null, ZNetPeer? excludePeer = null, int? playerCount = null, string? players = null)
        {
            if (string.IsNullOrEmpty(template))
            {
                return "";
            }

            if (playerCount == null || players == null)
            {
                (int count, string names) = OnlineRoster(excludePeer);
                playerCount ??= count;
                players ??= names;
            }

            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            var values = new Dictionary<string, string>
            {
                ["player"] = Neutralize(player),
                ["platform"] = platform ?? "",
                ["boss"] = boss ?? "",
                ["world"] = Neutralize(world),
                ["uptime"] = uptime ?? "",
                ["playercount"] = playerCount.Value.ToString(),
                ["players"] = playerCount.Value > 0 && !string.IsNullOrEmpty(players) ? Neutralize(players) : "none",
                ["time"] = $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>",
                ["version"] = WonderlandPlugin.ModVersion,
                ["mention"] = WonderlandConfig.DiscordMention?.Value?.Trim() ?? "",
            };
            string filled = PlaceholderPattern.Replace(template!, m => values[m.Groups[1].Value]);
            if (string.IsNullOrEmpty(platform) && template!.Contains("({platform})"))
            {
                filled = filled.Replace(" ()", "").Replace("()", "");
            }

            return string.Join("\n", filled.Split('\n').Select(line => line.TrimEnd()));
        }

        private static readonly Regex PlaceholderPattern =
            new Regex(@"\{(player|platform|boss|world|uptime|playercount|players|time|version|mention)\}", RegexOptions.Compiled);

        /// <summary>Untrusted text can never ping: "@" becomes "@" + U+200B, which Discord renders as-is
        /// and does not parse as @everyone / @here / &lt;@id&gt;.</summary>
        private static string Neutralize(string? value)
        {
            return string.IsNullOrEmpty(value) ? "" : value!.Replace("@", "@\u200B");
        }

        private static (int count, string names) OnlineRoster(ZNetPeer? excludePeer)
        {
            var names = new List<string>();
            if (ZNet.instance != null)
            {
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    if (peer != null && peer != excludePeer && peer.IsReady() && !string.IsNullOrEmpty(peer.m_playerName))
                    {
                        names.Add(PeerPlatform.WithLabel(peer.m_playerName, peer));
                    }
                }
            }
            return (names.Count, string.Join(", ", names));
        }
    }
}
