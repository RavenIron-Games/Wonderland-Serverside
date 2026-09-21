using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;
using Wonderland.Subsystems.Storage;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// Background-only stack consolidation: merges partial stacks of the same item (name + quality)
    /// within a container into fewer, fuller stacks. Nothing more aggressive - no full grid reshuffle
    /// on demand, since there is no admin-command surface to trigger one, and reshuffling a chest a
    /// player is actively looking at is exactly the thing to avoid. Runs on a slow interval across the
    /// same container set VacuumEngine tracks.
    /// </summary>
    public static class SortEngine
    {
        private static BudgetedSweep? _sweep;
        private static float _timer;
        private static readonly Action<ZDO> VisitDelegate = ConsolidateStacks;

        public static void Initialize()
        {
            _sweep = new BudgetedSweep(ContainerRegistry.PrefabNames);
        }

        public static void OnUpdate(float dt)
        {
            if (_sweep == null || WonderlandConfig.SortEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer >= (WonderlandConfig.SortInterval?.Value ?? 30f))
            {
                _timer = 0f;
                _sweep.Grant(Mathf.Max(1, WonderlandConfig.SortBatchSize?.Value ?? 10));
            }
            // The interval's chunks are visited over the frames that follow, SweepBudgetMs at a time (0.10.9).
            _sweep.Run(WonderlandConfig.SweepBudgetMs?.Value ?? 2f, VisitDelegate);
        }

        private static void ConsolidateStacks(ZDO zdo)
        {
            if (!zdo.IsValid() || ZdoInventoryIO.IsBusy(zdo))
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Container template = ContainerRegistry.ResolveTemplate(prefab);
            if (template == null)
            {
                return;
            }

            if (prefab.GetComponent<Ship>() != null && ShipAttachment.IsSimulatedByClient(zdo))
            {
                return; // a merge is cosmetic - not worth a write that races the hull's owner (see ShipAttachment.IsSimulatedByClient)
            }

            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            Inventory inventory = ZdoInventoryIO.Load(zdo, width, height);
            if (inventory == null)
            {
                return;
            }

            bool changed = MergePartialStacks(inventory);
            if (changed && ContainerRows.IsEnabled && ContainerRows.IsEligible(prefab, template))
            {
                // A merge can empty the stack that was holding the grown rows open; re-park before saving.
                ContainerRows.EnsureAnchor(inventory, GridGrowth.GetVanillaSize(prefab.name, template).height, height, out _);
            }
            if (changed)
            {
                ZdoInventoryIO.Save(zdo, inventory);
            }
        }

        private static bool MergePartialStacks(Inventory inventory)
        {
            var items = new List<ItemDrop.ItemData>(inventory.GetAllItems());
            var emptied = new List<ItemDrop.ItemData>();
            bool changed = false;

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData a = items[i];
                if (emptied.Contains(a) || a.m_stack >= a.m_shared.m_maxStackSize)
                {
                    continue;
                }

                for (int j = i + 1; j < items.Count; j++)
                {
                    ItemDrop.ItemData b = items[j];
                    if (emptied.Contains(b) || b.m_shared.m_name != a.m_shared.m_name || b.m_quality != a.m_quality)
                    {
                        continue;
                    }

                    int room = a.m_shared.m_maxStackSize - a.m_stack;
                    if (room <= 0)
                    {
                        break;
                    }

                    int moved = Mathf.Min(room, b.m_stack);
                    if (moved <= 0)
                    {
                        continue;
                    }

                    a.m_stack += moved;
                    b.m_stack -= moved;
                    changed = true;
                    if (b.m_stack <= 0)
                    {
                        emptied.Add(b);
                    }
                }
            }

            foreach (ItemDrop.ItemData item in emptied)
            {
                inventory.RemoveItem(item);
            }
            return changed;
        }
    }
}
