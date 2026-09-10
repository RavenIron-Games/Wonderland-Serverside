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
    /// </summary>
    public static class ProductionSupplyEngine
    {
        private const string Tag = "ProductionSupply";

        private static ZdoSpatialQuery.PrefabSetScanner _fireplaceScanner;
        private static ZdoSpatialQuery.PrefabSetScanner _smelterScanner;
        private static float _timer;
        private static readonly List<ZDO> _buffer = new List<ZDO>();

        public static void Initialize()
        {
            var fireplaceNames = new List<string>();
            var smelterNames = new List<string>();
            if (ZNetScene.instance != null)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null)
                    {
                        continue;
                    }
                    if (prefab.GetComponent<Fireplace>() != null)
                    {
                        fireplaceNames.Add(prefab.name);
                    }
                    if (prefab.GetComponent<Smelter>() != null)
                    {
                        smelterNames.Add(prefab.name);
                    }
                }
            }
            _fireplaceScanner = new ZdoSpatialQuery.PrefabSetScanner(fireplaceNames);
            _smelterScanner = new ZdoSpatialQuery.PrefabSetScanner(smelterNames);
            WonderlandDebug.LogInfo($"[ProductionSupplyEngine] tracking {fireplaceNames.Count} fireplace-family and {smelterNames.Count} smelter-family prefab types.");
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

        private static void ProcessFireplace(ZDO zdo)
        {
            if (!zdo.IsValid())
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
            if (!zdo.IsValid())
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
                    foreach (Smelter.ItemConversion conversion in template.m_conversion)
                    {
                        if (conversion.m_from == null)
                        {
                            continue;
                        }
                        if (TryConsumeOne(zdo.GetPosition(), conversion.m_from.gameObject.name))
                        {
                            if (!ownedForWrite)
                            {
                                zdo.SetOwner(ZNet.GetUID());
                                ownedForWrite = true;
                            }
                            zdo.Set("item" + queued, conversion.m_from.gameObject.name);
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
                Container template = prefab != null ? prefab.GetComponent<Container>() : null;
                if (template == null || ZdoInventoryIO.IsBusy(containerZdo))
                {
                    continue;
                }

                (int width, int height) = GridGrowth.GetVanillaSize(prefab.name, template);
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
