using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Wonderland.Core;
using Wonderland.Core.Data;
using Wonderland.Subsystems.DiscordNotify;
using Wonderland.Subsystems.ItemFlow;
using Wonderland.Subsystems.Security;

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
    /// tracking_since (the counters) are both declared so the reader knows which figures reset. The
    /// progression and enforcement blocks (0.10.x) are read fresh from the classifier and the synced config
    /// on every write - hot-reloaded values and live world keys, never cached here.
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
                "history). bosses_defeated / bosses_remaining cover the seven bosses (Eikthyr to Fader) and are the world's own progress, not " +
                "one player's kills; bosses_defeated_at only has dates for defeats seen since Wonderland 0.8.0.";
            doc["players"] = BuildPlayers(reg, stopping);
            doc["players_notes"] =
                "connected_seconds_alltime is server-measured connection time, not the character's playtime - never compare it with " +
                "another mod's playtime. sessions_alltime counts connections, not logins per day. deaths_alltime is deaths the " +
                "server saw. welcomed_at only exists for accounts first seen after 0.8.0. platform is the account platform they " +
                "last connected from - PC (Steam), Xbox (console or PC Game Pass), PlayStation, Switch 2 - or empty if unknown.";
            doc["players_not_achievements"] = new List<string> { "deaths_alltime", "sessions_alltime" };
            doc["lifetime"] = BuildLifetime(reg);
            doc["lifetime_notes"] =
                "Items moved by Wonderland's automation (fed to stations, vacuumed into chests, cached) - never a player's own " +
                "actions; never sum, rank or compare them across mods or against a player. raids_blocked and spawns_culled are " +
                "world-governor interventions. security_flags_total_alltime is server-side integrity flags - suspicions for the " +
                "admin, never verdicts, and deliberately not broken down per player.";
            // Both _notes are single strings: the BarrkBOT reader lifts a _notes key as attributed guidance
            // only when it is a string (valheimModExports.js dissect()); an array renders as a plain collection.
            doc["progression"] = BuildProgression(out List<string> progressionNotes);
            doc["progression_notes"] = string.Join(" ", progressionNotes);
            doc["enforcement"] = BuildEnforcement();
            doc["enforcement_notes"] = string.Join(" ", EnforcementNotes());
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
                // platform_id itself and the per-player security flag count stay in the .dat for the admin: the
                // bot has no reader for the id (only its platform half is exported, as a label), and a
                // per-player flag count would be ranked into a public "most flagged" answer from heuristics
                // that are suspicions, not verdicts.
                var row = new Dictionary<string, object>
                {
                    ["name"] = r.name,
                    ["online_now"] = !stopping && BarrkBotStats.OnlineIds.Contains(kv.Key),
                    ["platform"] = PeerPlatform.LabelForId(r.platform_id),
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

        // ---- progression + enforcement -------------------------------------------------------------


        // The ledger keys as last read while ZoneSystem was alive, for the shutdown write (see _lastBosses).
        private static List<string>? _lastBossKeys;

        /// <summary>The ledger as one ordered walk of ItemTierClassifier.NextUnlock from the baseline: each
        /// entry is the tier a boss unlocks, its global key and its display name, Swamp through DeepNorth.
        /// One source for unlocked_by, next_* and bosses_defeated_keys, so the three cannot disagree.</summary>
        private static List<(ItemTier Tier, string Key, string Boss)> Ledger()
        {
            var ledger = new List<(ItemTier, string, string)>();
            for (ItemTier tier = ItemTier.BlackForest; tier < ItemTier.DeepNorth; tier++)
            {
                (string Key, string Boss)? next = ItemTierClassifier.NextUnlock(tier);
                if (next == null) break;
                ledger.Add((tier + 1, next.Value.Key, next.Value.Boss));
            }
            return ledger;
        }

        /// <summary>The world's progression ceiling and what it gates, read at build time: MaxAllowedTier
        /// is hot-reloaded and the Auto ledger moves the moment a boss key lands. Item lists and counts
        /// are the classifier's derived table (empty until the first tick ObjectDB exists after a start; a
        /// ProgressionItemExemptions edit rewrites it within a second - enforcement itself sees the
        /// edit at once). ItemTier.None is written as "Unrestricted" wherever a tier is a string.</summary>
        private static Dictionary<string, object> BuildProgression(out List<string> notes)
        {
            notes = new List<string>();
            string configured = WonderlandConfig.MaxAllowedTier?.Value ?? "Auto";
            ItemTier effective = ItemTierClassifier.GetEffectiveMaxTier();
            bool on = effective >= ItemTier.Meadows && effective <= ItemTier.DeepNorth;
            string mode = !on ? "off" : configured.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) ? "auto" : "fixed";
            List<(ItemTier Tier, string Key, string Boss)> ledger = Ledger();

            var tiers = new List<string>();
            for (ItemTier t = ItemTier.Meadows; t <= ItemTier.DeepNorth; t++) tiers.Add(t.ToString());

            // Ledger order, not alphabetical: Swamp before Mountain is the whole point of the map.
            var unlockedBy = new Dictionary<string, object>();
            foreach ((ItemTier tier, string key, string boss) in ledger) unlockedBy[tier.ToString()] = boss;

            // Only Auto has a "next": a fixed ledger is lifted by the admin, not by a boss, and off gates nothing.
            (ItemTier Tier, string Key, string Boss)? next = null;
            if (mode == "auto")
            {
                foreach ((ItemTier tier, string key, string boss) in ledger)
                {
                    if (tier > effective) { next = (tier, key, boss); break; }
                }
            }

            // Every defeated_* key the ledger knows, Eikthyr included even though it lifts nothing (the
            // starter grant covers the Black Forest), read from the same world keys as bosses_defeated.
            if (ZoneSystem.instance != null)
            {
                var set = new List<string>();
                var keys = new List<string> { GlobalKeys.defeated_eikthyr.ToString() };
                foreach ((ItemTier tier, string key, string boss) in ledger) keys.Add(key);
                foreach (string key in keys)
                {
                    if (ZoneSystem.instance.GetGlobalKey(key)) set.Add(key);
                }
                _lastBossKeys = set;
            }
            List<string> bossKeys = _lastBossKeys ?? new List<string>();

            bool tableBuilt = ItemTierClassifier.LastTierCounts.Count > 0;
            var gatedTiers = new List<string>();
            var gatedByTier = new Dictionary<string, object>();
            int gatedCount = 0;
            if (on)
            {
                for (ItemTier t = effective + 1; t <= ItemTier.DeepNorth; t++)
                {
                    int n = TierCount(t);
                    gatedTiers.Add(t.ToString());
                    gatedByTier[t.ToString()] = n;
                    gatedCount += n;
                }
            }

            var byTier = new Dictionary<string, object> { ["Unrestricted"] = TierCount(ItemTier.None) };
            for (ItemTier t = ItemTier.Meadows; t <= ItemTier.DeepNorth; t++) byTier[t.ToString()] = TierCount(t);
            byTier["Cheat"] = TierCount(ItemTier.Cheat);

            notes.Add(
                "tier is the progression ceiling in force on this world right now, never one player's progress: mode auto follows " +
                "the world's own boss keys (Eikthyr is not on the ledger - the starter grant covers the Black Forest), fixed is " +
                "pinned by the admin, off means no ceiling. Tiers come from the game's own recipe, smelter and station data at " +
                "world start, not from item names: raw drops are unrestricted whatever biome they came from; only processed and " +
                "crafted items carry a tier.");
            notes.Add(
                "gated means above tier: gated_tiers, gated_items_count and gated_items_by_tier are what a player would be " +
                "flagged for right now. Every count here is items in the game's item database, not items in the world, not " +
                "anyone's actions and not a ranking. next_boss, next_key and next_tier are empty strings when no boss lifts " +
                "the ledger - DeepNorth reached, or mode fixed or off - which is a fact, not a missing reading.");
            notes.Add(
                "items_by_tier_count is the derived table; the item names are not in this file - tier_table_file has every " +
                "item with its tier and reason as text, tier_json_file the same as JSON. Both are written on the first tick the " +
                "game's item database exists after a start, and rewritten within a second of a ProgressionItemExemptions or " +
                "BannedItemsList edit, which takes effect on enforcement at the same moment.");
            if (!tableBuilt)
            {
                notes.Add("The tier table has not been built yet - it is derived on the first tick after start that the game's item database exists - so the counts are 0 until then.");
            }
            if (!on && !string.IsNullOrWhiteSpace(configured) && !configured.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"MaxAllowedTier is '{configured}', which names no tier, so no progression ceiling is in force until it is corrected.");
            }

            return new Dictionary<string, object>
            {
                ["mode"] = mode,
                ["configured"] = configured,
                ["tier"] = on ? effective.ToString() : "",
                ["tier_index"] = on ? (int)effective : 0,
                ["tiers"] = tiers,
                ["unlocked_by"] = unlockedBy,
                // "" = nothing lifts the ledger (DeepNorth, fixed, off). Not null: the reader words a null as "not
                // recorded yet", and this is a fact, not a missing reading.
                ["next_boss"] = next?.Boss ?? "",
                ["next_key"] = next?.Key ?? "",
                ["next_tier"] = next?.Tier.ToString() ?? "",
                ["bosses_defeated_keys"] = bossKeys,
                ["gated_tiers"] = gatedTiers,
                ["gated_items_count"] = gatedCount,
                ["gated_items_by_tier"] = gatedByTier,
                ["items_by_tier_count"] = byTier,
                ["tier_table_file"] = ItemTierClassifier.AuditFilePath,
                // The names themselves live beside this file (Wonderland/progression_tiers.json, not a
                // barrkbot_* name so the scanner never sees two files): 160-260 gated names would push this
                // whole block past the local provider's 6,000-character tool-result slice.
                ["tier_json_file"] = ItemTierClassifier.TierJsonPath,
            };
        }

        private static int TierCount(ItemTier tier)
        {
            return ItemTierClassifier.LastTierCounts.TryGetValue(tier, out int n) ? n : 0;
        }

        /// <summary>What the guards do when they find something, straight from the synced config at build
        /// time (all hot-reloaded). Booleans only, plus the two admin lists - no counters: violations are
        /// security_flags_total_alltime and are deliberately not broken down here.</summary>
        private static Dictionary<string, object> BuildEnforcement()
        {
            return new Dictionary<string, object>
            {
                ["container_sweep"] = WonderlandConfig.ItemIntegritySweepEnabled?.Value == true,
                ["container_sweep_removes"] = WonderlandConfig.ItemIntegritySweepCorrect?.Value == true,
                ["equipment_guard"] = WonderlandConfig.EquipmentGuardEnabled?.Value == true,
                ["equipment_guard_kicks"] = WonderlandConfig.EquipmentGuardKick?.Value == true,
                ["quality_guard"] = WonderlandConfig.EquipmentGuardEnforceQuality?.Value == true,
                ["admin_bypass"] = WonderlandConfig.EquipmentGuardAdminBypass?.Value == true,
                ["vanilla_client"] = WonderlandConfig.ModEnforcementEnabled?.Value == true,
                ["vanilla_client_kicks"] = WonderlandConfig.ModEnforcementKick?.Value == true,
                ["vanilla_client_admin_bypass"] = WonderlandConfig.ModEnforcementAdminBypass?.Value == true,
                ["strict_version"] = WonderlandConfig.ModEnforcementStrictVersion?.Value == true,
                ["active_probe"] = WonderlandConfig.ModEnforcementActiveProbe?.Value == true,
                ["routed_rpc"] = WonderlandConfig.ModEnforcementInspectRoutedRpc?.Value == true,
                ["placement_guard"] = WonderlandConfig.ModEnforcementPlacementGuard?.Value == true,
                ["max_plant_batch"] = WonderlandConfig.ModEnforcementMaxPlantBatch?.Value ?? 0,
                ["banned_items"] = SplitList(WonderlandConfig.BannedItemsList?.Value),
                ["pinned_items"] = SplitList(WonderlandConfig.ProgressionItemExemptions?.Value),
            };
        }

        private static List<string> EnforcementNotes()
        {
            return new List<string>
            {
                "Each flag is the live setting: what enforcement does when it finds a gated, banned or implausible item, or a " +
                "non-vanilla client. container_sweep checks player-built chests (a player's own bag is never networked to the " +
                "server) and container_sweep_removes says whether a find is removed or only logged; equipment_guard watches what " +
                "characters have equipped and equipment_guard_kicks says whether the wearer is disconnected or only logged; " +
                "quality_guard flags gear upgraded past its legitimate maximum; admin_bypass exempts authenticated admins from " +
                "every equipment, quality and progression check.",
                "vanilla_client is the vanilla-only client check and vanilla_client_kicks says whether a modded client is " +
                "disconnected or only logged; strict_version, active_probe, routed_rpc and placement_guard are its detectors, " +
                "max_plant_batch the placements allowed in one half-second burst, vanilla_client_admin_bypass exempts admins.",
                "banned_items are prohibited at every tier, in every mode (the game's own cheat prefabs are banned regardless of " +
                "the list); pinned_items are the admin's overrides to the derived tier table - a bare prefab is freed at any tier, " +
                "Prefab:Tier pins it. Nothing here is a player action, a count of violations or a ranking; violations are only " +
                "security_flags_total_alltime, and those are suspicions, not verdicts.",
            };
        }

        /// <summary>The config's comma/semicolon list as the classifier reads it (EnsureExemptionsLoaded):
        /// split, trimmed, empties dropped, order kept.</summary>
        private static List<string> SplitList(string? value)
        {
            var list = new List<string>();
            foreach (string part in (value ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) list.Add(trimmed);
            }
            return list;
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
