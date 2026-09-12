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
        public static ConfigEntry<bool>? VacuumEffectEnabled;
        public static ConfigEntry<string>? VacuumEffectPrefab;
        public static ConfigEntry<string>? VacuumSoundPrefab;
        public static ConfigEntry<bool>? AutoHarvestEnabled;
        public static ConfigEntry<float>? AutoHarvestRadius;

        // Item flow - water buoyancy
        public static ConfigEntry<bool>? AllItemsFloatEnabled;
        public static ConfigEntry<float>? FloatSurfaceOffset;
        public static ConfigEntry<float>? FloatSweepInterval;

        // World governor - world rates & carry capacity
        public static ConfigEntry<float>? CarryWeightMultiplier;
        public static ConfigEntry<float>? StaminaRegenRateMultiplier;

        // Status Effect Roster
        public static ConfigEntry<bool>? BuffRosterEnabled;
        public static ConfigEntry<bool>? BuffRoster_GP_Moder_Enabled;
        public static ConfigEntry<bool>? BuffRoster_GP_Moder_RequireBoat;
        public static ConfigEntry<bool>? BuffRoster_Potion_hasty_Enabled;
        public static ConfigEntry<bool>? BuffRoster_TrinketIronStamina_Enabled;
        public static ConfigEntry<bool>? BuffRoster_Potion_swimmer_Enabled;
        public static ConfigEntry<bool>? BuffRoster_TrinketChitinSwim_Enabled;
        public static ConfigEntry<bool>? BuffRoster_Warm_Enabled;
        public static ConfigEntry<bool>? BuffRoster_Potion_stamina_lingering_Enabled;
        public static ConfigEntry<bool>? BuffRoster_Potion_tasty_Enabled;
        public static ConfigEntry<bool>? BuffRoster_Rested_Enabled;
        public static ConfigEntry<bool>? BuffRoster_GP_Eikthyr_Enabled;
        public static ConfigEntry<bool>? BuffRoster_GP_Bonemass_Enabled;
        public static ConfigEntry<bool>? BuffRoster_GP_TheElder_Enabled;
        public static ConfigEntry<bool>? BuffRoster_GP_Yagluth_Enabled;
        public static ConfigEntry<bool>? BuffRoster_GP_Queen_Enabled;

        // Heartbeat
        public static ConfigEntry<bool>? HeartbeatEnabled;
        public static ConfigEntry<float>? HeartbeatIntervalMinutes;

        // Item flow - production supply
        public static ConfigEntry<bool>? ProductionSupplyEnabled;
        public static ConfigEntry<float>? ProductionSupplyInterval;
        public static ConfigEntry<int>? ProductionSupplyBatchSize;
        public static ConfigEntry<float>? ProductionSupplyRange;
        public static ConfigEntry<int>? ProductionSupplyReserve;
        public static ConfigEntry<string>? KilnWoodTypes;

        // 13 - Player Controls (vanilla emotes as the player-to-server signal)
        public static ConfigEntry<bool>? PlayerControlsEnabled;
        public static ConfigEntry<float>? ControlRange;
        public static ConfigEntry<string>? SupplyOffEmote;
        public static ConfigEntry<string>? SupplyOnEmote;
        public static ConfigEntry<string>? CacheClaimEmote;

        // Item flow - sort
        public static ConfigEntry<bool>? SortEnabled;
        public static ConfigEntry<float>? SortInterval;
        public static ConfigEntry<int>? SortBatchSize;

        // 5 - Container Rows
        public static ConfigEntry<bool>? ContainerRowsEnabled;
        public static ConfigEntry<float>? ContainerRowMultiplier;
        public static ConfigEntry<float>? ContainerRowsInterval;
        public static ConfigEntry<int>? ContainerRowsBatchSize;
        public static ConfigEntry<string>? ContainerRowsExcludedContainers;

        // Storage - stack size

        // Storage - grid growth

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
        public static ConfigEntry<float>? StructureUpkeepPlayerRadius;
        public static ConfigEntry<int>? StructureUpkeepSectorsPerSweep;
        public static ConfigEntry<bool>? StructureUpkeepPlayerBuiltOnly;

        // World governor - first spawn grant
        public static ConfigEntry<bool>? StarterGrantEnabled;
        public static ConfigEntry<string>? StarterKitItems;
        public static ConfigEntry<string>? StarterBoatPrefab;
        public static ConfigEntry<float>? StarterBoatSearchRadius;
        public static ConfigEntry<bool>? StarterBoatMapPin;

        // Discord Notify
        public static ConfigEntry<bool>? DiscordNotifyEnabled;
        public static ConfigEntry<string>? DiscordWebhookUrl;
        public static ConfigEntry<bool>? DiscordNotifyServerStatus;
        public static ConfigEntry<bool>? DiscordNotifyLogins;
        public static ConfigEntry<string>? DiscordUsername;
        public static ConfigEntry<string>? DiscordJoinMessage;
        public static ConfigEntry<string>? DiscordLeaveMessage;
        public static ConfigEntry<string>? DiscordServerOnlineMessage;
        public static ConfigEntry<string>? DiscordServerOfflineMessage;

        // Security
        public static ConfigEntry<bool>? VitalsGuardEnabled;
        public static ConfigEntry<float>? VitalsGuardInterval;
        public static ConfigEntry<float>? MaxHealthCeiling;
        public static ConfigEntry<float>? StaminaPlausibilityCeiling;
        public static ConfigEntry<bool>? PositionWatchEnabled;
        public static ConfigEntry<float>? PositionWatchInterval;
        public static ConfigEntry<float>? SpeedPlausibilityCeiling;
        public static ConfigEntry<float>? FlyDetectionTolerance;
        public static ConfigEntry<bool>? ItemIntegritySweepEnabled;
        public static ConfigEntry<float>? ItemIntegritySweepInterval;
        public static ConfigEntry<int>? ItemIntegritySweepBatchSize;
        public static ConfigEntry<bool>? ItemIntegritySweepCorrect;

        // Discord Notify - lifecycle & world events
        public static ConfigEntry<float>? DiscordLifecycleInterval;
        public static ConfigEntry<bool>? DiscordNotifyDeaths;
        public static ConfigEntry<string>? DiscordDeathMessage;
        public static ConfigEntry<bool>? DiscordNotifyFirstJoin;
        public static ConfigEntry<string>? DiscordFirstJoinMessage;
        public static ConfigEntry<bool>? DiscordNotifyBossDefeats;
        public static ConfigEntry<string>? DiscordBossDefeatMessage;
        public static ConfigEntry<bool>? DiscordNotifyHeartbeat;
        public static ConfigEntry<string>? DiscordHeartbeatMessage;

        public static void Bind(ConfigFile config, ConfigSync configSync)
        {
            ServerConfigLocked = BindSynced(config, configSync, "1 - General", "ServerConfigLocked", true, "If true, only server admins can modify synced configuration.");
            configSync.AddLockingConfigEntry(ServerConfigLocked);
            VerboseLogging = BindLocal(config, "1 - General", "VerboseLogging", true, "Enable verbose diagnostic log messages, including every item transfer the mod makes. On by default: a suspected duplication is only diagnosable if the transfer that caused it was already being logged when it happened. Security findings and ledger rejections always log regardless of this setting.");
            HeartbeatEnabled = BindLocal(config, "1 - General", "HeartbeatEnabled", true, "Log one summary line periodically (see HeartbeatIntervalMinutes below) showing uptime and who's currently online, so an admin tailing the server log can confirm the mod is alive at a glance without needing VerboseLogging's full per-event detail. There's also a Discord version of this in section 14 (DiscordNotifyHeartbeat) - both share this same interval.");
            HeartbeatIntervalMinutes = BindLocal(config, "1 - General", "HeartbeatIntervalMinutes", 15f, "Minutes between heartbeat summary lines (both the log one above and the optional Discord one in section 14).");

            VacuumEnabled = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumEnabled", true, "Enable containers auto-vacuuming matching ground items nearby. Match-required: only tops up an item type a container already holds.");
            VacuumInterval = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumInterval", 2f, "Seconds between vacuum sweep batches.", 0.5f, 30f);
            VacuumBatchSize = BindSyncedInt(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumBatchSize", 25, "How many container ZDOs to advance the round-robin scanner by each sweep.", 1, 500);
            VacuumRadius = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumRadius", 10f, "Radius around a container to vacuum matching ground items from.", 1f, 50f);
            ContainerLinkRadius = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "ContainerLinkRadius", 10f, "Radius used to find a sibling container for the capacity overflow guard.", 1f, 50f);
            VacuumExcludedContainers = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumExcludedContainers", "", "Comma-separated container prefab names to exclude from vacuuming entirely.");
            VacuumExcludedItems = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumExcludedItems", "", "Comma-separated item prefab names never to vacuum or auto-harvest.");
            VacuumEffectEnabled = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumEffectEnabled", true, "Play a visual splash at a container when it vacuums ground items into it, so the pull is visible on a completely vanilla client.");
            VacuumEffectPrefab = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumEffectPrefab", "vfx_fermenter_add", "Vanilla effect prefab to spawn at a container when it vacuums ground items (default: 'vfx_fermenter_add' for the fermenter liquid splash).");
            VacuumSoundPrefab = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "VacuumSoundPrefab", "sfx_fermenter_add", "Vanilla sound effect prefab to spawn at a container when it vacuums ground items (default: 'sfx_fermenter_add' for the fermenter splash sound; leave empty to disable sound).");
            AutoHarvestEnabled = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "AutoHarvestEnabled", true, "When a player harvests something, sweep in other ripe pickables of the same type nearby.");
            AutoHarvestRadius = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "AutoHarvestRadius", 8f, "Radius around the triggering pickable (and around each player, for trigger detection) to sweep.", 1f, 30f);
            AllItemsFloatEnabled = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "AllItemsFloatEnabled", true, "Force all dropped items (metals, ores, armor, weapons, serpent scales) to float on water, completely server-side. Vanilla clients see them bobbing on the water surface and can collect them from boats or while swimming.");
            FloatSurfaceOffset = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "FloatSurfaceOffset", -0.25f, "Elevation offset relative to water level where floating items rest (-0.25 places items naturally half-submerged in the waterline).", -2f, 2f);
            FloatSweepInterval = BindSynced(config, configSync, "2 - Vacuum & Auto-Harvest", "FloatSweepInterval", 0.3f, "Seconds between water buoyancy scans around connected players.", 0.1f, 10f);

            CarryWeightMultiplier = BindSynced(config, configSync, "15 - World Modifiers & Capacity", "CarryWeightMultiplier", 2.0f, "Adjusts player max carry weight via Valheim's native world modifier rate. Completely server-side - vanilla clients display the new limit in their UI and auto-pickup up to this capacity with no client mods. 1.0 = vanilla default (300 base, 450 with Megingjord); 1.5 = 450 base, 675 with belt; 2.0 = 600 base, 900 with belt; 3.0 = 900 base, 1350 with belt.", 0.5f, 10f);
            StaminaRegenRateMultiplier = BindSynced(config, configSync, "15 - World Modifiers & Capacity", "StaminaRegenRateMultiplier", 3.0f, "How fast every player's stamina regenerates, server-wide. 1.0 = vanilla default; 3.0 (default here) = three times as fast. Uses Valheim's own native world-rate system (same mechanism as CarryWeightMultiplier above) - vanilla clients apply it automatically, no client mod needed. NOTE: if you also turn on a Status Effect Roster entry that boosts stamina regen (section 16), the two MULTIPLY together rather than add - e.g. this at 3.0x plus a roster effect worth +100% regen yields 6x total, not 4x.", 0.1f, 10f);

            BuffRosterEnabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRosterEnabled", true, "Turns the whole Status Effect Roster on or off. When on, Wonderland automatically gives every connected player a chosen set of real potion/buff effects from the game (pick which ones below) and keeps them active permanently - like drinking a potion that never runs out, with nothing to install on the player's end. Turn individual effects on or off with the settings below this one; this is just the master switch for all of them. One limitation worth knowing: the game gives this mod no way to cancel an effect early once it's granted, so turning an effect off here means it stops being renewed and fades out naturally over its own duration, rather than disappearing instantly.");
            BuffRoster_GP_Moder_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_GP_Moder_Enabled", true, "Gives players the Moder boss-power buff: +10% run speed, +300 extra carry weight, and resistance to frost damage. This is a real effect that already exists in the game (normally earned by beating the Moder boss) - this setting just gives it to everyone for free, automatically. Live-confirmed working with RequireBoat below (2026-09-11): grants the instant you take a ship's wheel, including after getting off and back on the same or a different ship.");
            BuffRoster_GP_Moder_RequireBoat = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_GP_Moder_RequireBoat", true, "If on (the default), the Moder buff above only applies while a player is ACTIVELY STEERING a ship - hands on the wheel, not just standing on deck - matching what that power is really for in vanilla (sailing against the wind). Read directly off the ship's own steering-user field, the same signal the game itself uses to know who's driving. Live-confirmed 2026-09-11: grants immediately on taking the wheel, and grants again immediately on re-taking it (same ship or a different one) after letting go, even if the previous grant had barely started its own re-up cycle. If off, everyone gets the buff constantly, on land or at sea. 'Gone' after letting go still means fading over the effect's own duration, not an instant cutoff - see the note on BuffRosterEnabled above about why.");
            BuffRoster_Potion_hasty_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_Potion_hasty_Enabled", true, "Gives every player the Tonic of Ratatosk potion effect: +15% run speed plus a temporary skill boost. Real vanilla potion, just never wearing off.");
            BuffRoster_TrinketIronStamina_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_TrinketIronStamina_Enabled", true, "Gives players another +15% run speed, from a different vanilla trinket effect. With all three run-speed buffs on by default (this one plus Moder and Tonic of Ratatosk above), players get +40% run speed total - that's the most this mod can give, since there's no bigger speed effect anywhere in the game to grant.");
            BuffRoster_Potion_swimmer_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_Potion_swimmer_Enabled", true, "Cuts stamina cost while swimming in half - and ONLY while swimming, running and jumping cost the same as normal.");
            BuffRoster_TrinketChitinSwim_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_TrinketChitinSwim_Enabled", true, "Cuts stamina cost while swimming by 80% (swimming only, same as the setting above) and makes players swim 50% faster. The strongest swim buff this mod can grant. Stacks with the setting above for even cheaper swimming.");
            BuffRoster_Warm_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_Warm_Enabled", false, "Doubles stamina and eitr regeneration for every player, permanently. Off by default - if you also turn on StaminaRegenRateMultiplier above, the two multiply together rather than add, so combining them gives a bigger boost than you might expect (see that setting's description for the exact math).");
            BuffRoster_Potion_stamina_lingering_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_Potion_stamina_lingering_Enabled", false, "Boosts stamina regen by 25% for every player. Off by default - same stacking caution as Warm above if you also use StaminaRegenRateMultiplier.");
            BuffRoster_Potion_tasty_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_Potion_tasty_Enabled", false, "Doubles stamina regen for every player. Off by default - same stacking caution as Warm above. Also the chattiest effect in this list behind the scenes: this particular vanilla effect naturally wears off in just 10 seconds, so keeping it active means re-applying it roughly every 4 seconds per player - harmless, just worth knowing if you're watching server logs closely.");
            BuffRoster_Rested_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_Rested_Enabled", false, "Gives every player the same buff you get from resting at a campfire: better health/stamina/eitr regen and faster skill gain, permanently. Off by default - same stacking caution as Warm above with StaminaRegenRateMultiplier, and this is the single chattiest effect in the roster behind the scenes (it naturally wears off after 1 second in vanilla, so it needs constant re-applying to stay active) - harmless, just the busiest one.");
            BuffRoster_GP_Eikthyr_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_GP_Eikthyr_Enabled", false, "Gives every player Eikthyr's boss power: stamina costs for running, jumping, AND swimming all cut by 60% at once (compare to the Swimmer effects above, which only touch swimming). Off by default.");
            BuffRoster_GP_Bonemass_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_GP_Bonemass_Enabled", false, "Gives every player Bonemass's boss power: free blocking (no stamina cost) plus some real resistance to physical damage (blunt/slash/pierce). Off by default.");
            BuffRoster_GP_TheElder_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_GP_TheElder_Enabled", false, "Gives every player The Elder's boss power: 30% faster health regen, plus extra damage dealt when chopping wood or mining with a pickaxe. Off by default.");
            BuffRoster_GP_Yagluth_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_GP_Yagluth_Enabled", false, "Gives every player Yagluth's boss power: a farming skill boost, 10% extra damage with every weapon type, and lightning resistance. Off by default.");
            BuffRoster_GP_Queen_Enabled = BindSynced(config, configSync, "16 - Status Effect Roster", "BuffRoster_GP_Queen_Enabled", false, "Gives every player The Queen's boss power: free sneaking (no stamina cost), doubled eitr regen, and poison resistance. Off by default.");

            ProductionSupplyEnabled = BindSynced(config, configSync, "3 - Production Supply", "ProductionSupplyEnabled", true, "Auto-feed fuel and ore/process material to fireplaces and smelters from linked containers, even with nobody online.");
            ProductionSupplyInterval = BindSynced(config, configSync, "3 - Production Supply", "ProductionSupplyInterval", 3f, "Seconds between production supply sweep batches.", 0.5f, 30f);
            ProductionSupplyBatchSize = BindSyncedInt(config, configSync, "3 - Production Supply", "ProductionSupplyBatchSize", 20, "How many fireplace/smelter ZDOs to advance each scanner by per sweep.", 1, 500);
            ProductionSupplyRange = BindSynced(config, configSync, "3 - Production Supply", "ProductionSupplyRange", 15f, "Radius to search for linked source containers.", 1f, 50f);
            ProductionSupplyReserve = BindSyncedInt(config, configSync, "3 - Production Supply", "ProductionSupplyReserve", 1, "Minimum stock of a matching item a source container always keeps - never pulled below this.", 0, 999);
            KilnWoodTypes = BindSynced(config, configSync, "3 - Production Supply", "KilnWoodTypes", "Wood", "Comma-separated item prefab names the auto-supply may load into charcoal kilns (any smelter-family station whose only product is Coal). Default is plain Wood only, so fine wood, core wood and blackwood sitting in a linked chest are never turned into coal behind your back. Empty allows every wood the kiln accepts. Players feeding a kiln by hand are vanilla and unaffected.");

            SortEnabled = BindSynced(config, configSync, "4 - Sort", "SortEnabled", true, "Enable background stack consolidation (merging partial stacks) within containers.");
            SortInterval = BindSynced(config, configSync, "4 - Sort", "SortInterval", 30f, "Seconds between sort sweep batches.", 5f, 300f);
            SortBatchSize = BindSyncedInt(config, configSync, "4 - Sort", "SortBatchSize", 10, "How many container ZDOs to advance the scanner by per sweep.", 1, 500);

            ContainerRowsEnabled = BindSynced(config, configSync, "5 - Container Rows", "ContainerRowsEnabled", true, "Grow every player-built container to ContainerRowMultiplier times its vanilla rows, rendered by completely vanilla clients. Vanilla's own load path accepts item rows beyond the prefab grid and resizes the chest to fit them (client Container.UpdateRows), so the server only has to keep one stack parked in the last row. Width and stack sizes cannot be changed this way: a vanilla client refuses extra columns and clamps stacks on load.");
            ContainerRowMultiplier = BindSynced(config, configSync, "5 - Container Rows", "ContainerRowMultiplier", 2f, "Multiplier on each player-built container's vanilla row count. 2 doubles it: a 5x2 wood chest becomes 5x4, an 8x4 reinforced chest 8x8. 1 leaves containers at vanilla size. Total rows are capped at 32.", 1f, 4f);
            ContainerRowsInterval = BindSynced(config, configSync, "5 - Container Rows", "ContainerRowsInterval", 5f, "Seconds between anchor sweep batches.", 0.5f, 60f);
            ContainerRowsBatchSize = BindSyncedInt(config, configSync, "5 - Container Rows", "ContainerRowsBatchSize", 25, "How many container ZDOs to advance the scanner by per sweep.", 1, 500);
            ContainerRowsExcludedContainers = BindSynced(config, configSync, "5 - Container Rows", "ContainerRowsExcludedContainers", "", "Comma-separated container prefab names to keep at vanilla size. World-spawned containers (tombstones, treasure and dungeon chests, cargo crates) are never grown regardless.");


            PlayerControlsEnabled = BindSynced(config, configSync, "13 - Player Controls", "PlayerControlsEnabled", true, "Let players operate Wonderland from a completely vanilla client with emotes. A stock client never sends a custom slash command to the server (it runs locally and prints 'not a recognized command'), and plain chat is only delivered to other players, so a player alone on the server produces no chat traffic at all. Emotes are written into the character ZDO and always reach the server, from the emote wheel on any platform or typed in chat (/nonono, /thumbsup, /comehere). The server answers with an on-screen message.");
            ControlRange = BindSynced(config, configSync, "13 - Player Controls", "ControlRange", 5f, "How close, in metres, a player must stand to the kiln, smelter or fire an emote is aimed at. The nearest one within this range is the target.", 1f, 20f);
            SupplyOffEmote = BindSynced(config, configSync, "13 - Player Controls", "SupplyOffEmote", "nonono", "Emote that switches the auto-supply OFF for the nearest station: Wonderland stops loading it, whatever is inside burns out, hand-feeding still works. Vanilla emote names: wave, sit, challenge, cheer, nonono, thumbsup, point, blowkiss, bow, cower, cry, despair, flex, comehere, headbang, kneel, laugh, roar, shrug, dance, relax, toast, rest, vibe, loveyou.");
            SupplyOnEmote = BindSynced(config, configSync, "13 - Player Controls", "SupplyOnEmote", "thumbsup", "Emote that switches the auto-supply back ON for the nearest station.");
            CacheClaimEmote = BindSynced(config, configSync, "13 - Player Controls", "CacheClaimEmote", "comehere", "Emote that drops cached items (the overflow guard's item cache) at the player's feet - cached stacks within 30m first, or every cached stack in the world if none are nearby. Nothing happens when the cache is empty.");

            RaidBlockEnabled = BindSynced(config, configSync, "6 - Raids", "RaidBlockEnabled", true, "Block configured raid events from triggering in configured biomes.");
            RaidBlockedBiomes = BindSynced(config, configSync, "6 - Raids", "RaidBlockedBiomes", "Meadows,BlackForest", "Comma-separated Heightmap.Biome names to block high-tier raids in.");
            RaidBlockedEvents = BindSynced(config, configSync, "6 - Raids", "RaidBlockedEvents", "seeker,charred,fulling,gjall", "Comma-separated case-insensitive substrings matched against the raid event name.");

            NightSpawnBlockEnabled = BindSynced(config, configSync, "7 - Night Spawns", "NightSpawnBlockEnabled", true, "Destroy configured hostile creature spawns the instant the server learns about them at night in configured biomes.");
            NightSpawnBlockedBiomes = BindSynced(config, configSync, "7 - Night Spawns", "NightSpawnBlockedBiomes", "Meadows,BlackForest,Mistlands,AshLands,Plains", "Which biomes night-spawn blocking applies in (comma-separated: Meadows, BlackForest, Swamp, Mountain, Plains, Mistlands, AshLands, DeepNorth, Ocean). A creature only gets blocked if it's BOTH on the creature list below AND spawns in one of these biomes - so if you add a new creature below and it still isn't being blocked, check that its biome is listed here too. Mistlands/AshLands/Plains are already included by default to cover the Seeker, Charred, and Fenring creatures below.");
            NightSpawnBlockedCreatures = BindSynced(config, configSync, "7 - Night Spawns", "NightSpawnBlockedCreatures", "Draugr,Draugr_Elite,Wraith,Abomination,Deathsquito,Blob,BlobElite,StoneGolem,Seeker,SeekerBrood,SeekerBrute,SeekerQueen,Charred_Archer,Charred_Archer_Fader,Charred_Mage,Charred_Melee,Charred_Melee_Dyrnwyn,Charred_Melee_Fader,Charred_Twitcher,Charred_Twitcher_Summoned,Fenring,Fenring_Cultist,Fenring_Cultist_Hildir,Fenring_Cultist_Hildir_nochest", "Which creatures get destroyed if they spawn at night (comma-separated, exact in-game names - typos just mean that entry silently does nothing). Covers the Draugr/Wraith/Abomination/Blob/Stone Golem family by default, plus the full Seeker family (Mistlands), the full Charred family (AshLands), and the full Fenring family (Hildir's camps) - add or remove names freely. Remember: the biome list above also has to include wherever a creature actually spawns, or blocking it here won't do anything.");

            MaxPlayerCount = BindSyncedInt(config, configSync, "8 - Player Cap", "MaxPlayerCount", 10, "Maximum concurrent connected players. Vanilla hardcodes 10; this can raise or lower it. On a crossplay (-crossplay) server, PlayFab's own lobby registration is separately hardcoded to 10 and cannot be raised by this or any mod - Steam-direct joins can exceed 10, but PlayFab/Xbox joins past the 10th are still rejected by PlayFab itself. A startup log warning appears if this is set above 10 while crossplay is active.", 1, 256);

            StructureUpkeepEnabled = BindSynced(config, configSync, "9 - Structure Upkeep", "StructureUpkeepEnabled", true, "Periodically reset building piece health back to max, preventing decay.");
            StructureUpkeepInterval = BindSynced(config, configSync, "9 - Structure Upkeep", "StructureUpkeepInterval", 60f, "Seconds between structure upkeep sweep batches.", 5f, 600f);
            StructureUpkeepPlayerRadius = BindSynced(config, configSync, "9 - Structure Upkeep", "StructureUpkeepPlayerRadius", 128f, "Metres around each connected player checked for damaged pieces every sweep. Decay only ever happens inside a client's active area (a 1.5-zone box, up to ~128m from the player), so this pass is what actually keeps up with wear; the default covers that whole envelope.", 16f, 512f);
            StructureUpkeepSectorsPerSweep = BindSyncedInt(config, configSync, "9 - Structure Upkeep", "StructureUpkeepSectorsPerSweep", 512, "How many populated 64m sectors the background full-map sweep walks per sweep interval. This is the slow backstop for damage that predates the feature or happened in an unvisited zone; the per-player pass above does the real-time work. (Replaces StructureUpkeepBatchSize, whose unit was different.)", 1, 20000);
            StructureUpkeepPlayerBuiltOnly = BindSynced(config, configSync, "9 - Structure Upkeep", "StructureUpkeepPlayerBuiltOnly", true, "Only repair pieces a player placed (the piece has a creator). World-generated ruins are spawned deliberately pre-damaged; turning this off will gradually restore every abandoned village and dungeon on the map to pristine.");

            StarterGrantEnabled = BindSynced(config, configSync, "10 - Starter Grant", "StarterGrantEnabled", true, "Grant a one-time starter kit and boat the first time a character is seen in this world. Recorded as a world global key per character (wonderland_starter_<playerID>), saved with the world.");
            StarterKitItems = BindSynced(config, configSync, "10 - Starter Grant", "StarterKitItems", "Wood:50,Stone:10,Flint:5,AxeFlint:1,KnifeFlint:1,SpearFlint:1,PickaxeAntler:1", "Comma-separated PrefabName:Amount pairs spawned as ground items at spawn.");
            StarterBoatPrefab = BindSynced(config, configSync, "10 - Starter Grant", "StarterBoatPrefab", "Karve", "Vanilla hull prefab name granted (e.g. Raft, Karve, VikingShip).");
            StarterBoatSearchRadius = BindSynced(config, configSync, "10 - Starter Grant", "StarterBoatSearchRadius", 200f, "Radius to search for water near spawn to place the boat in. The search always starts at the shoreline nearest the player and works outward, so this is a ceiling, not a target.", 20f, 1500f);
            StarterBoatMapPin = BindSynced(config, configSync, "10 - Starter Grant", "StarterBoatMapPin", true, "Send a vanilla map pin discovery to the player's map marking the starter boat.");

            DiscordNotifyEnabled = BindLocal(config, "14 - Discord Notify", "DiscordNotifyEnabled", true, "Master switch for Discord webhook announcements. Has no effect until DiscordWebhookUrl is set.");
            DiscordWebhookUrl = BindLocal(config, "14 - Discord Notify", "DiscordWebhookUrl", "", "Discord webhook URL to post announcements to (Server Settings -> Integrations -> Webhooks in Discord). Local to this server only - never synced to clients, unlike most settings above.");
            DiscordNotifyServerStatus = BindLocal(config, "14 - Discord Notify", "DiscordNotifyServerStatus", true, "Announce when the server comes online (world finished loading) and when it shuts down.");
            DiscordNotifyLogins = BindLocal(config, "14 - Discord Notify", "DiscordNotifyLogins", true, "Announce when a player connects or disconnects.");
            DiscordUsername = BindLocal(config, "14 - Discord Notify", "DiscordUsername", "Wonderland", "Display name the webhook posts under in Discord. Empty uses the webhook's own configured name.");
            DiscordJoinMessage = BindLocal(config, "14 - Discord Notify", "DiscordJoinMessage", "🟢 **{player}** joined the server", "Message posted when a player connects. {player} is replaced with their name.");
            DiscordLeaveMessage = BindLocal(config, "14 - Discord Notify", "DiscordLeaveMessage", "🔴 **{player}** left the server", "Message posted when a player disconnects. {player} is replaced with their name.");
            DiscordServerOnlineMessage = BindLocal(config, "14 - Discord Notify", "DiscordServerOnlineMessage", "🟢 **{world}** server is online", "Message posted once the world has finished loading. {world} is replaced with the world name.");
            DiscordServerOfflineMessage = BindLocal(config, "14 - Discord Notify", "DiscordServerOfflineMessage", "🔴 **{world}** server is offline", "Message posted on shutdown. {world} is replaced with the world name.");

            VitalsGuardEnabled = BindSynced(config, configSync, "12 - Security", "VitalsGuardEnabled", true, "Flag max HP above a configured ceiling and implausible current stamina. Detect-only: neither can be corrected from the server - the owning client rewrites both every second and discards stale server writes while moving.");
            VitalsGuardInterval = BindSynced(config, configSync, "12 - Security", "VitalsGuardInterval", 5f, "Seconds between vitals checks.", 1f, 60f);
            MaxHealthCeiling = BindSynced(config, configSync, "12 - Security", "MaxHealthCeiling", 0f, "Max HP above this is flagged in the security log (once per distinct value per player). 0 disables.", 0f, 5000f);
            StaminaPlausibilityCeiling = BindSynced(config, configSync, "12 - Security", "StaminaPlausibilityCeiling", 500f, "Current stamina above this is flagged (detect-only - there is no real max to clamp to). 0 disables.", 0f, 5000f);
            PositionWatchEnabled = BindSynced(config, configSync, "12 - Security", "PositionWatchEnabled", true, "Flag implausible movement speed and likely fly/noclip.");
            PositionWatchInterval = BindSynced(config, configSync, "12 - Security", "PositionWatchInterval", 3f, "Seconds between position checks.", 0.5f, 30f);
            SpeedPlausibilityCeiling = BindSynced(config, configSync, "12 - Security", "SpeedPlausibilityCeiling", 40f, "Movement speed (m/s) above which a player is flagged.", 5f, 200f);
            FlyDetectionTolerance = BindSynced(config, configSync, "12 - Security", "FlyDetectionTolerance", 15f, "Meters a player's Y can exceed expected ground height before being flagged. Deliberately generous - mining/caving produces real negative deltas too.", 1f, 200f);
            ItemIntegritySweepEnabled = BindSynced(config, configSync, "12 - Security", "ItemIntegritySweepEnabled", true, "Periodically check every tracked container's inventory for fabricated items. Containers only - a player's own bag is never networked to the server, so there is nothing there to check.");
            ItemIntegritySweepInterval = BindSynced(config, configSync, "12 - Security", "ItemIntegritySweepInterval", 30f, "Seconds between integrity sweep batches.", 5f, 600f);
            ItemIntegritySweepBatchSize = BindSyncedInt(config, configSync, "12 - Security", "ItemIntegritySweepBatchSize", 25, "How many container ZDOs to advance the scanner by per sweep.", 1, 500);
            ItemIntegritySweepCorrect = BindSynced(config, configSync, "12 - Security", "ItemIntegritySweepCorrect", false, "If true, remove implausible items outright instead of only logging them.");

            DiscordLifecycleInterval = BindLocal(config, "14 - Discord Notify", "DiscordLifecycleInterval", 3f, "Seconds between the sweep that detects player deaths and first-time joins.");
            DiscordNotifyDeaths = BindLocal(config, "14 - Discord Notify", "DiscordNotifyDeaths", true, "Post in Discord whenever a connected player dies.");
            DiscordDeathMessage = BindLocal(config, "14 - Discord Notify", "DiscordDeathMessage", "\U0001F480 **{player}** died", "Message posted when a player dies. {player} is replaced with their name.");
            DiscordNotifyFirstJoin = BindLocal(config, "14 - Discord Notify", "DiscordNotifyFirstJoin", true, "Post a welcome message in Discord the first time a new character ever joins this world. Works even if Starter Grant (section 10) is turned off - this is tracked separately.");
            DiscordFirstJoinMessage = BindLocal(config, "14 - Discord Notify", "DiscordFirstJoinMessage", "\U0001F389 **{player}** joined {world} for the first time - welcome!", "Message posted the first time a character joins. {player} and {world} are replaced.");
            DiscordNotifyBossDefeats = BindLocal(config, "14 - Discord Notify", "DiscordNotifyBossDefeats", true, "Post in Discord when one of the five classic bosses (Eikthyr, The Elder, Bonemass, Moder, Yagluth) is defeated for the first time. Only fires once per boss per world - restarting the server won't re-post it.");
            DiscordNotifyHeartbeat = BindLocal(config, "14 - Discord Notify", "DiscordNotifyHeartbeat", false, "Post a periodic 'server is alive' status update in Discord, showing uptime and who's currently online - handy for a community to check who's playing without opening the game. Uses the same timer as the log-only heartbeat in section 1 (HeartbeatIntervalMinutes). Off by default since posting to a channel every 15 minutes adds up over a day - turn on if your community wants it, and raise the interval in section 1 if once every 15 minutes is too chatty for your channel.");
            DiscordHeartbeatMessage = BindLocal(config, "14 - Discord Notify", "DiscordHeartbeatMessage", "\U0001F49A **{world}** heartbeat - up {uptime} | {playercount} player(s) online: {players}", "Message posted for the Discord heartbeat above. Placeholders: {world}, {uptime}, {playercount} (a number), {players} (comma-separated names, or 'none' if nobody's online).");
            DiscordBossDefeatMessage = BindLocal(config, "14 - Discord Notify", "DiscordBossDefeatMessage", "⚔️ **{boss}** has been defeated on {world}!", "Message posted when a boss is defeated. {boss} and {world} are replaced.");

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
            removedAny |= TryMigrate(config, "3 - Inventory & Vacuum", "ContainerVacuumEnabled", VacuumEnabled);
            removedAny |= TryMigrate(config, "3 - Inventory & Vacuum", "ContainerVacuumRadius", VacuumRadius);
            removedAny |= TryMigrate(config, "3 - Inventory & Vacuum", "ContainerVacuumInterval", VacuumInterval);
            removedAny |= TryMigrate(config, "5 - Production & AutoFuel", "AutoFuelLightSources", ProductionSupplyEnabled);
            removedAny |= TryMigrate(config, "5 - Production & AutoFuel", "AutoFuelRadius", ProductionSupplyRange);
            removedAny |= TryMigrate(config, "8 - World & Portals & Raids", "BlockHighTierRaidsInLowBiomes", RaidBlockEnabled);
            // 0.1.0 -> 0.2.0: the Vitality section went away (its max-HP floor was verified impossible
            // server-side); its check interval moved next to the vitals guard it actually timed.
            removedAny |= TryMigrate(config, "11 - Vitality", "VitalityCheckInterval", VitalsGuardInterval);

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
