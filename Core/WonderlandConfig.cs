using System.Collections.Generic;
using BepInEx.Configuration;
using ServerSync;

namespace Wonderland.Core
{
    public static class WonderlandConfig
    {
        public static ConfigEntry<bool>? ServerConfigLocked;
        public static ConfigEntry<bool>? VerboseLogging;

        // Item flow - vacuum / auto-harvest
        public static ConfigEntry<bool>? VacuumEnabled;
        public static ConfigEntry<float>? VacuumInterval;
        public static ConfigEntry<int>? VacuumBatchSize;
        public static ConfigEntry<float>? VacuumRadius;
        public static ConfigEntry<float>? ContainerLinkRadius;
        public static ConfigEntry<string>? VacuumExcludedContainers;
        public static ConfigEntry<string>? VacuumExcludedItems;
        public static ConfigEntry<bool>? AutoHarvestEnabled;
        public static ConfigEntry<float>? AutoHarvestRadius;

        // Item flow - production supply
        public static ConfigEntry<bool>? ProductionSupplyEnabled;
        public static ConfigEntry<float>? ProductionSupplyInterval;
        public static ConfigEntry<int>? ProductionSupplyBatchSize;
        public static ConfigEntry<float>? ProductionSupplyRange;
        public static ConfigEntry<int>? ProductionSupplyReserve;

        // Item flow - sort
        public static ConfigEntry<bool>? SortEnabled;
        public static ConfigEntry<float>? SortInterval;
        public static ConfigEntry<int>? SortBatchSize;

        // Storage - stack size
        public static ConfigEntry<bool>? StackSizeEnabled;
        public static ConfigEntry<float>? StackSizeMultiplier;
        public static ConfigEntry<float>? StackSizeAbsoluteMax;
        public static ConfigEntry<string>? StackSizeExcludedPrefabs;

        // Storage - grid growth
        public static ConfigEntry<bool>? GridGrowthEnabled;
        public static ConfigEntry<int>? GridGrowthExtraRows;
        public static ConfigEntry<string>? GridGrowthExcludedPrefabs;

        // World governor - raids
        public static ConfigEntry<bool>? RaidBlockEnabled;
        public static ConfigEntry<string>? RaidBlockedBiomes;
        public static ConfigEntry<string>? RaidBlockedEvents;

        // World governor - night spawns
        public static ConfigEntry<bool>? NightSpawnBlockEnabled;
        public static ConfigEntry<string>? NightSpawnBlockedBiomes;
        public static ConfigEntry<string>? NightSpawnBlockedCreatures;

        // World governor - player cap
        public static ConfigEntry<int>? MaxPlayerCount;

        // World governor - structure upkeep
        public static ConfigEntry<bool>? StructureUpkeepEnabled;
        public static ConfigEntry<float>? StructureUpkeepInterval;
        public static ConfigEntry<int>? StructureUpkeepBatchSize;

        // World governor - first spawn grant
        public static ConfigEntry<bool>? StarterGrantEnabled;
        public static ConfigEntry<string>? StarterKitItems;
        public static ConfigEntry<string>? StarterBoatPrefab;
        public static ConfigEntry<float>? StarterBoatSearchRadius;

        // Vitality
        public static ConfigEntry<bool>? MaxHealthFloorEnabled;
        public static ConfigEntry<float>? MaxHealthFloor;
        public static ConfigEntry<float>? VitalityCheckInterval;

        // Security
        public static ConfigEntry<bool>? VitalsGuardEnabled;
        public static ConfigEntry<float>? MaxHealthCeiling;
        public static ConfigEntry<float>? StaminaPlausibilityCeiling;
        public static ConfigEntry<bool>? PositionWatchEnabled;
        public static ConfigEntry<float>? PositionWatchInterval;
        public static ConfigEntry<float>? SpeedPlausibilityCeiling;
        public static ConfigEntry<float>? FlyDetectionTolerance;
        public static ConfigEntry<bool>? DamagePlausibilityEnabled;
        public static ConfigEntry<float>? DamagePlausibilityCeiling;
        public static ConfigEntry<bool>? ItemIntegritySweepEnabled;
        public static ConfigEntry<float>? ItemIntegritySweepInterval;
        public static ConfigEntry<int>? ItemIntegritySweepBatchSize;
        public static ConfigEntry<bool>? ItemIntegritySweepCorrect;

        public static void Bind(ConfigFile config, ConfigSync configSync)
        {
            ServerConfigLocked = BindSynced(config, configSync, "1 - General", "ServerConfigLocked", true, "If true, only server admins can modify synced configuration.");
            configSync.AddLockingConfigEntry(ServerConfigLocked);
            VerboseLogging = BindLocal(config, "1 - General", "VerboseLogging", false, "Enable verbose diagnostic log messages. Security findings always log regardless of this setting.");

            VacuumEnabled = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumEnabled", true, "Enable containers auto-vacuuming matching ground items nearby. Match-required: only tops up an item type a container already holds.");
            VacuumInterval = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumInterval", 2f, "Seconds between vacuum sweep batches.", 0.5f, 30f);
            VacuumBatchSize = BindSyncedInt(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumBatchSize", 25, "How many container ZDOs to advance the round-robin scanner by each sweep.", 1, 500);
            VacuumRadius = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumRadius", 10f, "Radius around a container to vacuum matching ground items from.", 1f, 50f);
            ContainerLinkRadius = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "ContainerLinkRadius", 10f, "Radius used to find a sibling container for the capacity overflow guard.", 1f, 50f);
            VacuumExcludedContainers = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumExcludedContainers", "", "Comma-separated container prefab names to exclude from vacuuming entirely.");
            VacuumExcludedItems = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumExcludedItems", "", "Comma-separated item prefab names never to vacuum or auto-harvest.");
            AutoHarvestEnabled = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "AutoHarvestEnabled", true, "When a player harvests something, sweep in other ripe pickables of the same type nearby.");
            AutoHarvestRadius = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "AutoHarvestRadius", 8f, "Radius around the triggering pickable (and around each player, for trigger detection) to sweep.", 1f, 30f);

            ProductionSupplyEnabled = BindSynced(config, configSync, "3 - Production Supply", "ProductionSupplyEnabled", true, "Auto-feed fuel and ore/process material to fireplaces and smelters from linked containers, even with nobody online.");
            ProductionSupplyInterval = BindSynced(config, configSync, "3 - Production Supply", "ProductionSupplyInterval", 3f, "Seconds between production supply sweep batches.", 0.5f, 30f);
            ProductionSupplyBatchSize = BindSyncedInt(config, configSync, "3 - Production Supply", "ProductionSupplyBatchSize", 20, "How many fireplace/smelter ZDOs to advance each scanner by per sweep.", 1, 500);
            ProductionSupplyRange = BindSynced(config, configSync, "3 - Production Supply", "ProductionSupplyRange", 15f, "Radius to search for linked source containers.", 1f, 50f);
            ProductionSupplyReserve = BindSyncedInt(config, configSync, "3 - Production Supply", "ProductionSupplyReserve", 1, "Minimum stock of a matching item a source container always keeps - never pulled below this.", 0, 999);

            SortEnabled = BindSynced(config, configSync, "4 - Sort", "SortEnabled", true, "Enable background stack consolidation (merging partial stacks) within containers.");
            SortInterval = BindSynced(config, configSync, "4 - Sort", "SortInterval", 30f, "Seconds between sort sweep batches.", 5f, 300f);
            SortBatchSize = BindSyncedInt(config, configSync, "4 - Sort", "SortBatchSize", 10, "How many container ZDOs to advance the scanner by per sweep.", 1, 500);

            StackSizeEnabled = BindSynced(config, configSync, "5 - Storage Capacity", "StackSizeEnabled", true, "Enable the max stack size multiplier.");
            StackSizeMultiplier = BindSynced(config, configSync, "5 - Storage Capacity", "StackSizeMultiplier", 2f, "Multiplier applied to every stackable item's vanilla max stack size.", 1f, 20f);
            StackSizeAbsoluteMax = BindSynced(config, configSync, "5 - Storage Capacity", "StackSizeAbsoluteMax", 999f, "Hard ceiling on any single item's max stack size regardless of multiplier.", 1f, 9999f);
            StackSizeExcludedPrefabs = BindSynced(config, configSync, "5 - Storage Capacity", "StackSizeExcludedPrefabs", "", "Comma-separated item prefab names to leave at vanilla stack size.");
            GridGrowthEnabled = BindSynced(config, configSync, "5 - Storage Capacity", "GridGrowthEnabled", true, "Enable real grid slot-count growth for containers.");
            GridGrowthExtraRows = BindSyncedInt(config, configSync, "5 - Storage Capacity", "GridGrowthExtraRows", 2, "Extra grid rows added to every container's vanilla height.", 0, 20);
            GridGrowthExcludedPrefabs = BindSynced(config, configSync, "5 - Storage Capacity", "GridGrowthExcludedPrefabs", "", "Comma-separated container prefab names to leave at vanilla grid size.");

            RaidBlockEnabled = BindSynced(config, configSync, "6 - Raids", "RaidBlockEnabled", true, "Block configured raid events from triggering in configured biomes.");
            RaidBlockedBiomes = BindSynced(config, configSync, "6 - Raids", "RaidBlockedBiomes", "Meadows,BlackForest", "Comma-separated Heightmap.Biome names to block high-tier raids in.");
            RaidBlockedEvents = BindSynced(config, configSync, "6 - Raids", "RaidBlockedEvents", "seeker,charred,fulling,gjall", "Comma-separated case-insensitive substrings matched against the raid event name.");

            NightSpawnBlockEnabled = BindSynced(config, configSync, "7 - Night Spawns", "NightSpawnBlockEnabled", true, "Destroy configured hostile creature spawns the instant the server learns about them at night in configured biomes.");
            NightSpawnBlockedBiomes = BindSynced(config, configSync, "7 - Night Spawns", "NightSpawnBlockedBiomes", "Meadows,BlackForest", "Comma-separated Heightmap.Biome names.");
            NightSpawnBlockedCreatures = BindSynced(config, configSync, "7 - Night Spawns", "NightSpawnBlockedCreatures", "Draugr,Draugr_Elite,Wraith,Abomination,Deathsquito,Blob,BlobElite,StoneGolem", "Comma-separated exact creature prefab names to block.");

            MaxPlayerCount = BindSyncedInt(config, configSync, "8 - Player Cap", "MaxPlayerCount", 10, "Maximum concurrent connected players. Vanilla hardcodes 10; this can raise or lower it. On a crossplay (-crossplay) server, PlayFab's own lobby registration is separately hardcoded to 10 and cannot be raised by this or any mod - Steam-direct joins can exceed 10, but PlayFab/Xbox joins past the 10th are still rejected by PlayFab itself. A startup log warning appears if this is set above 10 while crossplay is active.", 1, 256);

            StructureUpkeepEnabled = BindSynced(config, configSync, "9 - Structure Upkeep", "StructureUpkeepEnabled", true, "Periodically reset building piece health back to max, preventing decay.");
            StructureUpkeepInterval = BindSynced(config, configSync, "9 - Structure Upkeep", "StructureUpkeepInterval", 60f, "Seconds between structure upkeep sweep batches.", 5f, 600f);
            StructureUpkeepBatchSize = BindSyncedInt(config, configSync, "9 - Structure Upkeep", "StructureUpkeepBatchSize", 50, "How many WearNTear ZDOs to advance the scanner by per sweep.", 1, 2000);

            StarterGrantEnabled = BindSynced(config, configSync, "10 - Starter Grant", "StarterGrantEnabled", true, "Grant a one-time starter kit and boat on first character creation.");
            StarterKitItems = BindSynced(config, configSync, "10 - Starter Grant", "StarterKitItems", "Wood:20,Stone:10,Flint:5", "Comma-separated PrefabName:Amount pairs spawned as ground items at spawn.");
            StarterBoatPrefab = BindSynced(config, configSync, "10 - Starter Grant", "StarterBoatPrefab", "Karve", "Vanilla hull prefab name granted (e.g. Raft, Karve, VikingShip).");
            StarterBoatSearchRadius = BindSynced(config, configSync, "10 - Starter Grant", "StarterBoatSearchRadius", 60f, "Radius to search for water near spawn to place the boat in.", 10f, 300f);

            MaxHealthFloorEnabled = BindSynced(config, configSync, "11 - Vitality", "MaxHealthFloorEnabled", false, "Enforce a minimum max HP floor for all players. Max stamina/carry weight cannot be changed server-side (no ZDO-backed value exists) - see the plan doc.");
            MaxHealthFloor = BindSynced(config, configSync, "11 - Vitality", "MaxHealthFloor", 0f, "Minimum max HP enforced on every connected player. 0 disables.", 0f, 2000f);
            VitalityCheckInterval = BindSynced(config, configSync, "11 - Vitality", "VitalityCheckInterval", 5f, "Seconds between vitality/vitals-guard correction passes.", 1f, 60f);

            VitalsGuardEnabled = BindSynced(config, configSync, "12 - Security", "VitalsGuardEnabled", true, "Clamp max HP above a configured ceiling; flag implausible current stamina.");
            MaxHealthCeiling = BindSynced(config, configSync, "12 - Security", "MaxHealthCeiling", 0f, "Max HP above this is clamped back down. 0 disables.", 0f, 5000f);
            StaminaPlausibilityCeiling = BindSynced(config, configSync, "12 - Security", "StaminaPlausibilityCeiling", 500f, "Current stamina above this is flagged (detect-only - there is no real max to clamp to). 0 disables.", 0f, 5000f);
            PositionWatchEnabled = BindSynced(config, configSync, "12 - Security", "PositionWatchEnabled", true, "Flag implausible movement speed and likely fly/noclip.");
            PositionWatchInterval = BindSynced(config, configSync, "12 - Security", "PositionWatchInterval", 3f, "Seconds between position checks.", 0.5f, 30f);
            SpeedPlausibilityCeiling = BindSynced(config, configSync, "12 - Security", "SpeedPlausibilityCeiling", 40f, "Movement speed (m/s) above which a player is flagged.", 5f, 200f);
            FlyDetectionTolerance = BindSynced(config, configSync, "12 - Security", "FlyDetectionTolerance", 15f, "Meters a player's Y can exceed expected ground height before being flagged. Deliberately generous - mining/caving produces real negative deltas too.", 1f, 200f);
            DamagePlausibilityEnabled = BindSynced(config, configSync, "12 - Security", "DamagePlausibilityEnabled", false, "Flag single hits exceeding a flat damage ceiling. Least-verified item in this mod - a coarse tripwire, not a tuned model.");
            DamagePlausibilityCeiling = BindSynced(config, configSync, "12 - Security", "DamagePlausibilityCeiling", 300f, "Total damage in one hit above which it is flagged.", 10f, 5000f);
            ItemIntegritySweepEnabled = BindSynced(config, configSync, "12 - Security", "ItemIntegritySweepEnabled", true, "Periodically check every tracked container's and connected player's inventory for fabricated items.");
            ItemIntegritySweepInterval = BindSynced(config, configSync, "12 - Security", "ItemIntegritySweepInterval", 30f, "Seconds between integrity sweep batches.", 5f, 600f);
            ItemIntegritySweepBatchSize = BindSyncedInt(config, configSync, "12 - Security", "ItemIntegritySweepBatchSize", 25, "How many container ZDOs to advance the scanner by per sweep.", 1, 500);
            ItemIntegritySweepCorrect = BindSynced(config, configSync, "12 - Security", "ItemIntegritySweepCorrect", false, "If true, remove implausible items outright instead of only logging them.");

            MigrateLegacyConfig(config);
        }

        // ------------------------------------------------------------------
        // Legacy config migration (Fatty's pattern - see Fatty/Configuration/ConfigManager.cs).
        //
        // The server-only rebuild renamed or dropped every key from the pre-rebuild mod. BepInEx keys a
        // setting by its (section, key) pair, so a rename makes Bind() find nothing: the new entry
        // starts at its default and the admin's tuned value is left behind in the .cfg as a silent
        // orphan. ConfigFile keeps every line it read but never bound to in its OrphanedEntries
        // dictionary (ConfigDefinition -> raw unparsed string) - reached by reflection since its
        // accessibility isn't part of BepInEx's public contract, and a migration that fails has to
        // stay a one-line log rather than take the whole config down.
        // ------------------------------------------------------------------
        private static void MigrateLegacyConfig(ConfigFile config)
        {
            bool removedAny = false;

            // Renamed, same concept, straightforward carry-across.
            removedAny |= TryMigrate(config, "3 - Inventory & Vacuum", "StackMaxMultiplier", StackSizeMultiplier);
            removedAny |= TryMigrate(config, "3 - Inventory & Vacuum", "ContainerVacuumEnabled", VacuumEnabled);
            removedAny |= TryMigrate(config, "3 - Inventory & Vacuum", "ContainerVacuumRadius", VacuumRadius);
            removedAny |= TryMigrate(config, "3 - Inventory & Vacuum", "ContainerVacuumInterval", VacuumInterval);
            removedAny |= TryMigrate(config, "5 - Production & AutoFuel", "AutoFuelLightSources", ProductionSupplyEnabled);
            removedAny |= TryMigrate(config, "5 - Production & AutoFuel", "AutoFuelRadius", ProductionSupplyRange);
            removedAny |= TryMigrate(config, "8 - World & Portals & Raids", "BlockHighTierRaidsInLowBiomes", RaidBlockEnabled);

            // Everything else pre-rebuild (combat/movement tuning, HUD, portals, craft-from-chests,
            // farming) has no destination - those features were cut, not renamed, per the plan doc.
            // Rather than hand-listing every one of those ~25 old keys here (a maintenance burden that
            // drifts the moment either list changes), whatever is left in OrphanedEntries after the
            // real migrations above is reported once as a single summary line, so an admin who goes
            // looking for a setting that vanished gets an answer instead of silence.
            var leftoverKeys = RemainingOrphanKeys(config);
            if (leftoverKeys.Count > 0)
            {
                WonderlandDebug.LogAlways($"[Config] {leftoverKeys.Count} setting(s) from the pre-rebuild mod no longer apply (that feature was removed in the server-only rebuild) and will be dropped from the config file: {string.Join(", ", leftoverKeys)}");
                removedAny = true;
            }

            if (removedAny)
            {
                config.Save();
            }
        }

        private static System.Collections.IDictionary GetOrphans(ConfigFile config)
        {
            try
            {
                var property = typeof(ConfigFile).GetProperty("OrphanedEntries",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                return property?.GetValue(config, null) as System.Collections.IDictionary;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> RemainingOrphanKeys(ConfigFile config)
        {
            var names = new List<string>();
            var orphans = GetOrphans(config);
            if (orphans == null)
            {
                return names;
            }
            var keysToRemove = new List<object>();
            foreach (System.Collections.DictionaryEntry entry in orphans)
            {
                if (entry.Key is ConfigDefinition def)
                {
                    names.Add(def.Key);
                    keysToRemove.Add(entry.Key);
                }
            }
            foreach (object key in keysToRemove)
            {
                orphans.Remove(key);
            }
            return names;
        }

        /// <summary>
        /// Carries a renamed setting's raw value across to its new ConfigEntry, whatever T is - a
        /// value that can't be parsed as T is not worth guessing at, so the new entry just keeps its
        /// default and the admin is told why. Returns true if the orphan was found (and therefore
        /// removed) regardless of whether the value could be parsed, since the dead line needs
        /// dropping from the file either way.
        /// </summary>
        private static bool TryMigrate<T>(ConfigFile config, string oldSection, string oldKey, ConfigEntry<T>? target)
        {
            if (target == null)
            {
                return false;
            }

            var orphans = GetOrphans(config);
            if (orphans == null)
            {
                return false;
            }

            var oldDefinition = new ConfigDefinition(oldSection, oldKey);
            if (!orphans.Contains(oldDefinition))
            {
                return false;
            }

            string raw = orphans[oldDefinition] as string;
            orphans.Remove(oldDefinition);

            try
            {
                T value = (T)System.ComponentModel.TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(raw);
                if (Equals(value, target.Value))
                {
                    return true;
                }
                target.Value = value;
                WonderlandDebug.LogAlways($"[Config] carried your old '{oldKey}' setting ({value}) across to '{target.Definition.Key}' after the server-only rebuild renamed it.");
            }
            catch (System.Exception ex)
            {
                WonderlandDebug.LogWarning($"[Config] could not read the old '{oldKey}' setting (value was '{raw}'): {ex.Message}. '{target.Definition.Key}' keeps its default.");
            }
            return true;
        }

        private static ConfigEntry<float> BindSynced(ConfigFile cfg, ConfigSync sync, string section, string key, float def, string desc, float min = float.MinValue, float max = float.MaxValue)
        {
            ConfigDescription description = (min != float.MinValue && max != float.MaxValue)
                ? new ConfigDescription(desc, new AcceptableValueRange<float>(min, max))
                : new ConfigDescription(desc);

            var entry = cfg.Bind(section, key, def, description);
            sync.AddConfigEntry(entry);
            return entry;
        }

        private static ConfigEntry<int> BindSyncedInt(ConfigFile cfg, ConfigSync sync, string section, string key, int def, string desc, int min = int.MinValue, int max = int.MaxValue)
        {
            ConfigDescription description = (min != int.MinValue && max != int.MaxValue)
                ? new ConfigDescription(desc, new AcceptableValueRange<int>(min, max))
                : new ConfigDescription(desc);

            var entry = cfg.Bind(section, key, def, description);
            sync.AddConfigEntry(entry);
            return entry;
        }

        private static ConfigEntry<bool> BindSynced(ConfigFile cfg, ConfigSync sync, string section, string key, bool def, string desc)
        {
            var entry = cfg.Bind(section, key, def, new ConfigDescription(desc));
            sync.AddConfigEntry(entry);
            return entry;
        }

        private static ConfigEntry<string> BindSynced(ConfigFile cfg, ConfigSync sync, string section, string key, string def, string desc)
        {
            var entry = cfg.Bind(section, key, def, new ConfigDescription(desc));
            sync.AddConfigEntry(entry);
            return entry;
        }

        private static ConfigEntry<T> BindLocal<T>(ConfigFile cfg, string section, string key, T def, string desc)
        {
            return cfg.Bind(section, key, def, new ConfigDescription(desc));
        }
    }
}
