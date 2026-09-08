using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// Real slot-count growth for containers, plus the overflow guard both GridGrowth and
    /// StackCapacity depend on. See the plan's "overflow guard" section: since every connecting
    /// client is plain vanilla (Wonderland never ships a client component), the danger isn't "an
    /// unmodded peer" - it's ANY peer, always, the moment their client loads a container whose stored
    /// grid position or stack size exceeds vanilla's raw Container/ItemData limits. Inventory.Load's
    /// AddItem(prefabHash, itemData) bounds-checks position against the LOCAL m_width/m_height and
    /// clamps stack against the LOCAL m_maxStackSize, silently discarding the excess - and that loss
    /// only becomes permanent once that peer's own Container.Save() fires.
    /// Enforcement rides the vacuum/production-supply/sort sweep's own regular interval rather than
    /// a Harmony hook on RPC_RequestOpen - that RPC only has anything to route to on whichever machine
    /// last had a *live* Container instance, which per this whole plan's foundational finding is
    /// essentially never the dedicated server, so a hook there could simply never fire when it matters.
    /// </summary>
    public static class GridGrowth
    {
        private static readonly Dictionary<string, (int w, int h)> VanillaSize = new Dictionary<string, (int w, int h)>();

        public static (int width, int height) GetVanillaSize(string prefabName, Container template)
        {
            if (!VanillaSize.TryGetValue(prefabName, out (int w, int h) size))
            {
                size = (template.m_width, template.m_height);
                VanillaSize[prefabName] = size;
            }
            return size;
        }

        /// <summary>The size Wonderland wants this container to behave as - vanilla size plus configured extra rows.</summary>
        public static (int width, int height) GetIntendedSize(string prefabName, Container template)
        {
            (int vw, int vh) = GetVanillaSize(prefabName, template);
            if (WonderlandConfig.GridGrowthEnabled?.Value != true || IsExcluded(prefabName))
            {
                return (vw, vh);
            }
            int extraRows = Mathf.Max(0, WonderlandConfig.GridGrowthExtraRows?.Value ?? 0);
            return (vw, vh + extraRows);
        }

        private static bool IsExcluded(string prefabName)
        {
            string list = WonderlandConfig.GridGrowthExcludedPrefabs?.Value ?? "";
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

        /// <summary>
        /// Checks whether anything currently in <paramref name="inventory"/> exceeds vanilla's raw
        /// bounds for this prefab. Cheap - callers should skip the full rebuild below unless this
        /// says there is actually something to fix, so an under-filled boosted container isn't
        /// pointlessly reshuffled every sweep.
        /// </summary>
        public static bool HasOverflow(Inventory inventory, string prefabName, Container template)
        {
            (int vw, int vh) = GetVanillaSize(prefabName, template);
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item.m_gridPos.x >= vw || item.m_gridPos.y >= vh)
                {
                    return true;
                }
                int vanillaMax = StackCapacity.GetOriginalMaxStack(item.m_dropPrefab.name, item.m_shared.m_maxStackSize);
                if (item.m_stack > vanillaMax)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Rebuilds <paramref name="inventory"/>'s contents into a fresh vanilla-sized inventory
        /// (each item re-added with its stack pre-capped to vanilla's remembered max, so the already-
        /// boosted live SharedData never gets a chance to let a single AddItem call skip the vanilla
        /// cap). Whatever doesn't fit is homed in the nearest sibling container within
        /// <paramref name="siblingSearchRadius"/> that has room; if nothing has room, the overflow is
        /// simply left in the original inventory rather than discarded, and logged as a real warning.
        /// Returns true if anything changed (caller should ZdoInventoryIO.Save the touched ZDOs).
        /// </summary>
        public static bool EnforceOverflow(ZDO containerZdo, Inventory inventory, string prefabName, Container template, float siblingSearchRadius, out bool siblingChanged, out ZDO siblingZdo)
        {
            siblingChanged = false;
            siblingZdo = null;
            (int vw, int vh) = GetVanillaSize(prefabName, template);
            if (!HasOverflow(inventory, prefabName, template))
            {
                return false;
            }

            var scratch = new Inventory("wonderland_overflow_scratch", null, vw, vh);
            var overflowRemaining = new List<ItemDrop.ItemData>();

            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                int vanillaMax = StackCapacity.GetOriginalMaxStack(item.m_dropPrefab.name, item.m_shared.m_maxStackSize);
                int remaining = item.m_stack;
                while (remaining > 0)
                {
                    int chunk = Mathf.Min(remaining, Mathf.Max(1, vanillaMax));
                    ItemDrop.ItemData clone = item.Clone();
                    clone.m_stack = chunk;
                    if (scratch.AddItem(clone))
                    {
                        remaining -= chunk;
                    }
                    else
                    {
                        ItemDrop.ItemData leftover = item.Clone();
                        leftover.m_stack = remaining;
                        overflowRemaining.Add(leftover);
                        remaining = 0;
                    }
                }
            }

            inventory.RemoveAll();
            foreach (ItemDrop.ItemData item in scratch.GetAllItems())
            {
                inventory.AddItem(item);
            }

            if (overflowRemaining.Count == 0)
            {
                return true;
            }

            ZDO sibling = FindNearestSiblingContainer(containerZdo, siblingSearchRadius);
            if (sibling != null)
            {
                Inventory siblingInv = ZdoInventoryIO.Load(sibling, template.m_width, template.m_height);
                if (siblingInv != null)
                {
                    var stillLeftover = new List<ItemDrop.ItemData>();
                    foreach (ItemDrop.ItemData item in overflowRemaining)
                    {
                        if (!siblingInv.AddItem(item.Clone()))
                        {
                            stillLeftover.Add(item);
                        }
                    }
                    if (stillLeftover.Count != overflowRemaining.Count)
                    {
                        siblingZdo = sibling;
                        siblingChanged = true;
                        ZdoInventoryIO.Save(sibling, siblingInv);
                    }
                    overflowRemaining = stillLeftover;
                }
            }

            foreach (ItemDrop.ItemData item in overflowRemaining)
            {
                inventory.AddItem(item);
                WonderlandDebug.LogWarning(
                    $"[GridGrowth] {item.m_stack}x {item.m_shared.m_name} in container '{prefabName}' at {containerZdo.GetPosition()} exceeds vanilla capacity and no sibling container had room. Left in place - place another container nearby to resolve.");
            }

            return true;
        }

        private static ZDO FindNearestSiblingContainer(ZDO self, float radius)
        {
            List<ZDO> nearby = ZdoSpatialQuery.FindNear(self.GetPosition(), radius);
            foreach (ZDO candidate in nearby)
            {
                if (candidate.m_uid == self.m_uid)
                {
                    continue;
                }
                if (ZNetScene.instance.GetPrefab(candidate.GetPrefab())?.GetComponent<Container>() == null)
                {
                    continue;
                }
                return candidate;
            }
            return null;
        }
    }
}
