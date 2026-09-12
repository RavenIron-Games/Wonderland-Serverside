using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;
using Wonderland.Subsystems.Storage;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// Keeps Fireplace-family (torch/hearth/campfire/sconce) and Smelter-family (smelter/blast
    /// furnace/kiln) fed from linked containers within a configurable range, with a reserve floor so
    /// a source container's matching stock is never fully drained. Runs independent of player
    /// proximity or online status - it operates purely on ZDO fields (ZDOVars.s_fuel, s_queued, and
    /// Smelter's dynamic "item0".."itemN-1" ore-queue keys), verified directly against the decompile,
    /// never a live GameObject - so a base doesn't go dark because its owner logged off.
    /// Two distinct resources for Smelter-family, not one: fuel (what keeps it burning) and the ore/
    /// process-material queue (what's actually being converted) are separate ZDO fields with
    /// separate caps (m_maxFuel vs m_maxOre).
    ///
    /// Two player-facing controls sit on top: a station a player has switched off (SupplySwitch) is
    /// skipped entirely, and a charcoal kiln - any smelter-family station whose only product is Coal -
    /// is only ever loaded with the wood types listed in KilnWoodTypes, so fine wood, core wood and
    /// blackwood in a linked chest are not quietly turned into coal. Both only govern what this engine
    /// loads; a player feeding a station by hand is vanilla and untouched.
    /// </summary>
    public static class ProductionSupplyEngine
    {
        private const string Tag = "ProductionSupply";
        private const string KilnProduct = "Coal";

        private static ZdoSpatialQuery.PrefabSetScanner _fireplaceScanner;
        private static ZdoSpatialQuery.PrefabSetScanner _smelterScanner;
        /// <summary>Prefab hash of every tracked station -> its own display name ("$piece_charcoalkiln"), for player toasts.</summary>
        private static readonly Dictionary<int, string> _stations = new Dictionary<int, string>();
        private static readonly HashSet<int> _kilns = new HashSet<int>();
        private static HashSet<string> _kilnInputs;
        private static string _kilnInputsRaw;
        private static float _timer;
        private static readonly List<ZDO> _buffer = new List<ZDO>();
        private static readonly List<ZDO> _nearBuffer = new List<ZDO>();

        public static void Initialize()
        {
            _stations.Clear();
            _kilns.Clear();
            var fireplaceNames = new List<string>();
            var smelterNames = new List<string>();
            var kilnNames = new List<string>();
            if (ZNetScene.instance != null)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null)
                    {
                        continue;
                    }
                    int hash = prefab.name.GetStableHashCode();
                    Fireplace fireplace = prefab.GetComponent<Fireplace>();
                    if (fireplace != null)
                    {
                        fireplaceNames.Add(prefab.name);
                        _stations[hash] = string.IsNullOrEmpty(fireplace.m_name) ? prefab.name : fireplace.m_name;
                    }
                    Smelter smelter = prefab.GetComponent<Smelter>();
                    if (smelter != null)
                    {
                        smelterNames.Add(prefab.name);
                        _stations[hash] = string.IsNullOrEmpty(smelter.m_name) ? prefab.name : smelter.m_name;
                        if (IsKiln(smelter))
                        {
                            _kilns.Add(hash);
                            kilnNames.Add(prefab.name);
                        }
                    }
                }
            }
            _fireplaceScanner = new ZdoSpatialQuery.PrefabSetScanner(fireplaceNames);
            _smelterScanner = new ZdoSpatialQuery.PrefabSetScanner(smelterNames);
            WonderlandDebug.LogInfo($"[ProductionSupplyEngine] tracking {fireplaceNames.Count} fireplace-family and {smelterNames.Count} smelter-family prefab types.");

            HashSet<string> allowed = KilnInputs();
            string filter = allowed.Count == 0 ? "any wood the kiln accepts" : string.Join(", ", allowed);
            WonderlandDebug.LogAlways($"[ProductionSupplyEngine] kiln input filter: {filter} - applies to {kilnNames.Count} kiln prefab(s): {string.Join(", ", kilnNames)}.");
        }

        public static void OnUpdate(float dt)
        {
            if (_fireplaceScanner == null || WonderlandConfig.ProductionSupplyEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.ProductionSupplyInterval?.Value ?? 3f))
            {
                return;
            }
            _timer = 0f;

            int budget = Mathf.Max(1, WonderlandConfig.ProductionSupplyBatchSize?.Value ?? 20);

            _buffer.Clear();
            for (int i = 0; i < budget; i++)
            {
                _fireplaceScanner.Advance(_buffer);
            }
            foreach (ZDO zdo in _buffer)
            {
                ProcessFireplace(zdo);
            }

            _buffer.Clear();
            for (int i = 0; i < budget; i++)
            {
                _smelterScanner.Advance(_buffer);
            }
            foreach (ZDO zdo in _buffer)
            {
                ProcessSmelter(zdo);
            }
        }

        /// <summary>
        /// The closest tracked station (any fireplace- or smelter-family object) within range of a
        /// point, with its display name. This is what a player's emote is aimed at.
        /// </summary>
        public static bool TryFindNearestStation(Vector3 position, float range, out ZDO station, out string displayName)
        {
            station = null;
            displayName = "";
            float best = float.MaxValue;
            foreach (ZDO zdo in ZdoSpatialQuery.FindNear(position, range, _nearBuffer))
            {
                if (!_stations.TryGetValue(zdo.GetPrefab(), out string name))
                {
                    continue;
                }
                float distance = (zdo.GetPosition() - position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    station = zdo;
                    displayName = name;
                }
            }
            return station != null;
        }

        private static bool IsKiln(Smelter template)
        {
            if (template.m_conversion == null || template.m_conversion.Count == 0)
            {
                return false;
            }
            foreach (Smelter.ItemConversion conversion in template.m_conversion)
            {
                if (conversion == null || conversion.m_to == null || conversion.m_to.gameObject.name != KilnProduct)
                {
                    return false;
                }
            }
            return true;
        }

        private static HashSet<string> KilnInputs()
        {
            string raw = WonderlandConfig.KilnWoodTypes?.Value ?? "Wood";
            if (_kilnInputs == null || !string.Equals(raw, _kilnInputsRaw, StringComparison.Ordinal))
            {
                _kilnInputsRaw = raw;
                _kilnInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in raw.Split(','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0)
                    {
                        _kilnInputs.Add(trimmed);
                    }
                }
            }
            return _kilnInputs;
        }

        private static void ProcessFireplace(ZDO zdo)
        {
            if (!zdo.IsValid() || SupplySwitch.IsOff(zdo.m_uid))
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Fireplace template = prefab != null ? prefab.GetComponent<Fireplace>() : null;
            if (template == null || template.m_infiniteFuel || template.m_fuelItem == null)
            {
                return;
            }

            float fuel = zdo.GetFloat(ZDOVars.s_fuel);
            if (fuel >= template.m_maxFuel - 0.01f)
            {
                return;
            }

            if (TryConsumeOne(zdo.GetPosition(), template.m_fuelItem.gameObject.name))
            {
                zdo.SetOwner(ZNet.GetUID());
                zdo.Set(ZDOVars.s_fuel, Mathf.Min((float)template.m_maxFuel, fuel + 1f));
            }
        }

        private static void ProcessSmelter(ZDO zdo)
        {
            if (!zdo.IsValid() || SupplySwitch.IsOff(zdo.m_uid))
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Smelter template = prefab != null ? prefab.GetComponent<Smelter>() : null;
            if (template == null)
            {
                return;
            }

            bool ownedForWrite = false;

            if (template.m_fuelItem != null && template.m_maxFuel > 0)
            {
                float fuel = zdo.GetFloat(ZDOVars.s_fuel);
                if (fuel < template.m_maxFuel - 0.01f && TryConsumeOne(zdo.GetPosition(), template.m_fuelItem.gameObject.name))
                {
                    if (!ownedForWrite)
                    {
                        zdo.SetOwner(ZNet.GetUID());
                        ownedForWrite = true;
                    }
                    zdo.Set(ZDOVars.s_fuel, Mathf.Min((float)template.m_maxFuel, fuel + 1f));
                }
            }

            if (template.m_maxOre > 0 && template.m_conversion.Count > 0)
            {
                int queued = zdo.GetInt(ZDOVars.s_queued);
                if (queued < template.m_maxOre)
                {
                    HashSet<string> kilnFilter = _kilns.Contains(zdo.GetPrefab()) ? KilnInputs() : null;
                    foreach (Smelter.ItemConversion conversion in template.m_conversion)
                    {
                        if (conversion.m_from == null)
                        {
                            continue;
                        }
                        string input = conversion.m_from.gameObject.name;
                        if (kilnFilter != null && kilnFilter.Count > 0 && !kilnFilter.Contains(input))
                        {
                            continue;
                        }
                        if (TryConsumeOne(zdo.GetPosition(), input))
                        {
                            if (!ownedForWrite)
                            {
                                zdo.SetOwner(ZNet.GetUID());
                                ownedForWrite = true;
                            }
                            zdo.Set("item" + queued, input);
                            zdo.Set(ZDOVars.s_queued, queued + 1);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds a linked container within range holding at least (reserve + 1) of the named prefab
        /// and removes exactly one, leaving the reserve floor untouched. Returns false (nothing
        /// consumed) if no source has enough spare stock.
        /// </summary>
        private static bool TryConsumeOne(Vector3 position, string fuelOrOrePrefabName)
        {
            float range = WonderlandConfig.ProductionSupplyRange?.Value ?? 15f;
            int reserve = Mathf.Max(0, WonderlandConfig.ProductionSupplyReserve?.Value ?? 1);

            foreach (ZDO containerZdo in ZdoSpatialQuery.FindNear(position, range))
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(containerZdo.GetPrefab());
                Container template = ContainerRegistry.ResolveTemplate(prefab);
                if (template == null || ZdoInventoryIO.IsBusy(containerZdo))
                {
                    continue;
                }

                (int width, int height) = ContainerRows.GetGridSize(prefab, template);
                Inventory inventory = ZdoInventoryIO.Load(containerZdo, width, height);
                if (inventory == null)
                {
                    continue;
                }

                ItemDrop.ItemData found = inventory.GetItem(fuelOrOrePrefabName, -1, isPrefabName: true);
                if (found == null)
                {
                    continue;
                }

                int totalOfThisItem = inventory.CountItems(found.m_shared.m_name);
                if (totalOfThisItem <= reserve)
                {
                    continue;
                }

                inventory.RemoveItem(found, 1);
                ZdoInventoryIO.Save(containerZdo, inventory);
                ItemLedger.RecordTransfer(Tag, found.m_shared.m_name, 1);
                return true;
            }

            return false;
        }
    }
}
