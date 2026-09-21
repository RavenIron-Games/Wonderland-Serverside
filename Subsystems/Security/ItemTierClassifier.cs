using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Security
{
    public enum ItemTier
    {
        None = 0,
        Meadows = 1,
        BlackForest = 2,
        Swamp = 3,
        Mountain = 4,
        Plains = 5,
        Mistlands = 6,
        Ashlands = 7,
        DeepNorth = 8,
        Cheat = 99
    }

    /// <summary>
    /// Places every item on the world's progression ledger (BlackForest at day one, Swamp once The Elder
    /// falls, Mountain on Bonemass, Plains on Moder, Mistlands on Yagluth, Ashlands on The Queen, DeepNorth on
    /// Fader) from the game's own data instead of a keyword list. 0.10.2's substring classifier called
    /// Feathers "Mistlands" and Barley "Plains" and, with ItemIntegritySweepCorrect on, deleted several
    /// hundred stacks of ordinary loot out of players' chests on the live server (see CHANGELOG 0.10.5).
    ///
    /// The rule, in one line: raw drops are free, processed and crafted is gated. An item's tier is the
    /// highest gated ingredient or station anywhere in the cheapest chain that produces it:
    ///  - Seeds are the ledger keys themselves - each boss's drop and trophy (you cannot hold The Elder's
    ///    key before The Elder is dead; Fader is the Ashlands boss and his key opens the Deep North, whose
    ///    own boss leaves only a drop) - plus the ores that only exist inside content those keys open
    ///    (crypt iron, wishbone silver, flametal, bloodgold) and the ingots and refined stock made from them.
    ///  - Station floors: anything crafted or converted at the artisan table and its dependants (blast
    ///    furnace, spinning wheel, windmill, oven - vanilla gates those behind Moder's tear), at the
    ///    Mistlands stations, or at the Deep North's Frost Foundry / Frigid Kiln is at least that tier.
    ///    Vanilla gates none of those behind a boss, so those floors are the ledger's policy, not vanilla's.
    ///  - Recipes (ObjectDB.m_recipes), smelter/cooking/fermenter conversions (Smelter.m_conversion and
    ///    friends on the ZNetScene prefabs) and the stations' own build requirements (Piece.m_resources,
    ///    Piece.m_craftingStation) are walked recursively; the cheapest recipe wins, an any-one-of recipe
    ///    (Recipe.m_requireOnlyOneIngredient) takes its cheapest ingredient.
    ///  - A crafted item whose prefab name carries a biome Iron Gate names items for (Ashlands, DeepNorth)
    ///    is at least that biome - the only name-based rule, applied to crafted items only, never to drops.
    ///  - Anything with no recipe and no conversion is a drop: scrap, pelts, needles, barley, mushrooms and
    ///    trophies carried home from a biome a player dared to visit are never confiscated.
    /// The whole derived table is written to BepInEx/config/Wonderland.ProgressionTiers.txt at world
    /// start, one line per item with the reason, so an operator can see exactly why an item is where it
    /// is; ProgressionItemExemptions (Prefab, or Prefab:Tier) pins any item over the derivation and is
    /// hot-reloaded.
    ///
    /// Everything read here was checked against the 1.0.15 server decompile: Recipe (m_item, m_enabled,
    /// m_craftingStation, m_requireOnlyOneIngredient, m_resources), Piece.Requirement (m_resItem,
    /// m_upgraderResource), Smelter/CookingStation/Fermenter.ItemConversion (m_from, m_to),
    /// ObjectDB.m_items/m_recipes, ZoneSystem.GetGlobalKey(GlobalKeys). Quality: 1.0's upgrader stations
    /// (CraftingStation.m_upgrader, InventoryGui 51005-51027) upgrade an item past m_shared.m_maxQuality
    /// when its recipe carries an m_upgraderResource requirement, so CanExceedMaxQuality is what
    /// EquipmentGuard's "impossible quality" check has to ask first.
    /// </summary>
    public static class ItemTierClassifier
    {
        private const string AuditFileName = "Wonderland.ProgressionTiers.txt";

        /// <summary>Where the derived table was last written (BepInEx/config/Wonderland.ProgressionTiers.txt).</summary>
        public static string AuditFilePath => Path.Combine(BepInEx.Paths.ConfigPath, AuditFileName);

        /// <summary>The same table as JSON, beside the BarrkBOT export (BepInEx/config/Wonderland/progression_tiers.json)
        /// for the website: {"generated_at", "source", "ledger", "seeds", "station_floors", "items": {prefab: {tier, reason}}}.
        /// Deliberately not a barrkbot_* name - the BarrkBOT scanner must never see two files claiming the same facts.</summary>
        public static string TierJsonPath => Path.Combine(BepInEx.Paths.ConfigPath, "Wonderland", "progression_tiers.json");

        /// <summary>Items per tier from the last audit write (ItemTier.None = unrestricted), for the BarrkBOT
        /// export; empty until the first world start.</summary>
        public static IReadOnlyDictionary<ItemTier, int> LastTierCounts => _lastTierCounts;
        private static readonly Dictionary<ItemTier, int> _lastTierCounts = new Dictionary<ItemTier, int>();

        /// <summary>Every item the audit placed at exactly this tier, sorted; empty until the first world start.
        /// The tier one above the ledger is what a player would be flagged for - the "restrictions" list.</summary>
        public static IReadOnlyList<string> ItemsAtTier(ItemTier tier)
        {
            return _lastItemsByTier.TryGetValue(tier, out List<string> list) ? list : Array.Empty<string>();
        }
        private static readonly Dictionary<ItemTier, List<string>> _lastItemsByTier = new Dictionary<ItemTier, List<string>>();

        /// <summary>The boss whose defeat lifts the ledger past <paramref name="current"/>, as the global key
        /// and the display name BossDefeatWatch uses (null at DeepNorth - nothing left to unlock).</summary>
        public static (string Key, string Boss)? NextUnlock(ItemTier current)
        {
            switch (current)
            {
                case ItemTier.None:
                case ItemTier.Meadows:
                case ItemTier.BlackForest: return ("defeated_gdking", "The Elder");
                case ItemTier.Swamp: return ("defeated_bonemass", "Bonemass");
                case ItemTier.Mountain: return ("defeated_dragon", "Moder");
                case ItemTier.Plains: return ("defeated_goblinking", "Yagluth");
                case ItemTier.Mistlands: return ("defeated_queen", "The Queen");
                case ItemTier.Ashlands: return ("defeated_fader", "Fader");
                default: return null;
            }
        }

        /// <summary>The ledger keys as items, and the ores/ingots that only exist behind them. Names as the
        /// prefabs spell them (matched case-insensitively). Ingots are seeded as well as their ore so the
        /// chain does not depend on the blast furnace's floor alone.</summary>
        private static readonly Dictionary<string, (ItemTier Tier, string Why)> SeedItems = new Dictionary<string, (ItemTier, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // Every name below was read out of the 1.0.15 asset bundle's ObjectDB (2026-09-20), not guessed.
            { "CryptKey", (ItemTier.Swamp, "The Elder's drop") },
            { "TrophyTheElder", (ItemTier.Swamp, "The Elder's trophy") },
            { "IronScrap", (ItemTier.Swamp, "only found in sunken crypts, behind the swamp key") },
            { "IronOre", (ItemTier.Swamp, "iron - nothing in the world drops this legacy ore, but the smelter takes it") },
            { "Iron", (ItemTier.Swamp, "smelted from crypt iron") },
            { "Wishbone", (ItemTier.Mountain, "Bonemass's drop") },
            { "TrophyBonemass", (ItemTier.Mountain, "Bonemass's trophy") },
            { "SilverOre", (ItemTier.Mountain, "found with the wishbone") },
            { "Silver", (ItemTier.Mountain, "smelted from wishbone silver") },
            { "DragonTear", (ItemTier.Plains, "Moder's drop") },
            { "TrophyDragonQueen", (ItemTier.Plains, "Moder's trophy") },
            { "YagluthDrop", (ItemTier.Mistlands, "Yagluth's drop") },
            { "TrophyGoblinKing", (ItemTier.Mistlands, "Yagluth's trophy") },
            { "QueenDrop", (ItemTier.Ashlands, "The Queen's drop") },
            { "TrophySeekerQueen", (ItemTier.Ashlands, "The Queen's trophy") },
            { "FlametalOre", (ItemTier.Ashlands, "Ashlands ore (legacy prefab)") },
            { "FlametalOreNew", (ItemTier.Ashlands, "Ashlands ore") },
            { "Flametal", (ItemTier.Ashlands, "smelted from Ashlands ore (legacy prefab)") },
            { "FlametalNew", (ItemTier.Ashlands, "smelted from Ashlands ore") },
            // Fader is the Ashlands boss; his key (defeated_fader) is what opens the Deep North on the ledger.
            { "FaderDrop", (ItemTier.DeepNorth, "Fader's drop") },
            { "TrophyFader", (ItemTier.DeepNorth, "Fader's trophy") },
            { "FrozenKingDrop", (ItemTier.DeepNorth, "the Deep North boss's drop (Kall Fimbulbringer has no trophy prefab)") },
            { "GoldOre", (ItemTier.DeepNorth, "Deep North ore (Petrified Tissue)") },
            { "Gold", (ItemTier.DeepNorth, "Bloodgold, smelted from Deep North ore") },
            { "Frostwood", (ItemTier.DeepNorth, "refined Deep North material (Timberwood)") },
            { "NornThread", (ItemTier.DeepNorth, "refined Deep North material (Nornathread)") },
        };

        /// <summary>Stations whose products are at least this tier whatever goes into them. The Plains
        /// entries are derivable (every one is built at, or is, the artisan table, which costs Moder's
        /// tear) and are listed so the audit file names the station rather than the tear; the Mistlands
        /// and Deep North entries are the ledger's policy - vanilla builds them from that biome's drops
        /// alone. Ashlands has no station of its own; its gear is gated through flametal.</summary>
        private static readonly Dictionary<string, ItemTier> StationFloors = new Dictionary<string, ItemTier>(StringComparer.OrdinalIgnoreCase)
        {
            { "piece_artisanstation", ItemTier.Plains },
            { "blastfurnace", ItemTier.Plains },
            { "piece_spinningwheel", ItemTier.Plains },
            { "windmill", ItemTier.Plains },
            { "piece_oven", ItemTier.Plains },
            { "blackforge", ItemTier.Mistlands },
            { "piece_magetable", ItemTier.Mistlands },
            { "eitrrefinery", ItemTier.Mistlands },
            // The Deep North's own stations (both built with FrostCore): the Frost Foundry finishes every
            // "Cast: ..." Bloodgold piece, the Frigid Kiln burns ice into FrozenFuel.
            { "piece_FrostFoundry", ItemTier.DeepNorth },
            { "piece_FrostKiln", ItemTier.DeepNorth },
        };

        /// <summary>Biomes Iron Gate spells into prefab names (ArmorAshlandsMediumChest, CapeDeepNorth,
        /// FeastDeepNorth). A crafted item or a station named for one is at least that tier. Drops are
        /// never judged by name.</summary>
        private static readonly (string Token, ItemTier Tier)[] NameFloors =
        {
            ("DeepNorth", ItemTier.DeepNorth),
            ("Ashlands", ItemTier.Ashlands),
        };

        private sealed class Conversion
        {
            public readonly string From;
            public readonly GameObject Station;
            public readonly string Kind;

            public Conversion(string from, GameObject station, string kind)
            {
                From = from;
                Station = station;
                Kind = kind;
            }
        }

        // The index: rebuilt whenever ObjectDB.instance is a different object (new world), never per query.
        private static ObjectDB? _indexedDb;
        private static readonly Dictionary<string, List<Recipe>> _recipesByItem = new Dictionary<string, List<Recipe>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<Conversion>> _conversionsByItem = new Dictionary<string, List<Conversion>>(StringComparer.Ordinal);
        private static readonly HashSet<string> _upgraderItems = new HashSet<string>(StringComparer.Ordinal);
        private static int _recipeCount;
        private static int _conversionCount;

        // Memo of resolved tiers and the reason each was chosen; cleared when the overrides change.
        private static readonly Dictionary<string, ItemTier> _tierByName = new Dictionary<string, ItemTier>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _reasonByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (ItemTier Tier, string Why)> _stationByName = new Dictionary<string, (ItemTier, string)>(StringComparer.Ordinal);
        private static readonly HashSet<string> _resolving = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _resolvingStations = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<int, ItemTier> _tierCache = new Dictionary<int, ItemTier>();

        private static HashSet<string>? _bannedCache;
        private static string? _cachedBannedString;
        private static string? _cachedExemptionsString;
        private static readonly Dictionary<string, ItemTier> _exemptionOverrides = new Dictionary<string, ItemTier>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Re-reads ProgressionItemExemptions ("Prefab" = unrestricted, "Prefab:Tier" = pinned) and
        /// BannedItemsList whenever either synced value changes, dropping every memoised tier so the change
        /// takes effect at once. Called at the public entry points only, never from inside a resolution.</summary>
        public static void EnsureExemptionsLoaded()
        {
            string bannedVal = WonderlandConfig.BannedItemsList?.Value ?? "";
            if (_bannedCache == null || _cachedBannedString != bannedVal)
            {
                _cachedBannedString = bannedVal;
                _bannedCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in bannedVal.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    _bannedCache.Add(part.Trim());
                }
                ClearMemo();
                _auditDirty = true;
            }

            string configVal = WonderlandConfig.ProgressionItemExemptions?.Value ?? "";
            if (_cachedExemptionsString == configVal)
            {
                return;
            }

            _cachedExemptionsString = configVal;
            _exemptionOverrides.Clear();
            ClearMemo();
            _auditDirty = true;

            if (string.IsNullOrWhiteSpace(configVal))
            {
                return;
            }

            string[] parts = configVal.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                int colon = trimmed.IndexOf(':');
                if (colon > 0)
                {
                    string itemName = trimmed.Substring(0, colon).Trim();
                    string tierName = trimmed.Substring(colon + 1).Trim();
                    ItemTier tier = ParseTier(tierName);
                    if (tier == ItemTier.None && !tierName.Equals("None", StringComparison.OrdinalIgnoreCase) && !tierName.Equals("Exempt", StringComparison.OrdinalIgnoreCase))
                    {
                        WonderlandDebug.LogWarning($"[Progression] ProgressionItemExemptions: '{trimmed}' names no tier (None, Meadows, BlackForest, Swamp, Mountain, Plains, Mistlands, Ashlands, DeepNorth) - treating {itemName} as unrestricted.");
                    }
                    _exemptionOverrides[itemName] = tier;
                }
                else if (!string.IsNullOrEmpty(trimmed))
                {
                    _exemptionOverrides[trimmed] = ItemTier.None;
                }
            }
        }

        private static ItemTier _lastAutoTier = ItemTier.None;

        public static ItemTier GetEffectiveMaxTier()
        {
            string configVal = WonderlandConfig.MaxAllowedTier?.Value ?? "Auto";
            if (string.IsNullOrWhiteSpace(configVal) || configVal.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return ItemTier.None;
            }

            if (!configVal.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return ParseTier(configVal);
            }

            return ResolveAutoTierFromWorld();
        }

        public static ItemTier ResolveAutoTierFromWorld()
        {
            if (ZoneSystem.instance == null)
            {
                return _lastAutoTier != ItemTier.None ? _lastAutoTier : ItemTier.BlackForest;
            }

            ItemTier tier;
            if (ZoneSystem.instance.GetGlobalKey("defeated_fader"))
            {
                tier = ItemTier.DeepNorth;
            }
            else if (ZoneSystem.instance.GetGlobalKey("defeated_queen"))
            {
                tier = ItemTier.Ashlands;
            }
            else if (ZoneSystem.instance.GetGlobalKey("defeated_goblinking"))
            {
                tier = ItemTier.Mistlands;
            }
            else if (ZoneSystem.instance.GetGlobalKey("defeated_dragon"))
            {
                tier = ItemTier.Plains;
            }
            else if (ZoneSystem.instance.GetGlobalKey("defeated_bonemass"))
            {
                tier = ItemTier.Mountain;
            }
            else if (ZoneSystem.instance.GetGlobalKey("defeated_gdking"))
            {
                tier = ItemTier.Swamp;
            }
            else
            {
                // Baseline: Eikthyr defeat is ignored because of starter grant (Bronze tools / Karve).
                tier = ItemTier.BlackForest;
            }

            _lastAutoTier = tier;
            return tier;
        }

        public static void OnBossDefeated(string bossKey, string bossName)
        {
            ItemTier oldTier = _lastAutoTier;
            ItemTier newTier = ResolveAutoTierFromWorld();
            if (newTier > oldTier)
            {
                WonderlandDebug.LogAlways($"[Progression] Boss '{bossName}' ({bossKey}) defeated! World progression tier advanced: {oldTier} -> {newTier}. Gear up to {newTier} is now permitted.");
                EquipmentGuard.ResetPlayerCache();
            }
        }

        public static ItemTier ParseTier(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return ItemTier.None;

            string clean = text.Trim().Replace(" ", "").Replace("_", "");
            if (Enum.TryParse<ItemTier>(clean, true, out ItemTier tier))
            {
                return tier;
            }

            return ItemTier.None;
        }

        public static bool IsBanned(string? prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;

            if (_bannedCache == null || _cachedBannedString != (WonderlandConfig.BannedItemsList?.Value ?? ""))
            {
                EnsureExemptionsLoaded();
            }

            if (_bannedCache!.Contains(prefabName))
            {
                return true;
            }

            // Default cheat prefixes
            if (prefabName.IndexOf("Cheat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prefabName.IndexOf("Cheater", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>World start: build the index if ObjectDB is already there. It often is not - ObjectDB.Awake
        /// can run after the ZNetScene.Awake hook this fires from (the Unity Awake-ordering race from
        /// IMPLEMENTATIONS/Wonderland.md; on the 2026-09-20 15:56Z boot the index only came up on first use and
        /// 0.10.5 never wrote its table) - so the real work is OnUpdate's lazy retry, and this is only a head start.</summary>
        public static void OnWorldReady()
        {
            if (!EnsureIndex())
            {
                WonderlandDebug.LogInfo("[Progression] ObjectDB not up yet at world ready - the tier table is built and written on the first tick it is.");
            }
        }

        /// <summary>Every frame from SecuritySubsystem: build the index the first tick ObjectDB exists (or a new
        /// one appears), and rewrite the two table files whenever they are stale - after the build, and after a
        /// ProgressionItemExemptions or BannedItemsList edit, so the website's JSON follows a pin within a second.
        /// Both are cheap null/reference checks on the frames where nothing changed.</summary>
        public static void OnUpdate()
        {
            if (!EnsureIndex())
            {
                return;
            }
            EnsureExemptionsLoaded();
            if (_auditDirty)
            {
                _auditDirty = false;
                WriteAuditFile();
            }
        }

        /// <summary>True from an index build or a config change until WriteAuditFile has run.</summary>
        private static bool _auditDirty;

        public static ItemTier GetTier(GameObject? prefab, ItemDrop.ItemData? itemData = null)
        {
            if (prefab == null) return ItemTier.None;

            EnsureExemptionsLoaded(); // a changed pin or ban list empties every cache before the lookup
            int hash = prefab.name.GetStableHashCode();
            if (_tierCache.TryGetValue(hash, out ItemTier cached))
            {
                return cached;
            }

            if (!EnsureIndex())
            {
                return ItemTier.None; // nothing to derive from yet - not cached, so the first real query builds it
            }

            ItemTier evaluated = Resolve(prefab.name, out _);
            _tierCache[hash] = evaluated;
            return evaluated;
        }

        public static ItemTier GetTierByName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName) || !EnsureIndex())
            {
                return ItemTier.None;
            }
            EnsureExemptionsLoaded();
            return Resolve(prefabName, out _);
        }

        /// <summary>The one-line reason behind an item's tier, for flag messages and the audit file.</summary>
        public static string GetTierReason(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName) || !EnsureIndex())
            {
                return "tier table not built";
            }
            EnsureExemptionsLoaded();
            Resolve(prefabName, out string reason);
            return reason;
        }

        /// <summary>True when the item can legitimately sit above m_shared.m_maxQuality: its recipe carries
        /// an upgrader resource (1.0's upgrader stations take it further, InventoryGui 51027), or the world
        /// runs with the NoCraftCost key, which lifts the cap for everything.</summary>
        public static bool CanExceedMaxQuality(GameObject? prefab)
        {
            if (prefab == null) return false;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost))
            {
                return true;
            }
            if (!EnsureIndex())
            {
                return true; // cannot tell yet - never flag on a guess
            }
            return _upgraderItems.Contains(prefab.name);
        }

        public static bool ExceedsTier(ItemTier itemTier, ItemTier maxTier)
        {
            if (itemTier == ItemTier.Cheat)
            {
                return true;
            }

            if (maxTier == ItemTier.None || itemTier == ItemTier.None)
            {
                return false;
            }

            return (int)itemTier > (int)maxTier;
        }

        // ---------------------------------------------------------------------------------------------
        // Index
        // ---------------------------------------------------------------------------------------------

        private static bool EnsureIndex()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || ZNetScene.instance == null)
            {
                return false;
            }
            if (ReferenceEquals(db, _indexedDb))
            {
                return true;
            }

            _recipesByItem.Clear();
            _conversionsByItem.Clear();
            _upgraderItems.Clear();
            _recipeCount = 0;
            _conversionCount = 0;
            ClearMemo();

            foreach (Recipe recipe in db.m_recipes)
            {
                if (recipe == null || !recipe.m_enabled || recipe.m_item == null)
                {
                    continue;
                }
                string itemName = recipe.m_item.gameObject.name;
                if (!_recipesByItem.TryGetValue(itemName, out List<Recipe> list))
                {
                    list = new List<Recipe>(1);
                    _recipesByItem[itemName] = list;
                }
                list.Add(recipe);
                _recipeCount++;
                // Upgrader-eligible: the requirement flag, or one of the Forge of Potential's idols
                // (Upgrader0..7Armor / Upgrader0..7Weapon - amount 1, perLevel 0, recover 0 in 271 of the 429
                // enabled 1.0.15 recipes) among the resources. Either reading errs toward not flagging.
                Piece.Requirement[] resources = recipe.m_resources;
                for (int i = 0; resources != null && i < resources.Length; i++)
                {
                    Piece.Requirement req = resources[i];
                    if (req == null)
                    {
                        continue;
                    }
                    if (req.m_upgraderResource || (req.m_resItem != null && req.m_resItem.gameObject.name.StartsWith("Upgrader", StringComparison.Ordinal)))
                    {
                        _upgraderItems.Add(itemName);
                        break;
                    }
                }
            }

            foreach (GameObject go in ZNetScene.instance.m_prefabs)
            {
                if (go == null)
                {
                    continue;
                }
                Smelter smelter = go.GetComponent<Smelter>();
                if (smelter != null && smelter.m_conversion != null)
                {
                    foreach (Smelter.ItemConversion conv in smelter.m_conversion)
                    {
                        AddConversion(conv?.m_from, conv?.m_to, go, "smelted");
                    }
                }
                CookingStation cooking = go.GetComponent<CookingStation>();
                if (cooking != null && cooking.m_conversion != null)
                {
                    foreach (CookingStation.ItemConversion conv in cooking.m_conversion)
                    {
                        AddConversion(conv?.m_from, conv?.m_to, go, "cooked");
                    }
                }
                Fermenter fermenter = go.GetComponent<Fermenter>();
                if (fermenter != null && fermenter.m_conversion != null)
                {
                    foreach (Fermenter.ItemConversion conv in fermenter.m_conversion)
                    {
                        AddConversion(conv?.m_from, conv?.m_to, go, "fermented");
                    }
                }
            }

            _indexedDb = db;
            _auditDirty = true;
            WonderlandDebug.LogInfo($"[Progression] Indexed {_recipeCount} recipes for {_recipesByItem.Count} items and {_conversionCount} conversions into {_conversionsByItem.Count} items; {_upgraderItems.Count} items take an upgrader resource.");
            return true;
        }

        /// <summary>A conversion with no input item (the Frigid Kiln makes FrozenFuel from its fuel alone)
        /// is kept with an empty From: the product is then gated by the station only.</summary>
        private static void AddConversion(ItemDrop? from, ItemDrop? to, GameObject station, string kind)
        {
            if (to == null)
            {
                return;
            }
            string toName = to.gameObject.name;
            if (!_conversionsByItem.TryGetValue(toName, out List<Conversion> list))
            {
                list = new List<Conversion>(1);
                _conversionsByItem[toName] = list;
            }
            list.Add(new Conversion(from != null ? from.gameObject.name : "", station, kind));
            _conversionCount++;
        }

        private static void ClearMemo()
        {
            _tierByName.Clear();
            _reasonByName.Clear();
            _stationByName.Clear();
            _tierCache.Clear();
            _resolving.Clear();
            _resolvingStations.Clear();
        }

        // ---------------------------------------------------------------------------------------------
        // Resolution
        // ---------------------------------------------------------------------------------------------

        private static ItemTier Resolve(string name, out string reason)
        {
            if (_tierByName.TryGetValue(name, out ItemTier memo))
            {
                reason = _reasonByName[name];
                return memo;
            }

            if (IsBanned(name))
            {
                reason = "banned (BannedItemsList or a cheat prefab)";
                return Remember(name, ItemTier.Cheat, reason);
            }

            if (_exemptionOverrides.TryGetValue(name, out ItemTier pinned))
            {
                reason = pinned == ItemTier.None ? "unrestricted by ProgressionItemExemptions" : "pinned by ProgressionItemExemptions";
                return Remember(name, pinned, reason);
            }

            if (SeedItems.TryGetValue(name, out (ItemTier Tier, string Why) seed))
            {
                reason = seed.Why;
                return Remember(name, seed.Tier, reason);
            }

            if (!_resolving.Add(name))
            {
                reason = "recipe cycle"; // not remembered: the outer frame decides
                return ItemTier.None;
            }

            try
            {
                bool anyPath = false;
                ItemTier best = ItemTier.None;
                string bestReason = "drop - no recipe or conversion makes it";

                if (_recipesByItem.TryGetValue(name, out List<Recipe> recipes))
                {
                    foreach (Recipe recipe in recipes)
                    {
                        ItemTier tier = RecipeTier(recipe, out string why);
                        if (!anyPath || tier < best)
                        {
                            best = tier;
                            bestReason = why;
                        }
                        anyPath = true;
                    }
                }

                if (_conversionsByItem.TryGetValue(name, out List<Conversion> conversions))
                {
                    foreach (Conversion conversion in conversions)
                    {
                        (ItemTier stationTier, string stationWhy) = StationTier(conversion.Station);
                        string fromWhy = "";
                        ItemTier fromTier = conversion.From.Length > 0 ? Resolve(conversion.From, out fromWhy) : ItemTier.None;
                        ItemTier tier;
                        string why;
                        if (fromTier > stationTier)
                        {
                            tier = fromTier;
                            why = $"{conversion.Kind} from {conversion.From} ({fromWhy})";
                        }
                        else
                        {
                            tier = stationTier;
                            why = stationTier == ItemTier.None
                                ? $"{conversion.Kind} from {(conversion.From.Length > 0 ? conversion.From : "fuel")} at {conversion.Station.name} - nothing gated"
                                : $"{conversion.Kind} at {conversion.Station.name} ({stationWhy})";
                        }
                        if (!anyPath || tier < best)
                        {
                            best = tier;
                            bestReason = why;
                        }
                        anyPath = true;
                    }
                }

                if (anyPath)
                {
                    foreach ((string token, ItemTier floor) in NameFloors)
                    {
                        if (floor > best && name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            best = floor;
                            bestReason = $"crafted item named for {token}";
                        }
                    }
                }

                reason = bestReason;
                return Remember(name, best, reason);
            }
            finally
            {
                _resolving.Remove(name);
            }
        }

        private static ItemTier Remember(string name, ItemTier tier, string reason)
        {
            _tierByName[name] = tier;
            _reasonByName[name] = reason;
            return tier;
        }

        /// <summary>A recipe's tier: its station's floor and build chain, plus its ingredients - the highest
        /// of them, or the lowest when any single one will do (Recipe.m_requireOnlyOneIngredient).</summary>
        private static ItemTier RecipeTier(Recipe recipe, out string why)
        {
            ItemTier tier = ItemTier.None;
            why = "hand-crafted from ungated materials";

            if (recipe.m_craftingStation != null)
            {
                (ItemTier stationTier, string stationWhy) = StationTier(recipe.m_craftingStation.gameObject);
                if (stationTier > tier)
                {
                    tier = stationTier;
                    why = $"crafted at {recipe.m_craftingStation.gameObject.name} ({stationWhy})";
                }
            }

            Piece.Requirement[] resources = recipe.m_resources;
            if (resources == null || resources.Length == 0)
            {
                return tier;
            }

            bool anyOne = recipe.m_requireOnlyOneIngredient;
            ItemTier ingredients = anyOne ? ItemTier.Cheat : ItemTier.None; // Cheat = "above everything" as the min sentinel
            string ingredientWhy = "";
            bool seen = false;
            for (int i = 0; i < resources.Length; i++)
            {
                ItemDrop res = resources[i]?.m_resItem;
                if (res == null)
                {
                    continue;
                }
                string resName = res.gameObject.name;
                ItemTier resTier = Resolve(resName, out string resWhy);
                seen = true;
                if (anyOne ? resTier < ingredients : resTier > ingredients)
                {
                    ingredients = resTier;
                    ingredientWhy = $"needs {resName} ({resWhy})";
                }
            }
            if (!seen)
            {
                return tier;
            }
            if (anyOne && ingredients == ItemTier.Cheat)
            {
                ingredients = ItemTier.None;
            }
            if (ingredients > tier)
            {
                tier = ingredients;
                why = ingredientWhy;
            }
            return tier;
        }

        /// <summary>A station's tier: its floor, its name, what it costs to build, and the station it must
        /// be built beside - the highest of them, memoised by prefab name.</summary>
        private static (ItemTier, string) StationTier(GameObject? station)
        {
            if (station == null)
            {
                return (ItemTier.None, "no station");
            }
            string name = station.name;
            if (_stationByName.TryGetValue(name, out (ItemTier Tier, string Why) memo))
            {
                return memo;
            }
            if (!_resolvingStations.Add(name))
            {
                return (ItemTier.None, "station cycle");
            }

            try
            {
                ItemTier tier = ItemTier.None;
                string why = $"{name} costs nothing gated";
                if (StationFloors.TryGetValue(name, out ItemTier floor))
                {
                    tier = floor;
                    why = $"{name} is a {floor} station";
                }
                foreach ((string token, ItemTier nameFloor) in NameFloors)
                {
                    if (nameFloor > tier && name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        tier = nameFloor;
                        why = $"{name} is named for {token}";
                    }
                }

                Piece piece = station.GetComponent<Piece>();
                if (piece != null)
                {
                    Piece.Requirement[] resources = piece.m_resources;
                    for (int i = 0; resources != null && i < resources.Length; i++)
                    {
                        ItemDrop res = resources[i]?.m_resItem;
                        if (res == null)
                        {
                            continue;
                        }
                        ItemTier resTier = Resolve(res.gameObject.name, out string resWhy);
                        if (resTier > tier)
                        {
                            tier = resTier;
                            why = $"{name} is built with {res.gameObject.name} ({resWhy})";
                        }
                    }
                    if (piece.m_craftingStation != null && !ReferenceEquals(piece.m_craftingStation.gameObject, station))
                    {
                        (ItemTier parentTier, string parentWhy) = StationTier(piece.m_craftingStation.gameObject);
                        if (parentTier > tier)
                        {
                            tier = parentTier;
                            why = $"{name} is built at {piece.m_craftingStation.gameObject.name} ({parentWhy})";
                        }
                    }
                }

                (ItemTier, string) result = (tier, why);
                _stationByName[name] = result;
                return result;
            }
            finally
            {
                _resolvingStations.Remove(name);
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Audit file
        // ---------------------------------------------------------------------------------------------

        /// <summary>Every ObjectDB item with its tier and reason, highest tier first, so "why was my X
        /// flagged" is a grep away and a wrong placement is a config pin, not a rebuild.</summary>
        private static void WriteAuditFile()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null)
            {
                return;
            }

            EnsureExemptionsLoaded();
            var rows = new List<(ItemTier Tier, string Name, string Reason)>(db.m_items.Count);
            var counts = new Dictionary<ItemTier, int>();
            _lastItemsByTier.Clear();
            foreach (GameObject item in db.m_items)
            {
                if (item == null || item.GetComponent<ItemDrop>() == null)
                {
                    continue;
                }
                ItemTier tier = Resolve(item.name, out string reason);
                _tierCache[item.name.GetStableHashCode()] = tier;
                rows.Add((tier, item.name, reason));
                counts.TryGetValue(tier, out int n);
                counts[tier] = n + 1;
                if (!_lastItemsByTier.TryGetValue(tier, out List<string> names))
                {
                    names = new List<string>();
                    _lastItemsByTier[tier] = names;
                }
                names.Add(item.name);
            }
            foreach (List<string> names in _lastItemsByTier.Values)
            {
                names.Sort(string.CompareOrdinal);
            }
            _lastTierCounts.Clear();
            foreach (KeyValuePair<ItemTier, int> kv in counts)
            {
                _lastTierCounts[kv.Key] = kv.Value;
            }
            rows.Sort((a, b) =>
            {
                int byTier = ((int)b.Tier).CompareTo((int)a.Tier);
                return byTier != 0 ? byTier : string.CompareOrdinal(a.Name, b.Name);
            });

            var summary = new StringBuilder();
            foreach (ItemTier tier in new[] { ItemTier.Cheat, ItemTier.DeepNorth, ItemTier.Ashlands, ItemTier.Mistlands, ItemTier.Plains, ItemTier.Mountain, ItemTier.Swamp, ItemTier.BlackForest, ItemTier.Meadows, ItemTier.None })
            {
                counts.TryGetValue(tier, out int n);
                if (n > 0)
                {
                    summary.Append(summary.Length > 0 ? ", " : "").Append(tier == ItemTier.None ? "unrestricted" : tier.ToString()).Append(' ').Append(n);
                }
            }

            string path = AuditFilePath;
            try
            {
                var sb = new StringBuilder(rows.Count * 96);
                sb.Append("# Wonderland ").Append(WonderlandPlugin.ModVersion).Append(" progression tiers - derived ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).Append(" UTC from ObjectDB (")
                  .Append(rows.Count).Append(" items, ").Append(_recipeCount).Append(" recipes, ").Append(_conversionCount).Append(" conversions)").AppendLine();
                sb.AppendLine("# Rule: raw drops are unrestricted; an item's tier is the highest gated seed or station in the cheapest chain that makes it.");
                sb.Append("# Seeds:");
                foreach (KeyValuePair<string, (ItemTier Tier, string Why)> seed in SeedItems)
                {
                    sb.Append(' ').Append(seed.Key).Append('=').Append(seed.Value.Tier);
                }
                sb.AppendLine();
                sb.Append("# Station floors:");
                foreach (KeyValuePair<string, ItemTier> floor in StationFloors)
                {
                    sb.Append(' ').Append(floor.Key).Append('=').Append(floor.Value);
                }
                sb.AppendLine();
                sb.AppendLine("# Name floors (crafted items and stations only): DeepNorth, Ashlands. Seeds and floors verified against the 1.0.15 asset bundle (2026-09-20).");
                sb.Append("# Ledger now: ").Append(GetEffectiveMaxTier()).Append(" (MaxAllowedTier = ").Append(WonderlandConfig.MaxAllowedTier?.Value ?? "Auto").AppendLine(")");
                sb.AppendLine("# Pin or free any item with ProgressionItemExemptions in wubarrk.wonderland.cfg: Prefab (unrestricted) or Prefab:Tier - hot-reloaded, no restart.");
                sb.Append("# Counts: ").AppendLine(summary.ToString());
                sb.AppendLine("#");
                sb.AppendLine("# Tier\tItem\tReason");
                foreach ((ItemTier tier, string name, string reason) in rows)
                {
                    sb.Append(tier == ItemTier.None ? "Unrestricted" : tier.ToString()).Append('\t').Append(name).Append('\t').AppendLine(reason);
                }
                File.WriteAllText(path, sb.ToString());
                WonderlandDebug.LogAlways($"[Progression] Tier table: {summary} - written to {path}");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[Progression] Tier table: {summary} - could not write {path}: {ex.Message}");
            }

            string jsonPath = TierJsonPath;
            try
            {
                var seeds = new Dictionary<string, string>();
                foreach (KeyValuePair<string, (ItemTier Tier, string Why)> seed in SeedItems)
                {
                    seeds[seed.Key] = seed.Value.Tier.ToString();
                }
                var floors = new Dictionary<string, string>();
                foreach (KeyValuePair<string, ItemTier> floor in StationFloors)
                {
                    floors[floor.Key] = floor.Value.ToString();
                }
                var items = new Dictionary<string, object>(rows.Count);
                foreach ((ItemTier tier, string name, string reason) in rows)
                {
                    items[name] = new Dictionary<string, string>
                    {
                        ["tier"] = tier == ItemTier.None ? "Unrestricted" : tier.ToString(),
                        ["reason"] = reason,
                    };
                }
                var doc = new Dictionary<string, object>
                {
                    ["generated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    ["source"] = $"{WonderlandPlugin.ModName} {WonderlandPlugin.ModVersion}",
                    ["ledger"] = GetEffectiveMaxTier() == ItemTier.None ? "" : GetEffectiveMaxTier().ToString(),
                    ["tiers"] = new[] { "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands", "DeepNorth" },
                    ["seeds"] = seeds,
                    ["station_floors"] = floors,
                    ["name_floors"] = new[] { "DeepNorth", "Ashlands" },
                    ["items"] = items,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));
                File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(doc, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[Progression] could not write {jsonPath}: {ex.Message}");
            }
        }
    }
}
