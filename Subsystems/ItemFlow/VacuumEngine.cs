using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;
using Wonderland.Subsystems.Storage;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// Two triggers sharing one "move items toward a container" engine:
    ///  - Drop-to-chest: every tracked container gets a periodic radius sweep for matching ground
    ///    items (match-required - a container only tops up an item type it already holds).
    ///  - Auto-harvest: anchored to each connected player (never a global sweep - a Pickable's
    ///    picked/present ZDO state is only worth checking where someone might actually be harvesting),
    ///    watches for a ripe Pickable near that player flipping to picked, and on that transition,
    ///    sweeps the surrounding radius for other ready Pickables of the same type. Those get
    ///    "harvested" purely at the ZDO layer (see HarvestPickable below) rather than through the real
    ///    RPC_Pick, which needs a live Pickable instance that does not exist for one nobody has
    ///    touched - see the plan's Context section for why.
    /// </summary>
    public static class VacuumEngine
    {
        private const string VacuumTag = "Vacuum";
        private const string HarvestTag = "AutoHarvest";

        private static ZdoSpatialQuery.PrefabSetScanner _containerScanner;
        private static float _vacuumTimer;
        private static readonly List<ZDO> _scanBuffer = new List<ZDO>();
        private static readonly Dictionary<ZDOID, bool> _lastPicked = new Dictionary<ZDOID, bool>();

        public static void Initialize()
        {
            _containerScanner = new ZdoSpatialQuery.PrefabSetScanner(ContainerRegistry.PrefabNames);
        }

        public static void OnUpdate(float dt)
        {
            if (_containerScanner == null)
            {
                return;
            }

            if (WonderlandConfig.VacuumEnabled?.Value == true)
            {
                _vacuumTimer += dt;
                if (_vacuumTimer >= (WonderlandConfig.VacuumInterval?.Value ?? 2f))
                {
                    _vacuumTimer = 0f;
                    ProcessVacuumBatch();
                }
            }

            if (WonderlandConfig.AutoHarvestEnabled?.Value == true)
            {
                ProcessHarvestTriggers();
            }
        }

        private static void ProcessVacuumBatch()
        {
            _scanBuffer.Clear();
            int budget = Mathf.Max(1, WonderlandConfig.VacuumBatchSize?.Value ?? 25);
            for (int i = 0; i < budget; i++)
            {
                _containerScanner.Advance(_scanBuffer);
            }

            foreach (ZDO containerZdo in _scanBuffer)
            {
                ProcessContainer(containerZdo);
            }
        }

        private static void ProcessContainer(ZDO containerZdo)
        {
            if (!containerZdo.IsValid() || ZdoInventoryIO.IsBusy(containerZdo))
            {
                return;
            }

            int prefabHash = containerZdo.GetPrefab();
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
            Container template = prefab != null ? prefab.GetComponent<Container>() : null;
            if (template == null)
            {
                return;
            }
            string prefabName = prefab.name;
            if (IsContainerExcluded(prefabName))
            {
                return;
            }

            (int width, int height) = GridGrowth.GetIntendedSize(prefabName, template);
            Inventory inventory = ZdoInventoryIO.Load(containerZdo, width, height);
            if (inventory == null)
            {
                return;
            }

            bool changed = false;

            // Overflow guard rides this same pass - every container Wonderland already has open gets
            // checked, on a short regular interval, well before any real client could load it.
            float linkRadius = WonderlandConfig.ContainerLinkRadius?.Value ?? 10f;
            if (GridGrowth.EnforceOverflow(containerZdo, inventory, prefabName, template, linkRadius, out bool siblingChanged, out ZDO siblingZdo))
            {
                changed = true;
                if (siblingChanged && siblingZdo != null)
                {
                    // sibling's own inventory object was already saved inside EnforceOverflow
                }
            }

            if (inventory.NrOfItems() > 0)
            {
                changed |= VacuumGroundItemsInto(containerZdo, inventory, prefabHash);
            }

            if (changed)
            {
                ZdoInventoryIO.Save(containerZdo, inventory);
            }
        }

        /// <summary>Match-required: only tops up an item type the container already holds at least one of.</summary>
        private static bool VacuumGroundItemsInto(ZDO containerZdo, Inventory inventory, int containerPrefabHash)
        {
            float radius = WonderlandConfig.VacuumRadius?.Value ?? 10f;
            List<ZDO> nearby = ZdoSpatialQuery.FindNear(containerZdo.GetPosition(), radius);
            bool changed = false;

            foreach (ZDO groundZdo in nearby)
            {
                if (!groundZdo.IsValid() || groundZdo.GetPrefab() == containerPrefabHash)
                {
                    continue;
                }
                GameObject groundPrefab = ZNetScene.instance.GetPrefab(groundZdo.GetPrefab());
                if (groundPrefab == null || groundPrefab.GetComponent<ItemDrop>() == null)
                {
                    continue;
                }
                if (IsItemExcluded(groundPrefab.name))
                {
                    continue;
                }

                // ItemData.Load never touches m_shared/m_dropPrefab - both are reference types that
                // cannot round-trip through the byte blob, so the prefab's own template ItemDrop is
                // the only place to get them from (m_shared is deliberately one instance shared by
                // every item of this type, which is exactly what StackCapacity mutates in place).
                ItemDrop dropTemplate = groundPrefab.GetComponent<ItemDrop>();
                if (dropTemplate == null || dropTemplate.m_itemData?.m_shared == null)
                {
                    continue;
                }
                var groundItem = new ItemDrop.ItemData();
                ItemDrop.LoadFromZDO(groundItem, groundZdo);
                groundItem.m_dropPrefab = groundPrefab;
                groundItem.m_shared = dropTemplate.m_itemData.m_shared;

                if (!ItemSanityGuard.IsPlausible(groundItem, out string rejectReason))
                {
                    ItemLedger.RecordRejection(VacuumTag, groundItem.m_shared.m_name, groundItem.m_stack, rejectReason);
                    continue;
                }

                ItemDrop.ItemData existing = inventory.GetItem(groundItem.m_shared.m_name, -1, isPrefabName: false);
                if (existing == null)
                {
                    continue; // match-required
                }

                int moved = ZdoInventoryIO.MoveMatchingItem(TempGroundInventory(groundItem), inventory, groundItem, groundItem.m_stack, VacuumTag);
                if (moved <= 0)
                {
                    continue;
                }

                changed = true;
                if (moved >= groundItem.m_stack)
                {
                    ZDOMan.instance.DestroyZDO(groundZdo);
                }
                else
                {
                    groundItem.m_stack -= moved;
                    ItemDrop.SaveToZDO(groundItem, groundZdo);
                }
            }

            return changed;
        }

        /// <summary>
        /// ZdoInventoryIO.MoveMatchingItem wants a source Inventory to remove from, but a ground item
        /// lives on its own ZDO, not inside one. A single-item scratch inventory gives the same
        /// RemoveItem contract without teaching the shared helper a second source shape.
        /// </summary>
        private static Inventory TempGroundInventory(ItemDrop.ItemData item)
        {
            var scratch = new Inventory("wonderland_ground_scratch", null, 1, 1);
            scratch.AddItem(item);
            return scratch;
        }

        private static bool IsContainerExcluded(string prefabName) => MatchesList(WonderlandConfig.VacuumExcludedContainers?.Value, prefabName);
        private static bool IsItemExcluded(string prefabName) => MatchesList(WonderlandConfig.VacuumExcludedItems?.Value, prefabName);

        private static bool MatchesList(string list, string prefabName)
        {
            if (string.IsNullOrWhiteSpace(list))
            {
                return false;
            }
            foreach (string entry in list.Split(','))
            {
                if (string.Equals(entry.Trim(), prefabName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // === Auto-harvest ===

        private static void ProcessHarvestTriggers()
        {
            float radius = WonderlandConfig.AutoHarvestRadius?.Value ?? 8f;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null)
                {
                    continue;
                }
                List<ZDO> nearby = ZdoSpatialQuery.FindNear(player.transform.position, radius);
                foreach (ZDO zdo in nearby)
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                    if (prefab == null || prefab.GetComponent<Pickable>() == null)
                    {
                        continue;
                    }

                    bool picked = zdo.GetBool(ZDOVars.s_picked);
                    bool wasTracked = _lastPicked.TryGetValue(zdo.m_uid, out bool wasPicked);
                    _lastPicked[zdo.m_uid] = picked;

                    if (picked && wasTracked && !wasPicked)
                    {
                        SweepBonusHarvest(zdo, prefab, radius);
                    }
                }
            }
        }

        private static void SweepBonusHarvest(ZDO triggerZdo, GameObject triggerPrefab, float radius)
        {
            int triggerPrefabHash = triggerZdo.GetPrefab();
            List<ZDO> nearby = ZdoSpatialQuery.FindNear(triggerZdo.GetPosition(), radius);
            foreach (ZDO zdo in nearby)
            {
                if (zdo.m_uid == triggerZdo.m_uid || zdo.GetPrefab() != triggerPrefabHash)
                {
                    continue;
                }
                if (zdo.GetBool(ZDOVars.s_picked))
                {
                    continue;
                }
                HarvestPickable(zdo, triggerPrefab);
            }
        }

        /// <summary>
        /// Mirrors Pickable.Drop's essentials (scaled item, ItemDrop.OnCreateNew) without the parts
        /// that only matter with a live GameObject watching (spawn effects, aggro range, extra drop
        /// tables) - a documented scope trim, not a bug. The triggering pick itself already ran for
        /// real on the harvesting player's own client with full vanilla behavior; this only covers
        /// the "swept in" bonus.
        /// </summary>
        private static void HarvestPickable(ZDO zdo, GameObject prefab)
        {
            Pickable pickableTemplate = prefab.GetComponent<Pickable>();
            if (pickableTemplate == null || pickableTemplate.m_itemPrefab == null)
            {
                return;
            }
            if (IsItemExcluded(prefab.name))
            {
                return;
            }

            ItemDrop dropTemplate = pickableTemplate.m_itemPrefab.GetComponent<ItemDrop>();
            if (dropTemplate == null || dropTemplate.m_itemData?.m_shared == null)
            {
                return;
            }

            int amount = pickableTemplate.m_dontScale
                ? pickableTemplate.m_amount
                : Mathf.Max(pickableTemplate.m_minAmountScaled, Game.instance.ScaleDrops(pickableTemplate.m_itemPrefab, pickableTemplate.m_amount));
            if (amount <= 0)
            {
                return;
            }

            ItemDrop.ItemData itemData = dropTemplate.m_itemData.Clone();
            ItemDrop.DropItem(itemData, amount, zdo.GetPosition() + Vector3.up * 0.3f, Quaternion.identity);

            zdo.Set(ZDOVars.s_picked, true);
            if (ZNet.instance != null)
            {
                zdo.Set(ZDOVars.s_pickedTime, ZNet.instance.GetTime().Ticks);
            }
            _lastPicked[zdo.m_uid] = true;

            ItemLedger.RecordTransfer(HarvestTag, itemData.m_shared.m_name, amount);
        }
    }
}
