using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// The container overflow guard: rehomes anything a vanilla client would silently discard the
    /// moment it loads the chest. What a vanilla 1.0.7 client actually does on load (client decompile,
    /// Inventory.AddItem 67784 and 68817, reached from Container.Load with skipValidPositionCheck=true):
    ///  - an item whose COLUMN is at or beyond the prefab width is refused and dropped;
    ///  - an item whose ROW is beyond the prefab height is accepted, and Container.UpdateRows() grows
    ///    the chest to fit it - that is the whole basis of ContainerRows;
    ///  - a stack above the client's m_maxStackSize is clamped down to it.
    /// So "overflow" here means: x >= vanilla width, y >= the height Wonderland itself is targeting
    /// for this prefab (vanilla, or vanilla x ContainerRowMultiplier when eligible - so lowering the
    /// setting later pulls the abandoned rows back in), or stack > vanilla max. Anything found is
    /// rebuilt into a properly-sized inventory; what no longer fits goes to the nearest sibling
    /// container with room, and failing that to the ItemCache. Nothing is ever discarded.
    ///
    /// Enforcement rides the vacuum sweep's regular interval rather than a Harmony hook on
    /// RPC_RequestOpen - that RPC only runs on whichever machine has a live Container instance, which
    /// on a dedicated server is essentially never the server itself.
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

        /// <summary>
        /// Cheap pre-check so a well-formed chest isn't rebuilt every sweep.
        /// </summary>
        public static bool HasOverflow(Inventory inventory, GameObject prefab, Container template)
        {
            (int vw, _) = GetVanillaSize(prefab.name, template);
            (_, int targetHeight) = ContainerRows.GetGridSize(prefab, template);
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item.m_gridPos.x >= vw || item.m_gridPos.y >= targetHeight)
                {
                    return true;
                }
                if (item.m_stack > item.m_shared.m_maxStackSize)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Rebuilds <paramref name="inventory"/> into a fresh inventory of the size Wonderland targets
        /// for this prefab, each stack pre-capped to the vanilla max. Whatever doesn't fit is homed in
        /// the nearest sibling container within <paramref name="siblingSearchRadius"/> that has room
        /// (sized by its own prefab's rules); if nothing has room, the leftover goes to the ItemCache
        /// and is logged as a real warning. Returns true if anything changed (caller saves the ZDO).
        /// </summary>
        public static bool EnforceOverflow(ZDO containerZdo, Inventory inventory, GameObject prefab, Container template, float siblingSearchRadius, out bool siblingChanged, out ZDO siblingZdo)
        {
            siblingChanged = false;
            siblingZdo = null;
            if (!HasOverflow(inventory, prefab, template))
            {
                return false;
            }

            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            var scratch = new Inventory("wonderland_overflow_scratch", null, width, height);
            var overflowRemaining = new List<ItemDrop.ItemData>();

            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                int vanillaMax = item.m_shared.m_maxStackSize;
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

            ZDO sibling = FindNearestSiblingContainer(containerZdo, siblingSearchRadius, out GameObject siblingPrefab, out Container siblingTemplate);
            if (sibling != null)
            {
                (int sw, int sh) = ContainerRows.GetGridSize(siblingPrefab!, siblingTemplate!);
                Inventory siblingInv = ZdoInventoryIO.Load(sibling, sw, sh);
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

            if (overflowRemaining.Count > 0)
            {
                Vector3 pos = containerZdo.GetPosition();
                foreach (ItemDrop.ItemData item in overflowRemaining)
                {
                    ItemCache.Store(pos, item, $"GridGrowth:{prefab.name}");
                }
                WonderlandDebug.LogWarning(
                    $"[GridGrowth] {overflowRemaining.Count} stack(s) in container '{prefab.name}' at {pos:F1} exceeded its capacity and no sibling container had room. Moved to the Wonderland ItemCache so nothing is lost.");
            }

            return true;
        }

        private static ZDO FindNearestSiblingContainer(ZDO self, float radius, out GameObject? prefab, out Container? template)
        {
            prefab = null;
            template = null;
            List<ZDO> nearby = ZdoSpatialQuery.FindNear(self.GetPosition(), radius);
            foreach (ZDO candidate in nearby)
            {
                if (candidate.m_uid == self.m_uid)
                {
                    continue;
                }
                GameObject candidatePrefab = ZNetScene.instance.GetPrefab(candidate.GetPrefab());
                Container candidateTemplate = candidatePrefab != null ? candidatePrefab.GetComponent<Container>() : null;
                if (candidateTemplate == null)
                {
                    continue;
                }
                prefab = candidatePrefab;
                template = candidateTemplate;
                return candidate;
            }
            return null;
        }
    }
}
