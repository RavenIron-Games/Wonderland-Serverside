using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// Server-side chest row growth that a completely vanilla 1.0.7 client renders on its own.
    ///
    /// The mechanism is vanilla's, not ours. When a client loads a container from its ZDO,
    /// Container.Load() -> Inventory.Load() -> AddItem(prefabHash, itemData, skipValidPositionCheck: true)
    /// (client decompile 68700) -> AddItem(item, amount, x, y, skip) (67784), whose bounds check is
    /// `x >= m_width || (y >= m_height &amp;&amp; !skipValidPositionCheck)`: a column past the grid is
    /// still refused, but a ROW past the grid is accepted. Container.Load() then calls UpdateRows()
    /// (122671), which sets the inventory height to max(prefab height, lowest occupied row + 1), and
    /// the container grid in InventoryGui is a masked ScrollRect (UI dump: "ContainerGrid |
    /// InventoryGrid,RectMask2D,ScrollRect"), so the extra rows draw and scroll. Nothing about the
    /// prefab, the client, or any RPC changes - only the stored grid positions in the ZDO's item blob.
    /// Vanilla clients re-read that blob once a second (Container.Awake: InvokeRepeating
    /// "CheckForChanges", 1s) whenever the ZDO's data revision moved and nobody has the chest open.
    ///
    /// Consequences that shape this class:
    ///  - Height is the only axis that grows. Width is refused on load (an item at x >= width is
    ///    dropped by the client and lost when it saves), so this never touches x.
    ///  - Stack sizes are NOT part of this: the same load path clamps every stack to the client's own
    ///    m_maxStackSize (68817). A taller grid is how a chest holds more; bigger stacks are not
    ///    reachable from the server at all.
    ///  - The grown height only exists while some item occupies the last row: UpdateRows() recomputes
    ///    it on every reload. So growing a chest means keeping one stack parked in the target row -
    ///    the "anchor". Vanilla itself keeps it there most of the time: materials are placed
    ///    bottom-first (Inventory.TopFirst / FindEmptySlot(false)), so a player's own deposits land in
    ///    the grown rows. When the anchor row empties, the sweep parks another stack there.
    ///  - Anchoring is a pure position move: the same ItemData with the same stack, only m_gridPos
    ///    changes. It cannot add, remove or resize anything, so a write that loses a same-revision race
    ///    against a client (ZDOMan.RPC_ZDOData ignores a revision that isn't strictly newer) simply
    ///    never takes effect - nothing is duplicated or lost either way.
    ///  - Only player-buildable containers (listed in a build table such as the hammer's) are grown.
    ///    Tombstones, dungeon and treasure chests, cargo crates and every other world-spawned
    ///    container keep vanilla rows.
    /// </summary>
    public static class ContainerRows
    {
        /// <summary>
        /// Hard ceiling on total rows. Grid positions serialize as a byte (255) but the client
        /// instantiates width x height UI elements every time the grid changes size, so keep it sane.
        /// </summary>
        public const int MaxTotalRows = 32;
        public const float MaxMultiplier = 4f;

        /// <summary>
        /// Prefab names a player can actually place: the union of every build table (hammer, hoe,
        /// cultivator...) hung off an item in ObjectDB. "Has a Piece component" is not good enough in
        /// 1.0 - every TreasureChest_*, loot_chest_* and dungeon pot carries one too. Resolved lazily
        /// because ObjectDB is not guaranteed to be populated when the ZNetScene.Awake hook fires.
        /// </summary>
        private static HashSet<string>? _buildable;

        public static float Multiplier => Mathf.Clamp(WonderlandConfig.ContainerRowMultiplier?.Value ?? 1f, 1f, MaxMultiplier);

        public static bool IsEnabled => WonderlandConfig.ContainerRowsEnabled?.Value == true && Multiplier > 1f;

        public static void ResetCache() => _buildable = null;

        public static bool IsResolved => _buildable != null;

        public static bool TryResolveBuildable()
        {
            if (_buildable != null)
            {
                return true;
            }
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items == null || db.m_items.Count == 0)
            {
                return false;
            }
            var set = new HashSet<string>();
            foreach (GameObject item in db.m_items)
            {
                ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
                PieceTable table = drop != null ? drop.m_itemData?.m_shared?.m_buildPieces : null;
                if (table == null || table.m_pieces == null)
                {
                    continue;
                }
                foreach (GameObject piece in table.m_pieces)
                {
                    if (piece != null)
                    {
                        set.Add(piece.name);
                    }
                }
            }
            if (set.Count == 0)
            {
                return false;
            }
            _buildable = set;
            return true;
        }

        /// <summary>Player-buildable (listed in some build table) and not excluded by config.</summary>
        public static bool IsEligible(GameObject prefab, Container template)
        {
            if (prefab == null || template == null || !TryResolveBuildable())
            {
                return false;
            }
            return _buildable!.Contains(prefab.name) && !IsExcluded(prefab.name);
        }

        /// <summary>
        /// The grid every Wonderland engine should build its scratch Inventory with: vanilla width,
        /// and vanilla height times the configured multiplier when this prefab is eligible. Using the
        /// grown height everywhere is what lets the vacuum and production supply see and fill the
        /// extra rows instead of judging the chest full at its vanilla capacity.
        /// </summary>
        public static (int width, int height) GetGridSize(GameObject prefab, Container template)
        {
            (int vw, int vh) = GridGrowth.GetVanillaSize(prefab.name, template);
            if (!IsEnabled || !IsEligible(prefab, template))
            {
                return (vw, vh);
            }
            return (vw, Mathf.Clamp(Mathf.RoundToInt(vh * Multiplier), vh, MaxTotalRows));
        }

        /// <summary>
        /// Guarantees one stack sits in row <paramref name="targetHeight"/> - 1 so a vanilla client's
        /// UpdateRows() grows the chest to <paramref name="targetHeight"/> rows. Chooses the stack the
        /// way vanilla would have placed it: a bottom-first item (materials, food, anything not a
        /// weapon/tool/shield/utility/misc/trinket) from the lowest occupied row. Returns true if a
        /// position changed (caller saves). Returns false, touching nothing, if the chest is empty,
        /// already anchored, or holds an item beyond the target row (left to the overflow guard).
        /// </summary>
        public static bool EnsureAnchor(Inventory inventory, int vanillaHeight, int targetHeight, out ItemDrop.ItemData? moved)
        {
            moved = null;
            int anchorRow = targetHeight - 1;
            if (anchorRow < vanillaHeight)
            {
                return false;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            if (items.Count == 0)
            {
                return false;
            }

            ItemDrop.ItemData? best = null;
            foreach (ItemDrop.ItemData item in items)
            {
                if (item.m_gridPos.y >= anchorRow)
                {
                    return false;
                }
                if (best == null || Prefer(item, best))
                {
                    best = item;
                }
            }
            if (best == null)
            {
                return false;
            }

            // The anchor row is empty (checked above), so the stack's own column is free there.
            best.m_gridPos = new Vector2i(best.m_gridPos.x, anchorRow);
            moved = best;
            return true;
        }

        private static bool Prefer(ItemDrop.ItemData candidate, ItemDrop.ItemData current)
        {
            bool candidateBottomFirst = !TopFirst(candidate);
            bool currentBottomFirst = !TopFirst(current);
            if (candidateBottomFirst != currentBottomFirst)
            {
                return candidateBottomFirst;
            }
            return candidate.m_gridPos.y > current.m_gridPos.y;
        }

        /// <summary>
        /// Mirror of vanilla Inventory.TopFirst (client 67951): the item types vanilla places from the
        /// top row down. Everything else is placed bottom-first, which is what keeps a grown chest's
        /// anchor row occupied without our help most of the time.
        /// </summary>
        private static bool TopFirst(ItemDrop.ItemData item)
        {
            if (item.IsWeapon())
            {
                return true;
            }
            ItemDrop.ItemData.ItemType t = item.m_shared.m_itemType;
            return t == ItemDrop.ItemData.ItemType.Tool
                || t == ItemDrop.ItemData.ItemType.Shield
                || t == ItemDrop.ItemData.ItemType.Utility
                || t == ItemDrop.ItemData.ItemType.Misc
                || t == ItemDrop.ItemData.ItemType.Trinket;
        }

        private static bool IsExcluded(string prefabName)
        {
            string list = WonderlandConfig.ContainerRowsExcludedContainers?.Value ?? "";
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
    }
}
