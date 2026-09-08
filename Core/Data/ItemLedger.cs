using System.Collections.Generic;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// Every transfer any subsystem makes through ZdoInventoryIO.MoveMatchingItem is recorded here -
    /// "never lose an item" as a concrete, checkable trail rather than a slogan. A single
    /// MoveMatchingItem call is already atomic from the ledger's point of view (the destination add
    /// and source removal both happen before it returns), so what this actually guards against is a
    /// crash or exception between two SEPARATE ZDO writes that were meant to be one logical transfer -
    /// callers that do their own multi-step moves (e.g. splitting a stack across two destinations)
    /// should record each leg so an admin can spot an orphaned debit in the log.
    /// Bounded ring buffer, not a database: this is a diagnostic trail for grepping logs and for the
    /// item-conservation soak test in the plan's Verification section, not a live world-wide
    /// item-count auditor (that would need a full-map inventory snapshot every sweep, which is far
    /// more expensive than anything else in this mod for a guarantee this trail already gives an
    /// admin the tools to check by hand).
    /// </summary>
    public static class ItemLedger
    {
        private const int MaxEntries = 2000;

        public readonly struct Entry
        {
            public readonly string Subsystem;
            public readonly string ItemName;
            public readonly int Amount;
            public readonly bool Credited;

            public Entry(string subsystem, string itemName, int amount, bool credited)
            {
                Subsystem = subsystem;
                ItemName = itemName;
                Amount = amount;
                Credited = credited;
            }
        }

        private static readonly LinkedList<Entry> Entries = new LinkedList<Entry>();
        private static long _totalMoved;
        private static long _totalRejected;

        public static void RecordTransfer(string subsystem, string itemName, int amount)
        {
            _totalMoved += amount;
            Append(new Entry(subsystem, itemName, amount, credited: true));
            WonderlandDebug.LogInfo($"[ItemLedger] {subsystem} moved {amount}x {itemName}");
        }

        /// <summary>A transfer that was attempted but refused - full stack didn't fit, guard rejected it, etc.</summary>
        public static void RecordRejection(string subsystem, string itemName, int amount, string reason)
        {
            _totalRejected += amount;
            Append(new Entry(subsystem, itemName, amount, credited: false));
            WonderlandDebug.LogInfo($"[ItemLedger] {subsystem} rejected {amount}x {itemName}: {reason}");
        }

        private static void Append(Entry entry)
        {
            Entries.AddLast(entry);
            if (Entries.Count > MaxEntries)
            {
                Entries.RemoveFirst();
            }
        }

        public static (long moved, long rejected, int entriesHeld) GetSummary()
        {
            return (_totalMoved, _totalRejected, Entries.Count);
        }
    }
}
