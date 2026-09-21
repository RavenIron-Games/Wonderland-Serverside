using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// The sweep that keeps every eligible chest's anchor row occupied (see ContainerRows for why a
    /// parked stack is what makes a vanilla client draw the extra rows). Round-robins the same
    /// container ZDO set the other engines use. A chest is written only when it has items, nobody has
    /// it open (ZDOVars.s_inUse, set by the opening client before any content edit) and its anchor row
    /// is empty - so after the first pass a chest is normally never written again until a player
    /// empties its bottom row. The write never touches ownership (since 0.8.2 - see ZdoInventoryIO.Save
    /// for the hiccup that taking it caused); ZDO.Set replicates to every peer in range on its own.
    /// </summary>
    public static class ContainerRowsEngine
    {
        /// <summary>
        /// How long a resolved eligible set is trusted before being rebuilt from scratch. Not
        /// event-driven off ContainerRowsExcludedContainers.SettingChanged (nothing else in this mod
        /// reacts to config edits that way - every engine here is poll-based by design, see
        /// GridGrowth's header) - a periodic rebuild is cheap (one pass over the container-prefab list,
        /// no ZDO I/O) and means an admin's exclusion-list edit takes effect within a minute instead of
        /// needing a restart.
        /// </summary>
        private const float ReannounceInterval = 60f;

        private static BudgetedSweep? _sweep;
        private static readonly Action<ZDO> VisitDelegate = Visit;
        private static readonly Action<ZDO> WriteBorrowedHullDelegate = WriteBorrowedHull;
        private static List<string> _eligible = new List<string>();
        private static float _timer;
        private static int _anchoredTotal;

        private static bool _announced;
        private static float _unresolvedFor;
        private static float _reannounceTimer;

        public static void Initialize()
        {
            ContainerRows.ResetCache();
            _sweep = null;
            _anchoredTotal = 0;
            _announced = false;
            _unresolvedFor = 0f;
            _reannounceTimer = 0f;

            if (!ContainerRows.IsEnabled)
            {
                WonderlandDebug.LogAlways("[ContainerRows] disabled (ContainerRowsEnabled=false or ContainerRowMultiplier=1) - containers keep vanilla rows.");
            }
        }

        /// <summary>
        /// Builds (and periodically rebuilds) the scanner from just the eligible - player-buildable,
        /// not excluded - container prefab names, rather than every container-bearing prefab in the
        /// game (dungeon pots, tar pits, cargo crates, every TreasureChest_* variant: 30-50+ types on a
        /// stock install, none of which can ever grow). PrefabSetScanner round-robins by PREFAB TYPE,
        /// not by ZDO, so scanning that whole list was spending most of the sweep budget on container
        /// types that always fail IsEligible in Visit() anyway - which is what made a specific player
        /// chest's turn arrive so unpredictably (the exact "expands at random" complaint this exists to
        /// fix). Returns false until the build tables can be read, so no sweep runs against an
        /// unresolved eligibility set.
        /// </summary>
        private static bool Announce(float dt)
        {
            if (_announced)
            {
                // Clamped: a live boot showed Unity's deltaTime can spike well past 60s on the first
                // tick after ZNetScene's synchronous world-load stall, which otherwise fires a spurious
                // reannounce (and its log line) within seconds of the real one.
                _reannounceTimer += Mathf.Min(dt, 5f);
                if (_reannounceTimer < ReannounceInterval)
                {
                    return true;
                }
                _reannounceTimer = 0f;
            }
            if (!ContainerRows.TryResolveBuildable())
            {
                _unresolvedFor += dt;
                if (_unresolvedFor > 60f)
                {
                    WonderlandDebug.LogWarning("[ContainerRows] ObjectDB has no build tables after 60s - cannot tell player-built containers from world loot, so no container is being grown.");
                    _unresolvedFor = float.NegativeInfinity;
                }
                return false;
            }

            bool firstAnnounce = !_announced;
            var eligibleNames = new List<string>();
            var logEntries = new List<string>();
            foreach (string name in ContainerRegistry.PrefabNames)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                Container template = ContainerRegistry.ResolveTemplate(prefab);
                if (template != null && ContainerRows.IsEligible(prefab, template))
                {
                    eligibleNames.Add(name);
                    if (firstAnnounce)
                    {
                        (int w, int h) = ContainerRows.GetGridSize(prefab, template);
                        logEntries.Add($"{name} {template.m_width}x{template.m_height}->{w}x{h}");
                    }
                }
            }
            // Only a changed set gets a new scanner: a fresh PrefabSetScanner starts at prefab 0 / sector 0,
            // so rebuilding it every minute regardless (as up to 0.8.1) restarted the round-robin each time -
            // the head of the list was swept every minute and the tail could go unvisited - and logged a
            // "may have changed" line 60 times an hour when nothing had.
            bool changed = firstAnnounce || !SameSet(_eligible, eligibleNames);
            if (changed)
            {
                _eligible = eligibleNames;
                _sweep = new BudgetedSweep(eligibleNames);
            }

            string summary = $"[ContainerRows] x{ContainerRows.Multiplier:0.##} rows on {eligibleNames.Count} of {ContainerRegistry.PrefabNames.Count} container type(s) (player-buildable only, includes ship cargo): {string.Join(", ", logEntries)}";
            if (firstAnnounce)
            {
                WonderlandDebug.LogAlways(summary);
            }
            else if (changed)
            {
                WonderlandDebug.LogInfo($"[ContainerRows] eligible set changed ({_eligible.Count} type(s)) - ContainerRowsExcludedContainers or the multiplier was edited; sweep restarted.");
            }
            _announced = true;
            return true;
        }

        private static bool SameSet(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], System.StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        public static void OnUpdate(float dt)
        {
            HullBorrow.OnUpdate(); // first, every frame, whatever else is on: a borrowed hull is written and handed back the frame it arrives

            if (!ContainerRows.IsEnabled || !Announce(dt) || _sweep == null)
            {
                return;
            }

            _timer += dt;
            if (_timer >= (WonderlandConfig.ContainerRowsInterval?.Value ?? 5f))
            {
                _timer = 0f;
                _sweep.Grant(Mathf.Max(1, WonderlandConfig.ContainerRowsBatchSize?.Value ?? 25));
            }
            // The interval's chunks are visited over the frames that follow, SweepBudgetMs at a time (0.10.9;
            // one frame used to carry the whole batch - 120-140 ms on the live world).
            _sweep.Run(WonderlandConfig.SweepBudgetMs?.Value ?? 2f, VisitDelegate);
        }

        private static void Visit(ZDO zdo)
        {
            if (!zdo.IsValid() || ZdoInventoryIO.IsBusy(zdo))
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Container template = ContainerRegistry.ResolveTemplate(prefab);
            if (template == null || !ContainerRows.IsEligible(prefab, template))
            {
                return;
            }

            // A ship's cargo is on the ship's own ZDO, and a floating hull's ZDO belongs to the client nearest
            // it, which rewrites it every frame. A plain server write to it is lost (0.10.8: a moored Karve
            // beside its crew was "parked" in the log and never grew a row on screen) and a forced one can
            // drop the owner's in-flight cargo change. So a client-simulated hull is never written from here:
            // when it needs its anchor the hull is BORROWED through vanilla's own open handshake (HullBorrow,
            // 0.10.10 - 0.10.9 forced the write with a revision lead, which could drop the owner's in-flight
            // deposit) and written by WriteBorrowedHull once the server owns it. A hull under sail is left
            // alone until it stops; a hull nobody simulates is written directly, the same as a chest.
            // (0.10.3 saw one moored Karve re-parked 78 times for the lost-write reason; the 0.10.3-0.10.7
            // velocity-only check then never parked a moored ship again.)
            bool ship = prefab.GetComponent<Ship>() != null;
            if (ship && (ShipAttachment.IsUnderway(zdo) || HullBorrow.IsPending(zdo.m_uid)))
            {
                return;
            }

            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            (_, int vanillaHeight) = GridGrowth.GetVanillaSize(prefab.name, template);
            if (height <= vanillaHeight)
            {
                return;
            }

            Inventory? inventory = ZdoInventoryIO.Load(zdo, width, height);
            if (inventory == null || inventory.NrOfItems() == 0)
            {
                return;
            }

            if (!ContainerRows.EnsureAnchor(inventory, vanillaHeight, height, out ItemDrop.ItemData? moved) || moved == null)
            {
                return;
            }

            if (ship && ShipAttachment.IsSimulatedByClient(zdo))
            {
                // Judged on the server's copy (fresh to within a frame - the owner sends every one), but not
                // written from it: ask the owner for the hull and write the copy that arrives with it. One
                // request a minute per hull, granted or not (HullBorrow's cooldown).
                if (HullBorrow.Request(zdo, WriteBorrowedHullDelegate))
                {
                    WonderlandDebug.LogInfo($"[ContainerRows] '{prefab.name}' at {zdo.GetPosition():F0}: needs its row parked - asked its owner {zdo.GetOwner()} for the hull.");
                }
                return;
            }

            ZdoInventoryIO.Save(zdo, inventory);
            _anchoredTotal++;
            WonderlandDebug.LogInfo($"[ContainerRows] '{prefab.name}' at {zdo.GetPosition():F0}: parked {moved.m_shared.m_name} x{moved.m_stack} in row {height - 1} ({vanillaHeight} -> {height} rows). Anchors this session: {_anchoredTotal}");
        }

        /// <summary>The borrowed write: runs the frame the hull's owner has handed it over, on the copy that
        /// came with the hand-over (its last word), and only if that copy still needs the anchor.</summary>
        private static void WriteBorrowedHull(ZDO zdo)
        {
            if (!zdo.IsValid() || ZdoInventoryIO.IsBusy(zdo) || ZNetScene.instance == null)
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Container template = ContainerRegistry.ResolveTemplate(prefab);
            if (template == null || !ContainerRows.IsEligible(prefab, template))
            {
                return;
            }
            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            (_, int vanillaHeight) = GridGrowth.GetVanillaSize(prefab.name, template);
            if (height <= vanillaHeight)
            {
                return;
            }
            Inventory? inventory = ZdoInventoryIO.Load(zdo, width, height);
            if (inventory == null || inventory.NrOfItems() == 0)
            {
                return;
            }
            if (!ContainerRows.EnsureAnchor(inventory, vanillaHeight, height, out ItemDrop.ItemData? moved) || moved == null)
            {
                return; // the owner's last word already had it
            }
            ZdoInventoryIO.Save(zdo, inventory);
            _anchoredTotal++;
            WonderlandDebug.LogInfo($"[ContainerRows] '{prefab.name}' at {zdo.GetPosition():F0}: parked {moved.m_shared.m_name} x{moved.m_stack} in row {height - 1} ({vanillaHeight} -> {height} rows) on a borrowed hull. Anchors this session: {_anchoredTotal}");
        }
    }
}
