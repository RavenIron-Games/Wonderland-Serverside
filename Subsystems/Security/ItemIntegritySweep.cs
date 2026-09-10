using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;
using Wonderland.Subsystems.ItemFlow;
using Wonderland.Subsystems.Storage;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Independent of the vacuum engine's point-of-entry check: a periodic pass over every tracked
    /// container's inventory (round-robin, same ContainerRegistry prefab set VacuumEngine/SortEngine
    /// use), checking every item against ItemSanityGuard.IsPlausible - catches a cheat that writes
    /// straight into a container ZDO and never goes anywhere near the vacuum engine. Configurable as
    /// detect-only or detect-and-correct (removing the offending item outright is the only "correction"
    /// that makes sense here - there's no legitimate lesser value to clamp a fabricated stack down to).
    ///
    /// Containers only. A connected player's own bag is never networked - Humanoid.m_inventory lives
    /// purely in the memory of the client that owns the character, and a dedicated server has no Player
    /// instance to read it from either (see ZdoInventoryIO's header and ConnectedCharacters). The first
    /// version of this file also iterated Player.GetAllPlayers(); on a dedicated server that list is
    /// always empty, so the "player inventory" half never did anything and was removed rather than kept
    /// as a promise the server cannot keep.
    /// </summary>
    public static class ItemIntegritySweep
    {
        private static ZdoSpatialQuery.PrefabSetScanner _scanner;
        private static float _timer;
        private static readonly List<ZDO> _buffer = new List<ZDO>();

        public static void Initialize()
        {
            _scanner = new ZdoSpatialQuery.PrefabSetScanner(ContainerRegistry.PrefabNames);
        }

        public static void OnUpdate(float dt)
        {
            if (_scanner == null || WonderlandConfig.ItemIntegritySweepEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.ItemIntegritySweepInterval?.Value ?? 30f))
            {
                return;
            }
            _timer = 0f;

            _buffer.Clear();
            int budget = Mathf.Max(1, WonderlandConfig.ItemIntegritySweepBatchSize?.Value ?? 25);
            for (int i = 0; i < budget; i++)
            {
                _scanner.Advance(_buffer);
            }

            foreach (ZDO zdo in _buffer)
            {
                CheckContainer(zdo);
            }
        }

        private static void CheckContainer(ZDO zdo)
        {
            if (!zdo.IsValid() || ZdoInventoryIO.IsBusy(zdo))
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Container template = prefab != null ? prefab.GetComponent<Container>() : null;
            if (template == null)
            {
                return;
            }

            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            Inventory inventory = ZdoInventoryIO.Load(zdo, width, height);
            if (inventory == null)
            {
                return;
            }

            if (CheckInventory(inventory, $"container '{prefab.name}' at {zdo.GetPosition()}"))
            {
                ZdoInventoryIO.Save(zdo, inventory);
            }
        }

        /// <summary>Returns true if anything was removed (caller should persist the change).</summary>
        private static bool CheckInventory(Inventory inventory, string where)
        {
            if (inventory == null)
            {
                return false;
            }

            bool correct = WonderlandConfig.ItemIntegritySweepCorrect?.Value == true;
            var toRemove = new List<ItemDrop.ItemData>();

            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                if (ItemSanityGuard.IsPlausible(item, out string reason))
                {
                    continue;
                }

                AuditLog.Flag("ItemIntegritySweep", where, $"implausible item found: {reason}{(correct ? " - removed." : " - left in place (detect-only).")}");
                if (correct)
                {
                    toRemove.Add(item);
                }
            }

            if (toRemove.Count == 0)
            {
                return false;
            }
            foreach (ItemDrop.ItemData item in toRemove)
            {
                inventory.RemoveItem(item);
            }
            return true;
        }
    }
}
