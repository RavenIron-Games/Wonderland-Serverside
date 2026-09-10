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
    ///  - Auto-harvest: anchored to each connected player's character ZDO (never a global sweep - a
    ///    Pickable's picked/present ZDO state is only worth checking where someone might actually be
    ///    harvesting),
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
        private static readonly HashSet<ZDOID> _destroyedThisBatch = new HashSet<ZDOID>();
        private static readonly List<ZDO> _pendingGroundDestroy = new List<ZDO>();
        private static readonly Dictionary<ZDOID, bool> _lastPicked = new Dictionary<ZDOID, bool>();
        private static readonly Dictionary<ZDOID, long> _harvestedAt = new Dictionary<ZDOID, long>();
        private const int MaxTrackedPickables = 50000;

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
            _destroyedThisBatch.Clear();
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

            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            Inventory inventory = ZdoInventoryIO.Load(containerZdo, width, height);
            if (inventory == null)
            {
                return;
            }

            bool changed = false;

            // Overflow guard rides this same pass - every container Wonderland already has open gets
            // checked, on a short regular interval, well before any real client could load it.
            float linkRadius = WonderlandConfig.ContainerLinkRadius?.Value ?? 10f;
            if (GridGrowth.EnforceOverflow(containerZdo, inventory, prefab, template, linkRadius, out bool siblingChanged, out ZDO siblingZdo))
            {
                changed = true;
                if (siblingChanged && siblingZdo != null)
                {
                    // sibling's own inventory object was already saved inside EnforceOverflow
                }
            }

            // Try draining any previously overflowed items from ItemCache into this container
            changed |= ItemCache.TryDrainInto(containerZdo, inventory);

            if (inventory.NrOfItems() > 0)
            {
                changed |= VacuumGroundItemsInto(containerZdo, inventory, prefabHash);
            }

            if (changed && ContainerRows.IsEnabled && ContainerRows.IsEligible(prefab, template))
            {
                // Already committing this chest - make the same write leave its grown rows visible.
                ContainerRows.EnsureAnchor(inventory, GridGrowth.GetVanillaSize(prefabName, template).height, height, out _);
            }

            if (changed)
            {
                ZdoInventoryIO.Save(containerZdo, inventory);
            }

            FlushPendingGroundDestroy();
        }

        /// <summary>
        /// Destroys the ground items only after the container ZDO carrying them has been committed,
        /// so there is never a window where neither side holds the stack. ZDOMan.DestroyZDO silently
        /// does nothing unless the caller owns the ZDO, hence the ownership claim first.
        /// </summary>
        private static void FlushPendingGroundDestroy()
        {
            foreach (ZDO groundZdo in _pendingGroundDestroy)
            {
                if (!groundZdo.IsValid())
                {
                    continue;
                }
                groundZdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(groundZdo);
            }
            _pendingGroundDestroy.Clear();
        }

        /// <summary>Match-required: only tops up an item type the container already holds at least one of.</summary>
        private static bool VacuumGroundItemsInto(ZDO containerZdo, Inventory inventory, int containerPrefabHash)
        {
            float radius = WonderlandConfig.VacuumRadius?.Value ?? 10f;
            List<ZDO> nearby = ZdoSpatialQuery.FindNear(containerZdo.GetPosition(), radius);
            bool changed = false;

            foreach (ZDO groundZdo in nearby)
            {
                if (!groundZdo.IsValid() || groundZdo.GetPrefab() == containerPrefabHash || _destroyedThisBatch.Contains(groundZdo.m_uid))
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
                // every item of this type).
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

                int stackOnGround = groundItem.m_stack;
                if (stackOnGround <= 0)
                {
                    continue;
                }

                // All-or-nothing. The old partial path both wrote a reduced stack back to a ZDO whose
                // owning client rewrites it, and double-counted the move: Inventory.RemoveItem already
                // decrements m_stack in place, so subtracting the moved amount a second time deleted
                // the difference outright.
                if (!inventory.CanAddItem(groundItem, stackOnGround))
                {
                    ItemLedger.RecordRejection(VacuumTag, groundItem.m_shared.m_name, stackOnGround, "container full - left on the ground");
                    continue;
                }

                string itemName = groundItem.m_shared.m_name;
                int before = inventory.CountItems(itemName);
                ItemDrop.ItemData toStore = groundItem.Clone();
                toStore.m_stack = stackOnGround;
                bool added = inventory.AddItem(toStore);

                // Verify the whole stack actually landed before queueing the ground copy for
                // destruction: until the container ZDO is committed, that ground item is the only
                // copy of these items that exists. AddItem can top up pre-existing stacks and only
                // then fail to place the remainder, so trust the count, never the return value, and
                // roll back by quantity - removing the ItemData we passed in would leave those
                // top-ups behind and duplicate them.
                int landed = inventory.CountItems(itemName) - before;
                if (!added || landed != stackOnGround)
                {
                    if (landed > 0)
                    {
                        inventory.RemoveItem(itemName, landed);
                    }
                    ItemLedger.RecordRejection(VacuumTag, itemName, stackOnGround, "container could not take the whole stack - left on the ground");
                    continue;
                }

                changed = true;
                _destroyedThisBatch.Add(groundZdo.m_uid);
                _pendingGroundDestroy.Add(groundZdo);
                ItemLedger.RecordTransfer(VacuumTag, groundItem.m_shared.m_name, stackOnGround);
            }

            return changed;
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
            if (_lastPicked.Count > MaxTrackedPickables)
            {
                // Every pickable ever seen near a player over the server's life would otherwise stay here.
                // A reset only costs one missed trigger per pickable, and only right after the reset.
                _lastPicked.Clear();
            }

            if (_harvestedAt.Count > MaxTrackedPickables && ZNet.instance != null)
            {
                // Aged out rather than cleared outright: dropping a still-fresh entry would reopen the
                // very re-harvest window this ledger exists to close. A day of game time is far past
                // any vanilla respawn, so anything older is genuinely finished with.
                long cutoff = ZNet.instance.GetTime().AddDays(-1.0).Ticks;
                var stale = new List<ZDOID>();
                foreach (KeyValuePair<ZDOID, long> entry in _harvestedAt)
                {
                    if (entry.Value < cutoff)
                    {
                        stale.Add(entry.Key);
                    }
                }
                foreach (ZDOID id in stale)
                {
                    _harvestedAt.Remove(id);
                }
            }

            // Anchored to each connected player's character ZDO - never Player.GetAllPlayers(), which is
            // the local instance list and always empty on a dedicated server (see ConnectedCharacters).
            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                List<ZDO> nearby = ZdoSpatialQuery.FindNear(character.Position, radius);
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
            Pickable template = triggerPrefab.GetComponent<Pickable>();
            List<ZDO> nearby = ZdoSpatialQuery.FindNear(triggerZdo.GetPosition(), radius);
            foreach (ZDO zdo in nearby)
            {
                if (zdo.m_uid == triggerZdo.m_uid || zdo.GetPrefab() != triggerPrefabHash)
                {
                    continue;
                }
                if (zdo.GetBool(ZDOVars.s_picked) || WasHarvestedRecently(zdo.m_uid, template))
                {
                    continue;
                }
                HarvestPickable(zdo, triggerPrefab);
            }
        }

        /// <summary>
        /// s_picked alone is not a safe "already harvested" record, so this ledger is the one that
        /// actually stops a bush being swept twice. The server cannot make a pick stick: writing
        /// s_picked never reaches an already-loaded Pickable (applying a ZDO fires no callback, so the
        /// live component's m_picked - the only thing gating Interact and the visual - keeps its old
        /// value), the RPC only lands on peers that happen to have the bush instantiated at that
        /// instant, and vanilla's ReleaseNearbyZDOS hands ownership back to the nearby player within
        /// ~2s, after which their client can restore the flag. Without this ledger the sweep re-harvests
        /// the same bush every time the flag flips back, spawning items forever.
        /// </summary>
        private static bool WasHarvestedRecently(ZDOID uid, Pickable template)
        {
            if (!_harvestedAt.TryGetValue(uid, out long ticks))
            {
                return false;
            }
            float respawnMinutes = template != null ? template.m_respawnTimeMinutes : 0f;
            if (respawnMinutes <= 0f || ZNet.instance == null)
            {
                return true; // never respawns (or no clock to judge with) - never sweep it again
            }
            if ((ZNet.instance.GetTime() - new System.DateTime(ticks)).TotalMinutes >= respawnMinutes)
            {
                _harvestedAt.Remove(uid);
                return false;
            }
            return true;
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

            // A prefab's template ItemData only gets m_dropPrefab from ItemDrop.Awake, which never runs
            // for the prefab itself - and ItemDrop.DropItem instantiates from exactly that field.
            ItemDrop.ItemData itemData = dropTemplate.m_itemData.Clone();
            itemData.m_dropPrefab = pickableTemplate.m_itemPrefab;
            ItemDrop.DropItem(itemData, amount, zdo.GetPosition() + Vector3.up * 0.3f, Quaternion.identity);

            // Claim ownership on server and broadcast vanilla RPC_SetPicked so all connected clients
            // immediately update visual berries/geometry and set their local m_picked = true.
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZNetView.Everybody, zdo.m_uid, "RPC_SetPicked", true);

            if (pickableTemplate.m_respawnTimeMinutes > 0f || pickableTemplate.m_hideWhenPicked != null)
            {
                zdo.Set(ZDOVars.s_picked, true);
                if (ZNet.instance != null)
                {
                    zdo.Set(ZDOVars.s_pickedTime, ZNet.instance.GetTime().Ticks);
                }
            }
            else
            {
                ZDOMan.instance.DestroyZDO(zdo);
            }

            _lastPicked[zdo.m_uid] = true;
            if (ZNet.instance != null)
            {
                _harvestedAt[zdo.m_uid] = ZNet.instance.GetTime().Ticks;
            }

            ItemLedger.RecordTransfer(HarvestTag, itemData.m_shared.m_name, amount);
        }
    }
}
