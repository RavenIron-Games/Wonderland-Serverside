using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Wonderland.Core;
using Wonderland.Core.Data;
using Wonderland.Subsystems.DiscordNotify;
using Wonderland.Subsystems.ItemFlow;

namespace Wonderland.Subsystems.BarrkBot
{
    /// <summary>
    /// Writes BepInEx/config/Wonderland/barrkbot_wonderland.json in the shape BarrkBOT 6.0.119 reads
    /// (libs-Tools/IMPLEMENTATIONS/BarrkBOTExports.md, re-checked against
    /// WindowsDEV/Discord-BarrkBOT/src/actions/valheimModData.js + valheimModExports.js): generated_at with a
    /// Z, source = name + version, schema_version, intervals, a players map keyed by the stable player id
    /// with the display name inside each row, units in every field name, and *_notes keys for every caveat
    /// (those are lifted out as guidance, never read as data). The file is written temp-then-rename so the
    /// sweep never parses a half-written file. Lifetime counters live in the registry beside the config
    /// (BarrkBotStats), so a restart does not blank the export: session_started_at (the process) and
    /// tracking_since (the counters) are both declared so the reader knows which figures reset.
    /// </summary>
    public static class BarrkBotExport
    {
        public const string FileName = "barrkbot_wonderland.json";

        public static string FilePath()
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "Wonderland", FileName);
        }

        /// <summary>Rewrites the export. stopping = the server is shutting down: the file then says
        /// online=false with nobody on, because the reader only starts doubting a file after 60 minutes
        /// (valheimModExports.js STALE_AFTER_MINUTES) and would otherwise report the last roster as online
        /// for that long.</summary>
        public static void Write(bool stopping = false)
        {
            string path = FilePath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                BarrkBotStats.WriteAtomic(path, JsonConvert.SerializeObject(Build(stopping), Formatting.Indented));
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[BarrkBot] export write failed: {ex.Message}");
            }
        }

        /// <summary>BarrkBotExportEnabled switched off at runtime: a file left behind would keep answering
        /// as if live, so it goes; the reader then says the mod has no data, which is the truth.</summary>
        public static void Remove()
        {
            string path = FilePath();
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[BarrkBot] could not remove {path}: {ex.Message}");
            }
        }

        /// <summary>The document, as an ordered dictionary tree so the JSON reads top-down the way the
        /// reader's overview renders it: metadata, the live server block, players, lifetime counters.
        /// Shape rules from the reader source (valheimModExports.js, 6.0.119): schema_version 3; ids as
        /// strings (JSON.parse rounds 64-bit ints); no block-level generated_at on the live block (its
        /// zeros would render as "none so far in this one"); session_started_at + tracking_since together
        /// with a block named "lifetime" and *_alltime fields, or the bot claims every figure resets on
        /// restart; no field name shared between two scopes; floats rounded; no nested objects in rows.</summary>
        public static Dictionary<string, object> Build(bool stopping = false)
        {
            string now = BarrkBotStats.Now();
            BarrkBotStats.Registry reg = BarrkBotStats.Current;
            float writeSeconds = BarrkBotSubsystem.EffectiveWriteSeconds();

            var doc = new Dictionary<string, object>
            {
                ["schema_version"] = 3,
                ["generated_at"] = now,
                ["source"] = $"{WonderlandPlugin.ModName} {WonderlandPlugin.ModVersion}",
                ["intervals"] = new Dictionary<string, object> { ["write_seconds"] = (int)writeSeconds },
                ["session_started_at"] = Heartbeat.StartedAtUtc,
                ["tracking_since"] = reg.tracking_since,
                ["export_notes"] =
                    "All figures are measured by the server itself, never reported by a client. server is live and resets on " +
                    "restart (session_started_at); players and lifetime are cumulative since tracking_since. An empty players " +
                    "map means nothing recorded yet, not nobody.",
            };

            doc["server"] = BuildServer(stopping);
            doc["server_notes"] =
                "online false means the server was stopped cleanly and players_online is then 0 by definition. players_online is who " +
                "is connected right now. known_accounts is how many distinct accounts have ever joined this world (the game's own " +
                "history). bosses_defeated / bosses_remaining cover the five classic bosses and are the world's own progress, not " +
                "one player's kills; bosses_defeated_at only has dates for defeats seen since Wonderland 0.8.0.";
            doc["players"] = BuildPlayers(reg, stopping);
            doc["players_notes"] =
                "connected_seconds_alltime is server-measured connection time, not the character's playtime - never compare it with " +
                "another mod's playtime. sessions_alltime counts connections, not logins per day. deaths_alltime is deaths the " +
                "server saw. welcomed_at only exists for accounts first seen after 0.8.0.";
            doc["players_not_achievements"] = new List<string> { "deaths_alltime", "sessions_alltime" };
            doc["lifetime"] = BuildLifetime(reg);
            doc["lifetime_notes"] =
                "Items moved by Wonderland's automation (fed to stations, vacuumed into chests, cached) - never a player's own " +
                "actions; never sum, rank or compare them across mods or against a player. raids_blocked and spawns_culled are " +
                "world-governor interventions. security_flags_total_alltime is server-side integrity flags - suspicions for the " +
                "admin, never verdicts, and deliberately not broken down per player.";
            return doc;
        }

        // World identity/progress as last read while the game singletons were alive. Plugin.OnDestroy runs in
        // no defined order relative to ZNet/ZoneSystem/EnvMan's own OnDestroy, so the shutdown write reuses
        // these rather than emitting world_name "" / every boss "remaining" if they are already gone.
        private static string _lastWorldName = "";
        private static object _lastWorldDay = null!;
        private static int _lastKnownAccounts;
        private static (List<string> defeated, List<string> remaining)? _lastBosses;

        private static Dictionary<string, object> BuildServer(bool stopping)
        {
            // Same base as online_now and the peak (ids whose connection is up as of the last sweep), so the
            // server block and the rows never disagree about who is on - a dead, respawning player has no
            // character ZDO for 8-18 s but is still connected.
            var onlineNames = stopping ? new List<string>() : BarrkBotStats.OnlineNames();

            if (ZNet.instance != null && !string.IsNullOrEmpty(ZNet.instance.GetWorldName())) _lastWorldName = ZNet.instance.GetWorldName();
            if (EnvMan.instance != null) _lastWorldDay = EnvMan.instance.GetCurrentDay();
            if (ZNet.World != null) _lastKnownAccounts = ZNet.World.m_playerHistory.Count;
            if (ZoneSystem.instance != null) _lastBosses = BossDefeatWatch.Snapshot();
            (List<string> defeated, List<string> remaining) = _lastBosses ?? BossDefeatWatch.Snapshot();

            return new Dictionary<string, object>
            {
                ["name"] = ServerName(),
                ["world_name"] = _lastWorldName,
                ["online"] = !stopping,
                // null = not measured (the contract's word for it); 0 would render as "day 0".
                ["world_day"] = _lastWorldDay, // serialises as JSON null until EnvMan has been seen
                ["known_accounts"] = _lastKnownAccounts,
                ["uptime_seconds"] = (long)Heartbeat.UptimeSeconds,
                ["players_online"] = onlineNames.Count,
                ["players_online_names"] = onlineNames,
                ["peak_players_online"] = BarrkBotStats.Current.peak_players_online_count,
                ["wonderland_version"] = WonderlandPlugin.ModVersion,
                ["carry_weight_multiplier"] = Math.Round((double)(WonderlandConfig.CarryWeightMultiplier?.Value ?? 1f), 3),
                ["stamina_regen_multiplier"] = Math.Round((double)(WonderlandConfig.StaminaRegenRateMultiplier?.Value ?? 1f), 3),
                ["bosses_defeated"] = defeated,
                ["bosses_remaining"] = remaining,
                ["bosses_defeated_at"] = new SortedDictionary<string, string>(BarrkBotStats.Current.boss_defeated_at),
                ["stations_switched_off"] = SupplySwitch.Count,
            };
        }

        private static readonly System.Reflection.FieldInfo ServerNameField =
            HarmonyLib.AccessTools.Field(typeof(ZNet), "m_ServerName");

        /// <summary>The dedicated server's -name string (ZNet.m_ServerName, private static, set by SetServer) -
        /// a cross-check for a reader that sweeps more than one server root; "" if unreadable, never absent.</summary>
        private static string ServerName()
        {
            try
            {
                return ServerNameField?.GetValue(null) as string ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static Dictionary<string, object> BuildPlayers(BarrkBotStats.Registry reg, bool stopping)
        {
            var players = new Dictionary<string, object>();
            foreach (KeyValuePair<string, BarrkBotStats.PlayerRow> kv in reg.players.OrderBy(p => p.Value.name, StringComparer.OrdinalIgnoreCase))
            {
                BarrkBotStats.PlayerRow r = kv.Value;
                if (string.IsNullOrEmpty(r.name))
                {
                    continue; // a row without a name would stop the whole map being read as per-player
                }
                // platform_id and the per-player security flag count stay in the .dat for the admin: the bot
                // has no reader for the id, and a per-player flag count would be ranked into a public
                // "most flagged" answer from heuristics that are suspicions, not verdicts.
                var row = new Dictionary<string, object>
                {
                    ["name"] = r.name,
                    ["online_now"] = !stopping && BarrkBotStats.OnlineIds.Contains(kv.Key),
                    ["first_seen_at"] = r.first_seen_at,
                    ["last_seen_at"] = r.last_seen_at,
                    ["sessions_alltime"] = r.sessions_count,
                    ["connected_seconds_alltime"] = r.online_seconds,
                    ["deaths_alltime"] = r.deaths_count,
                };
                if (!string.IsNullOrEmpty(r.welcomed_at)) row["welcomed_at"] = r.welcomed_at;
                players[kv.Key] = row;
            }
            return players;
        }

        private static Dictionary<string, object> BuildLifetime(BarrkBotStats.Registry reg)
        {
            long Sub(string tag) => reg.transfers_by_subsystem.TryGetValue(tag, out long v) ? v : 0L;
            SortedDictionary<string, long> ByItem(string tag) =>
                reg.transfers_by_item.TryGetValue(tag, out Dictionary<string, long> d) ? new SortedDictionary<string, long>(d) : new SortedDictionary<string, long>();

            return new Dictionary<string, object>
            {
                ["production_supply_items_fed_alltime"] = Sub("ProductionSupply"),
                ["production_supply_items_fed_by_item"] = ByItem("ProductionSupply"),
                ["vacuum_items_moved_alltime"] = Sub(ItemFlow.VacuumEngine.VacuumTag) + Sub(ItemFlow.VacuumEngine.HarvestTag),
                ["vacuum_items_moved_by_item"] = Merge(ByItem(ItemFlow.VacuumEngine.VacuumTag), ByItem(ItemFlow.VacuumEngine.HarvestTag)),
                ["item_cache_items_stored_alltime"] = Sub("ItemCache:Store"),
                ["item_cache_items_returned_alltime"] = Sub("ItemCache:Drain") + Sub("ItemCache:Claim"),
                ["starter_kit_items_granted_alltime"] = Sub("FirstSpawnGrant"),
                ["raids_blocked_alltime"] = reg.raids_blocked_count,
                ["spawns_culled_alltime"] = reg.spawns_culled_count,
                ["security_flags_total_alltime"] = reg.security_flags_count,
            };
        }

        private static SortedDictionary<string, long> Merge(SortedDictionary<string, long> a, SortedDictionary<string, long> b)
        {
            foreach (KeyValuePair<string, long> kv in b)
            {
                a[kv.Key] = (a.TryGetValue(kv.Key, out long v) ? v : 0L) + kv.Value;
            }
            return a;
        }
    }
}
