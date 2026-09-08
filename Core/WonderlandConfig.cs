using BepInEx.Configuration;
using ServerSync;
using UnityEngine;

namespace Wonderland.Core
{
    public static class WonderlandConfig
    {
        public static ConfigEntry<bool>? ServerConfigLocked;
        public static ConfigEntry<bool>? VerboseLogging;

        // Building
        public static ConfigEntry<float>? BuildRangeMultiplier;
        public static ConfigEntry<bool>? StaminaFreeBuild;
        public static ConfigEntry<bool>? DisableBuildingDecay;
        public static ConfigEntry<float>? BuildingSupportMultiplier;
        public static ConfigEntry<float>? AutoRepairRadius;

        // Inventory & Vacuum
        public static ConfigEntry<float>? StackMaxMultiplier;
        public static ConfigEntry<float>? CarryWeightBonus;
        public static ConfigEntry<bool>? ContainerVacuumEnabled;
        public static ConfigEntry<float>? ContainerVacuumRadius;
        public static ConfigEntry<float>? ContainerVacuumInterval;

        // Crafting
        public static ConfigEntry<bool>? CraftFromChestsEnabled;
        public static ConfigEntry<float>? CraftFromChestsRadius;
        public static ConfigEntry<bool>? WorkstationNoRoof;
        public static ConfigEntry<float>? WorkstationExtraRange;
        public static ConfigEntry<int>? BatchCraftingMax;

        // Production & Farming & AutoFuel
        public static ConfigEntry<bool>? PlantAnythingEnabled;
        public static ConfigEntry<bool>? CropBiomeRestrictionsBypass;
        public static ConfigEntry<int>? MassPlantingGridSize;
        public static ConfigEntry<float>? CropGrowthSpeedMultiplier;
        public static ConfigEntry<bool>? DisableCropSpaceCheck;
        public static ConfigEntry<bool>? AutoFuelLightSources;
        public static ConfigEntry<float>? AutoFuelRadius;

        // Player & Combat Physics
        public static ConfigEntry<float>? AttackSpeedMultiplier;
        public static ConfigEntry<float>? BowDrawSpeedMultiplier;
        public static ConfigEntry<float>? CrossbowReloadSpeedMultiplier;
        public static ConfigEntry<float>? RunningStaminaMultiplier;
        public static ConfigEntry<float>? SwimmingStaminaMultiplier;
        public static ConfigEntry<float>? MiningStaminaMultiplier;
        public static ConfigEntry<float>? FallDamageMultiplier;
        public static ConfigEntry<bool>? SlopeClimbingLimitBypass;

        // HUD & Telemetry (Client-Local Unsynced)
        public static ConfigEntry<bool>? EnableHUD;
        public static ConfigEntry<KeyCode>? HUDHotkey;
        public static ConfigEntry<float>? HUDPositionX;
        public static ConfigEntry<float>? HUDPositionY;
        public static ConfigEntry<bool>? ShowMovementSpeed;
        public static ConfigEntry<bool>? ShowPureStats;
        public static ConfigEntry<bool>? ShowArmor;
        public static ConfigEntry<bool>? ShowEnvironmentInfo;
        public static ConfigEntry<bool>? ShowWindVector;

        // World, Portals & Raids
        public static ConfigEntry<bool>? SinglePortalDialingEnabled;
        public static ConfigEntry<bool>? AllowMetalsThroughPortals;
        public static ConfigEntry<bool>? PortalLockingEnabled;
        public static ConfigEntry<bool>? BlockHighTierRaidsInLowBiomes;
        public static ConfigEntry<float>? CustomDayLengthMinutes;
        public static ConfigEntry<float>? DayNightRatioPercent;

        public static void Bind(ConfigFile config, ConfigSync configSync)
        {
            // General
            ServerConfigLocked = BindSynced(config, configSync, "1 - General", "ServerConfigLocked", true, "If true, only server admins can modify synced configuration.");
            configSync.AddLockingConfigEntry(ServerConfigLocked);
            VerboseLogging = BindLocal(config, "1 - General", "VerboseLogging", false, "Enable verbose diagnostic log messages.");

            // Building
            BuildRangeMultiplier = BindSynced(config, configSync, "2 - Building", "BuildRangeMultiplier", 2.0f, "Multiplier for player building and repair reach.", 1.0f, 10.0f);
            StaminaFreeBuild = BindSynced(config, configSync, "2 - Building", "StaminaFreeBuild", true, "Disable stamina cost when building or repairing with hammer.");
            DisableBuildingDecay = BindSynced(config, configSync, "2 - Building", "DisableBuildingDecay", true, "Prevent wooden structural pieces from taking water/rain wear decay.");
            BuildingSupportMultiplier = BindSynced(config, configSync, "2 - Building", "BuildingSupportMultiplier", 2.0f, "Multiplier for structural stability support propagation.", 1.0f, 10.0f);
            AutoRepairRadius = BindSynced(config, configSync, "2 - Building", "AutoRepairRadius", 15.0f, "Radius around player to automatically repair damaged building pieces when holding hammer.", 0f, 50f);

            // Inventory & Vacuum
            StackMaxMultiplier = BindSynced(config, configSync, "3 - Inventory & Vacuum", "StackMaxMultiplier", 2.0f, "Multiplier for max stack size of all stackable items (e.g. 10 -> 20).", 1.0f, 20.0f);
            CarryWeightBonus = BindSynced(config, configSync, "3 - Inventory & Vacuum", "CarryWeightBonus", 300.0f, "Additional carry weight capacity added to player base weight limit.", 0f, 2000f);
            ContainerVacuumEnabled = BindSynced(config, configSync, "3 - Inventory & Vacuum", "ContainerVacuumEnabled", true, "Enable containers auto-vacuuming matching items from the ground.");
            ContainerVacuumRadius = BindSynced(config, configSync, "3 - Inventory & Vacuum", "ContainerVacuumRadius", 10.0f, "Radius around containers to vacuum matching ground items.", 2.0f, 30.0f);
            ContainerVacuumInterval = BindSynced(config, configSync, "3 - Inventory & Vacuum", "ContainerVacuumInterval", 2.0f, "Time interval in seconds between container vacuum scans.", 0.5f, 10.0f);

            // Crafting
            CraftFromChestsEnabled = BindSynced(config, configSync, "4 - Crafting", "CraftFromChestsEnabled", true, "Allow crafting stations and player crafting to pull items directly from nearby chests.");
            CraftFromChestsRadius = BindSynced(config, configSync, "4 - Crafting", "CraftFromChestsRadius", 20.0f, "Radius to search for chests when pulling crafting ingredients.", 5.0f, 50.0f);
            WorkstationNoRoof = BindSynced(config, configSync, "4 - Crafting", "WorkstationNoRoof", true, "Remove 'Workstation needs a roof' requirement for crafting and upgrading.");
            WorkstationExtraRange = BindSynced(config, configSync, "4 - Crafting", "WorkstationExtraRange", 40.0f, "Build/crafting range extension for workstations.", 20.0f, 100.0f);
            BatchCraftingMax = BindSynced(config, configSync, "4 - Crafting", "BatchCraftingMax", 10, "Maximum batch size for instant item crafting.", 1, 100);

            // Production & AutoFuel
            PlantAnythingEnabled = BindSynced(config, configSync, "5 - Production & AutoFuel", "PlantAnythingEnabled", true, "Enable planting all wild flora, crops, bushes, and trees.");
            CropBiomeRestrictionsBypass = BindSynced(config, configSync, "5 - Production & AutoFuel", "CropBiomeRestrictionsBypass", true, "Allow crops to grow in any biome (e.g. Barley/Flax in Meadows).");
            MassPlantingGridSize = BindSynced(config, configSync, "5 - Production & AutoFuel", "MassPlantingGridSize", 3, "Grid size (NxN) for mass planting seeds with proper spacing.", 1, 5);
            CropGrowthSpeedMultiplier = BindSynced(config, configSync, "5 - Production & AutoFuel", "CropGrowthSpeedMultiplier", 2.0f, "Growth speed multiplier for crops and trees.", 1.0f, 10.0f);
            DisableCropSpaceCheck = BindSynced(config, configSync, "5 - Production & AutoFuel", "DisableCropSpaceCheck", true, "Disable 'Plant needs more space' death condition.");
            AutoFuelLightSources = BindSynced(config, configSync, "5 - Production & AutoFuel", "AutoFuelLightSources", true, "Automatically refuel torches, sconces, campfires, and hearths from nearby containers.");
            AutoFuelRadius = BindSynced(config, configSync, "5 - Production & AutoFuel", "AutoFuelRadius", 15.0f, "Radius around light/heat sources to scan for fuel containers.", 5.0f, 40.0f);

            // Player & Combat Physics
            AttackSpeedMultiplier = BindSynced(config, configSync, "6 - Player & Combat", "AttackSpeedMultiplier", 1.25f, "Melee and unarmed attack animation speed multiplier.", 1.0f, 3.0f);
            BowDrawSpeedMultiplier = BindSynced(config, configSync, "6 - Player & Combat", "BowDrawSpeedMultiplier", 1.5f, "Bow hold and aim draw speed acceleration multiplier.", 1.0f, 5.0f);
            CrossbowReloadSpeedMultiplier = BindSynced(config, configSync, "6 - Player & Combat", "CrossbowReloadSpeedMultiplier", 1.5f, "Crossbow reload animation acceleration multiplier.", 1.0f, 5.0f);
            RunningStaminaMultiplier = BindSynced(config, configSync, "6 - Player & Combat", "RunningStaminaMultiplier", 0.5f, "Stamina drain multiplier while running.", 0.0f, 1.0f);
            SwimmingStaminaMultiplier = BindSynced(config, configSync, "6 - Player & Combat", "SwimmingStaminaMultiplier", 0.5f, "Stamina drain multiplier while swimming.", 0.0f, 1.0f);
            MiningStaminaMultiplier = BindSynced(config, configSync, "6 - Player & Combat", "MiningStaminaMultiplier", 0.5f, "Stamina drain multiplier while pickaxing or woodcutting.", 0.0f, 1.0f);
            FallDamageMultiplier = BindSynced(config, configSync, "6 - Player & Combat", "FallDamageMultiplier", 0.5f, "Fall damage multiplier.", 0.0f, 1.0f);
            SlopeClimbingLimitBypass = BindSynced(config, configSync, "6 - Player & Combat", "SlopeClimbingLimitBypass", true, "Allow running up steep mountain slopes without slipping.");

            // HUD & Telemetry (Client-Local Unsynced)
            EnableHUD = BindLocal(config, "7 - HUD & Telemetry", "EnableHUD", true, "Enable dynamic IMGUI player stats overlay.");
            HUDHotkey = BindLocal(config, "7 - HUD & Telemetry", "HUDHotkey", KeyCode.F7, "Hotkey toggle for HUD overlay.");
            HUDPositionX = BindLocal(config, "7 - HUD & Telemetry", "HUDPositionX", 20.0f, "HUD overlay X screen position.");
            HUDPositionY = BindLocal(config, "7 - HUD & Telemetry", "HUDPositionY", 180.0f, "HUD overlay Y screen position.");
            ShowMovementSpeed = BindLocal(config, "7 - HUD & Telemetry", "ShowMovementSpeed", true, "Show live player movement speed (m/s).");
            ShowPureStats = BindLocal(config, "7 - HUD & Telemetry", "ShowPureStats", true, "Show detailed HP, Stamina, and Eitr max/current/regen metrics.");
            ShowArmor = BindLocal(config, "7 - HUD & Telemetry", "ShowArmor", true, "Show total calculated armor value.");
            ShowEnvironmentInfo = BindLocal(config, "7 - HUD & Telemetry", "ShowEnvironmentInfo", true, "Show biome, coordinates, time of day, and weather status.");
            ShowWindVector = BindLocal(config, "7 - HUD & Telemetry", "ShowWindVector", true, "Show live wind direction and speed indicator.");

            // World, Portals & Raids
            SinglePortalDialingEnabled = BindSynced(config, configSync, "8 - World & Portals & Raids", "SinglePortalDialingEnabled", true, "Enable single-portal destination selection menu.");
            AllowMetalsThroughPortals = BindSynced(config, configSync, "8 - World & Portals & Raids", "AllowMetalsThroughPortals", true, "Allow teleportation with metals, ores, and dragon eggs.");
            PortalLockingEnabled = BindSynced(config, configSync, "8 - World & Portals & Raids", "PortalLockingEnabled", true, "Enable PIN code/password locking on portals.");
            BlockHighTierRaidsInLowBiomes = BindSynced(config, configSync, "8 - World & Portals & Raids", "BlockHighTierRaidsInLowBiomes", true, "Prevent high-tier raids (Seekers, Charred, Furlings) from spawning in Meadows or Black Forest.");
            CustomDayLengthMinutes = BindSynced(config, configSync, "8 - World & Portals & Raids", "CustomDayLengthMinutes", 30.0f, "Total length of a full day/night cycle in minutes (vanilla is 30).", 10.0f, 240.0f);
            DayNightRatioPercent = BindSynced(config, configSync, "8 - World & Portals & Raids", "DayNightRatioPercent", 75.0f, "Percentage of the full cycle allocated to daylight (vanilla is ~70%).", 20.0f, 90.0f);
        }

        private static ConfigEntry<T> BindSynced<T>(ConfigFile cfg, ConfigSync sync, string section, string key, T def, string desc, float min = float.MinValue, float max = float.MaxValue)
        {
            ConfigDescription description = (min != float.MinValue && max != float.MaxValue) 
                ? new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)) 
                : new ConfigDescription(desc);

            var entry = cfg.Bind(section, key, def, description);
            sync.AddConfigEntry(entry);
            return entry;
        }

        private static ConfigEntry<T> BindLocal<T>(ConfigFile cfg, string section, string key, T def, string desc)
        {
            return cfg.Bind(section, key, def, new ConfigDescription(desc));
        }
    }
}
