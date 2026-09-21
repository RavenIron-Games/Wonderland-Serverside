using System;
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
        private static BudgetedSweep? _sweep;
        private static float _timer;
        private static readonly Action<ZDO> CheckDelegate = CheckContainer;

        public static void Initialize()
        {
            _sweep = new BudgetedSweep(ContainerRegistry.PrefabNames);
        }

        public static void OnUpdate(float dt)
        {
            if (_sweep == null || WonderlandConfig.ItemIntegritySweepEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer >= (WonderlandConfig.ItemIntegritySweepInterval?.Value ?? 30f))
            {
                _timer = 0f;
                _sweep.Grant(Mathf.Max(1, WonderlandConfig.ItemIntegritySweepBatchSize?.Value ?? 25));
            }
            // Same coverage per interval as before, visited over the frames that follow within SweepBudgetMs
            // each (0.10.11; the one-frame pass was 89 ms on the live world - the last sweep 0.10.9 missed).
            _sweep.Run(WonderlandConfig.SweepBudgetMs?.Value ?? 2f, CheckDelegate);
        }

        private static void CheckContainer(ZDO zdo)
        {
            if (!zdo.IsValid() || ZdoInventoryIO.IsBusy(zdo))
            {
                return;
            }

            // Admin bypass: containers created by authenticated server admins are exempt from integrity sweep
            long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
            if (AdminRegistry.IsAdminPlayerId(creator))
            {
                return;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Container template = ContainerRegistry.ResolveTemplate(prefab);
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

            string where = $"container '{prefab.name}'";
            bool correct = WonderlandConfig.ItemIntegritySweepCorrect?.Value == true;
            if (correct && prefab.GetComponent<Ship>() != null && ShipAttachment.IsSimulatedByClient(zdo))
            {
                // A hull a client is simulating discards a plain server write, and its owner's next packet
                // would put a removed item straight back. Judged quietly on the server's copy; if anything
                // must go, the hull is borrowed the way the rows anchor is (HullBorrow, 0.10.11) and this
                // same check runs again - audit line, removal and write - on the copy that arrives with the
                // hand-over, as the hull's owner. Detect-only mode needs no write and takes the plain path.
                if (CheckInventory(inventory, where, zdo.GetPosition(), quiet: true) && HullBorrow.Request(zdo, CheckDelegate))
                {
                    WonderlandDebug.LogAlways($"[ItemIntegritySweep] '{prefab.name}' at {zdo.GetPosition():F0} needs correcting - asked its owner {zdo.GetOwner()} for the hull.");
                }
                return;
            }

            if (CheckInventory(inventory, where, zdo.GetPosition(), quiet: false))
            {
                ZdoInventoryIO.Save(zdo, inventory);
            }
        }

        /// <summary>Returns true if anything was removed (caller should persist the change). <paramref name="quiet"/>
        /// judges without writing the audit line - for a look at a copy that will not be the one written.</summary>
        private static bool CheckInventory(Inventory inventory, string where, Vector3 location, bool quiet)
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

                if (!quiet)
                {
                    AuditLog.Flag("ItemIntegritySweep", where, $"implausible item found: {reason}{(correct ? " - removed." : " - left in place (detect-only).")}", 0L, null, location);
                }
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
