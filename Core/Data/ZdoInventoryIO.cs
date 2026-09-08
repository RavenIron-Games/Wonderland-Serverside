namespace Wonderland.Core.Data
{
    /// <summary>
    /// The only place any Wonderland code touches a Container-family ZDO's item blob
    /// (torches/smelters/fireplaces store fuel and ore as plain float/int/string fields instead -
    /// see ProductionSupplyEngine). Never touches a live Container/GameObject: on a dedicated
    /// server almost nothing near an actual base has one (see ZoneAnchor.md), so every read/write
    /// here operates purely on the ZDO's stored byte blob via a scratch Inventory instance, the
    /// same way Container.Load()/Save() do internally.
    ///
    /// Player character inventories are NOT stored this way - ZDOVars.s_items is written only by
    /// Container-family objects (confirmed against the decompile; a character's Humanoid.m_inventory
    /// lives in memory on whichever client owns that character and is never mirrored into its ZDO).
    /// Granting a connected player something goes through their live Player instance
    /// (Player.GetAllPlayers()) or, for someone not yet connected, a ground-spawned ItemDrop/
    /// Container ZDO at their spawn point - never through this class.
    /// </summary>
    public static class ZdoInventoryIO
    {
        /// <summary>True while some player has the container's interaction UI open - skip it this tick.</summary>
        public static bool IsBusy(ZDO zdo)
        {
            return zdo.GetInt(ZDOVars.s_inUse) == 1;
        }

        /// <summary>
        /// Reads the fresh byte blob right now (nothing cached across ticks - there is nothing to go
        /// stale). Returns null if the ZDO is gone or currently in use; callers skip the tick rather
        /// than retry.
        /// </summary>
        public static Inventory? Load(ZDO zdo, int width, int height)
        {
            if (zdo == null || !zdo.IsValid() || IsBusy(zdo))
            {
                return null;
            }

            var inventory = new Inventory("wonderland_scratch", null, width, height);
            byte[] bytes = zdo.GetByteArray(ZDOVars.s_items);
            if (bytes != null && bytes.Length > 0)
            {
                inventory.Load(new ZPackage(bytes));
            }
            return inventory;
        }

        /// <summary>
        /// Claims ownership only at the moment of commit (never pinned - this never fights
        /// Container.RPC_RequestOpen's own handoff to whichever player opens it next) and writes
        /// the inventory back to the ZDO's byte blob.
        /// </summary>
        public static void Save(ZDO zdo, Inventory inventory)
        {
            zdo.SetOwner(ZNet.GetUID());
            var pkg = new ZPackage();
            inventory.Save(pkg);
            zdo.Set(ZDOVars.s_items, pkg.GetArray());
        }

        /// <summary>
        /// Moves up to <paramref name="amount"/> of a matching item from <paramref name="from"/> to
        /// <paramref name="to"/>, honoring the destination's real capacity (grid space and per-stack
        /// max). Commits the destination add before the source removal (add-then-remove, never the
        /// reverse - the AwayFromHome OnAddOre-order lesson: a crash between the two steps must fail
        /// toward "nothing moved yet", never "vanished from the source with nowhere landed").
        /// Returns how many actually moved (0 if the destination had no room at all).
        /// </summary>
        public static int MoveMatchingItem(Inventory from, Inventory to, ItemDrop.ItemData sourceStack, int amount, string subsystem)
        {
            if (amount <= 0)
            {
                return 0;
            }

            string itemName = sourceStack.m_shared.m_name;
            int toMove = System.Math.Min(amount, sourceStack.m_stack);
            if (toMove <= 0)
            {
                return 0;
            }
            int room = to.CanAddItem(sourceStack, toMove) ? toMove : CountRoomFor(to, sourceStack, toMove);
            if (room <= 0)
            {
                ItemLedger.RecordRejection(subsystem, itemName, toMove, "destination has no room");
                return 0;
            }

            ItemDrop.ItemData clone = sourceStack.Clone();
            clone.m_stack = room;
            if (!to.AddItem(clone))
            {
                ItemLedger.RecordRejection(subsystem, itemName, room, "AddItem refused despite CanAddItem check");
                return 0;
            }

            from.RemoveItem(sourceStack, room);
            ItemLedger.RecordTransfer(subsystem, itemName, room);
            return room;
        }

        /// <summary>
        /// CanAddItem is all-or-nothing (it fails once the full amount will not fit), so a partial
        /// top-up needs its own room-remaining probe. CanAddItem's own formula (free space in existing
        /// stacks + empty slots * max stack size >= stack) is monotonic in the requested amount, so a
        /// binary search finds the largest amount that still fits in O(log amount) probes instead of
        /// walking down one item at a time.
        /// </summary>
        private static int CountRoomFor(Inventory to, ItemDrop.ItemData item, int amount)
        {
            int lo = 0;
            int hi = amount - 1;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (to.CanAddItem(item, mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return to.CanAddItem(item, lo) ? lo : 0;
        }
    }
}
