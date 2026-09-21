using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;
using Wonderland.Subsystems.Storage;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// Two triggers sharing one "move items toward a container" engine:
    ///  - Drop-to-chest (match-required - a container only tops up an item type it already holds), in
    ///    two passes since 0.8.3: a near-player pass every VacuumInterval that starts from the ground
    ///    items around each connected player and only loads the containers within VacuumRadius of one
    ///    of them (nothing on the ground, nothing loaded - the steady state), plus the world-wide
    ///    round-robin over every container type for chests nobody is near. The round-robin alone was
    ///    the 0.8.2 complaint "vacuum takes too long": 64 container types x a 600k-ZDO world at 25
    ///    chunks per pass gave any one chest its turn about every half minute. Since 0.8.4 a stack a
    ///    client owns is claimed first and moved a second later (the settle step,
    ///    see VacuumGroundItemsInto): moving it in the same instant its player picked it up duplicated it.
    ///  - Auto-harvest: event-driven since 0.8.2. The picking client's own "RPC_SetPicked" broadcast
    ///    (the one wire-visible event of every pick, crops included) reaches the server's ZRoutedRpc and
    ///    HarvestTriggerPatch hands it to OnPickedRpc; half a second later the radius around that plant is
    ///    swept for other ready Pickables of the same type. Those get "harvested" purely at the ZDO layer
    ///    (see HarvestPickable below) rather than through the real RPC_Pick, which needs a live Pickable
    ///    instance the dedicated server never has (ConnectedCharacters explains why) and which now also
    ///    dereferences Player.m_localPlayer (HEADLESS-AND-EMPTY-SERVER-FACTS §5).
    /// </summary>
    public static class VacuumEngine
    {
        // Ledger tags - BarrkBotExport sums them, so they are shared rather than repeated.
        public const string VacuumTag = "Vacuum";
        public const string HarvestTag = "AutoHarvest";

        private static BudgetedSweep? _containerSweep;
        private static readonly Action<ZDO> VisitBackgroundContainer = ProcessBackgroundContainer;
        private static float _vacuumTimer;
        private static readonly HashSet<ZDOID> _destroyedThisBatch = new HashSet<ZDOID>();
        private static readonly List<ZDO> _pendingGroundDestroy = new List<ZDO>();
        private static readonly HashSet<int> _containerPrefabHashes = new HashSet<int>();
        private static readonly HashSet<int> _itemDropPrefabHashes = new HashSet<int>();
        private static readonly HashSet<ZDOID> _visitedThisPass = new HashSet<ZDOID>();
        private static readonly List<ZDO> _groundBuffer = new List<ZDO>();
        private static readonly List<ZDO> _groundItems = new List<ZDO>();
        private static readonly List<ZDO> _nearBuffer = new List<ZDO>();
        private static readonly Dictionary<ZDOID, long> _harvestedAt = new Dictionary<ZDOID, long>();
        private const int MaxTrackedPickables = 50000;

        /// <summary>A ground stack the server has taken from a client and is letting settle before it moves it
        /// (see the "settle step" note above VacuumGroundItemsInto): when the hold matures, and which container
        /// asked for it.</summary>
        private readonly struct GroundHold
        {
            public readonly float Until;
            public readonly ZDOID Container;

            public GroundHold(float until, ZDOID container)
            {
                Until = until;
                Container = container;
            }
        }

        private static readonly Dictionary<ZDOID, GroundHold> _holds = new Dictionary<ZDOID, GroundHold>();
        private static readonly List<ZDOID> _holdScratch = new List<ZDOID>();
        private static readonly HashSet<ZDOID> _holdContainersThisFrame = new HashSet<ZDOID>();
        /// <summary>Ground stacks a container wanted this pass but TryTakeOwnership left alone because their
        /// owning client still reports them moving (see the at-rest gate there). Per pass, like
        /// _visitedThisPass: VacuumAround must not file them as unwanted - they get the next pass, at rest.</summary>
        private static readonly HashSet<ZDOID> _movingThisPass = new HashSet<ZDOID>();
        /// <summary>Above this speed (m/s, squared) a client-owned stack is still falling, sliding or being
        /// carried and is not claimed: the claim freezes it on every client at the server's last-known position
        /// - the "jerks to a stop mid-air" a player sees when a stack is grabbed before it lands. A sleeping
        /// Rigidbody writes exactly zero (ZSyncTransform.OwnerSync sets s_velHash from GetVelocity() whenever
        /// it changes), so at rest this gate costs nothing.</summary>
        private const float AtRestSpeedSqr = 0.05f * 0.05f;
        /// <summary>Ground stacks a pass looked at and no container in reach would take, with the time until
        /// which they stay out of the qualifying set (the containers around them are not loaded for them).</summary>
        private static readonly Dictionary<ZDOID, float> _unwantedUntil = new Dictionary<ZDOID, float>();
        private static readonly List<ZDOID> _unwantedScratch = new List<ZDOID>(); // own list: PruneUnwanted can run inside ProcessMaturedHolds' loop over _holdScratch
        private static readonly List<bool> _groundTruncated = new List<bool>();
        private static readonly HashSet<ZDOID> _effectsThisFrame = new HashSet<ZDOID>();
        private static int _nearRotation;
        private static int _stacksMoved;
        private static string _excludedItemsSource;
        private static string _excludedContainersSource;
        private static readonly HashSet<string> _excludedItems = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _excludedContainers = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        /// <summary>Per item prefab hash: the prefab's name (what the exclusion list matches) and the shared item
        /// name (what match-required compares), so a ground ZDO is classified without a single GetPrefab.</summary>
        private static readonly Dictionary<int, string> _prefabNameByHash = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> _itemNameByPrefab = new Dictionary<int, string>();
        private static readonly List<string> _groundNames = new List<string>();
        private static readonly List<ZDO> _nearContainers = new List<ZDO>();

        /// <summary>What a container held the last time it was loaded, valid while its DataRevision is
        /// unchanged (every write to a ZDO, by any client or by this server, raises it). Lets the near-player
        /// pass answer "does any chest in reach already hold this?" without loading a chest that nobody has
        /// touched since - the steady state with junk on the floor is zero loads.</summary>
        private sealed class ContainerNames
        {
            public uint Revision;
            public float SeenAt;
            /// <summary>Shared item name -> total count across stacks at Revision (0.10.11: counts, so a chest
            /// holding only a station's reserve is not reloaded every visit).</summary>
            public readonly Dictionary<string, int> Names = new Dictionary<string, int>();
        }

        private static readonly Dictionary<ZDOID, ContainerNames> _containerNames = new Dictionary<ZDOID, ContainerNames>();
        private static readonly List<ZDOID> _containerNameScratch = new List<ZDOID>();
        private static readonly HashSet<(ZDOID, string)> _fullLogged = new HashSet<(ZDOID, string)>();
        private static float _fullLoggedClearedAt;
        private static float _lastVacuumWarning = -999f;
        private const int MaxContainerNameEntries = 8192;
        private const float ContainerNameTtlSeconds = 600f;
        /// <summary>"container full" is reported once per chest and item type per this many seconds.</summary>
        private const float FullLogIntervalSeconds = 60f;

        /// <summary>How long a claimed stack settles before it is moved: the claim has to reach the owning
        /// client (ZDOMan.SendZDOToPeers2 waits 50 ms and then serves one peer per frame, so plus one server
        /// frame per connected peer, plus the trip) and any pickup that client committed before it heard has
        /// to come back as its DestroyZDO (another trip). A second covers a 300 ms round trip on a 14-peer
        /// server at 20 ms frames with room to spare; the case it does not cover - a peer whose send queue is
        /// saturated, a server frame of 100 ms - is caught by LatePickupPatch, which sees that late DestroyZDO
        /// and takes the copy back out of the chest.</summary>
        private const float HoldSeconds = 1f;

        /// <summary>A stack the vacuum moved, kept for RecentMoveSeconds so a client's late DestroyZDO for it
        /// (LatePickupPatch) can be answered by taking the stack back out of the container it went into.</summary>
        private readonly struct MovedStack
        {
            public readonly ZDOID Container;
            public readonly string ItemName;
            public readonly int Stack;
            public readonly float At;

            public MovedStack(ZDOID container, string itemName, int stack, float at)
            {
                Container = container;
                ItemName = itemName;
                Stack = stack;
                At = at;
            }
        }

        private static readonly Dictionary<ZDOID, MovedStack> _recentlyMoved = new Dictionary<ZDOID, MovedStack>();
        private static readonly List<ZDOID> _movedScratch = new List<ZDOID>();
        private static readonly List<MovedStack> _pendingTakeBacks = new List<MovedStack>();
        private const float RecentMoveSeconds = 10f;
        private const float TakeBackRetrySeconds = 30f;
        /// <summary>A hold nobody got to act on (per-frame budget spent, container gone) is handed back this
        /// long after it matured, so a stack is never left frozen.</summary>
        private const float HoldHardExpirySeconds = 3f;
        /// <summary>How long a stack no container in reach wanted stays out of the qualifying set. Short enough
        /// that dropping a matching item into the chest is still followed by the stack within a few seconds;
        /// long enough that a trophy on the floor does not load every chest in the room every pass.</summary>
        private const float UnwantedRetrySeconds = 5f;
        /// <summary>Load guard: containers one VacuumAround call may load. The rest wait for the next pass,
        /// which starts from a rotated position so every container gets its turn.</summary>
        private const int MaxContainersPerVacuumAround = 32;
        /// <summary>Load guard: containers ProcessMaturedHolds may load per frame.</summary>
        private const int MaxHoldContainersPerFrame = 8;
        private const int MaxUnwantedEntries = 4096;

        public static void Initialize()
        {
            _containerSweep = new BudgetedSweep(ContainerRegistry.PrefabNames);

            _containerPrefabHashes.Clear();
            foreach (string name in ContainerRegistry.PrefabNames)
            {
                _containerPrefabHashes.Add(name.GetStableHashCode());
            }

            // Every prefab that is a dropped item, keyed the way ZDO.GetPrefab reports it: m_namedPrefabs is
            // ZNetScene's own name.GetStableHashCode() table and the one ZNetScene.GetPrefab(int) resolves
            // through, so this set and the per-item lookup below agree by construction. Built here rather
            // than borrowed from WaterBuoyancyEngine so the vacuum does not depend on a sibling feature's init
            // order - which is how 0.8.3 lost that set's one deliberate exception: a live Fish carries an
            // ItemDrop on the same prefab (Fish.Awake takes GetComponent<ItemDrop>()), and dropping a caught
            // fish puts a swimming one back in the water. Counting them as ground items kept every chest near
            // a shore loaded each pass, and a chest holding that fish type would pull a live one out of the
            // lake.
            _itemDropPrefabHashes.Clear();
            _prefabNameByHash.Clear();
            _itemNameByPrefab.Clear();
            _containerNames.Clear();
            if (ZNetScene.instance != null)
            {
                foreach (KeyValuePair<int, GameObject> entry in ZNetScene.instance.m_namedPrefabs)
                {
                    GameObject prefab = entry.Value;
                    ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                    if (drop == null || prefab.GetComponent<Fish>() != null)
                    {
                        continue;
                    }
                    string? itemName = drop.m_itemData?.m_shared?.m_name;
                    if (itemName == null || itemName.Length == 0)
                    {
                        continue; // nothing match-required could ever compare it to
                    }
                    _itemDropPrefabHashes.Add(entry.Key);
                    _prefabNameByHash[entry.Key] = prefab.name;
                    _itemNameByPrefab[entry.Key] = itemName;
                }
            }
            WonderlandDebug.LogAlways($"[Vacuum] tracking {_containerPrefabHashes.Count} container and {_itemDropPrefabHashes.Count} item prefab types.");
        }

        public static void OnUpdate(float dt)
        {
            if (_containerSweep == null)
            {
                return;
            }

            // Ground stacks moved last frame are gone from the sector index by now (their DestroyZDO went
            // out in ZDOMan.Update), so the "already moved" set starts empty each frame. Every routed RPC
            // that arrives before this frame's clear still sees last frame's moves (WasMovedThisFrame),
            // whichever order Unity runs ZNet.Update and this in.
            _destroyedThisBatch.Clear();
            _effectsThisFrame.Clear();

            if (WonderlandConfig.VacuumEnabled?.Value == true)
            {
                _vacuumTimer += dt;
                if (_vacuumTimer >= (WonderlandConfig.VacuumInterval?.Value ?? 2f))
                {
                    _vacuumTimer = 0f;
                    BeginVacuumPass();
                    ProcessVacuumNearPlayers();
                    _containerSweep.Grant(Mathf.Max(1, WonderlandConfig.VacuumBatchSize?.Value ?? 25));
                }
                ProcessVacuumBatch();
                ProcessMaturedHolds();
            }
            else if (_holds.Count > 0)
            {
                ReleaseAllHolds(); // switched off mid-settle: the stacks go back to their players untouched
            }
            if (_pendingTakeBacks.Count > 0)
            {
                ProcessPendingTakeBacks();
            }

            if (WonderlandConfig.AutoHarvestEnabled?.Value == true)
            {
                ProcessPendingHarvests();
            }
            else if (_pendingTriggers.Count > 0)
            {
                // Switched off between a pick and its sweep: the pick already happened on the client, the
                // bonus simply does not follow.
                _pendingTriggers.Clear();
                _pendingUids.Clear();
            }

            _ledgerPruneTimer += dt;
            if (_ledgerPruneTimer >= LedgerPruneIntervalSeconds)
            {
                _ledgerPruneTimer = 0f;
                PruneHarvestLedger();
                PruneUnwanted(Time.time);
                PruneContainerNames(Time.time, force: false);
                PruneRecentlyMoved(Time.time);
            }
        }

        /// <summary>One pass = one clear of the visited-container set, so within a pass no chest is loaded
        /// twice. The set is per pass, not per frame: the vacuum that follows a harvest sweep starts a pass
        /// of its own (its drops did not exist when the regular pass ran), so on a frame with both, a chest
        /// near the sweep is loaded once more. _destroyedThisBatch (ground stacks already moved and queued
        /// for destruction) is NOT cleared here but once per frame in OnUpdate: a queued DestroyZDO only
        /// leaves the sector index in ZDOMan.Update, so that second look would otherwise see a stack the
        /// regular pass had just moved and move it a second time.</summary>
        private static void BeginVacuumPass()
        {
            _visitedThisPass.Clear();
            _movingThisPass.Clear();
        }

        /// <summary>The world-wide background round-robin: VacuumBatchSize chunks of the container-type
        /// scanner per pass, granted at the pass and visited over the frames that follow within
        /// SweepBudgetMs each (since 0.10.9 - one frame per pass used to carry the whole batch, 250-440 ms
        /// on the live world whenever the scanner reached a common chest type). Covers chests nobody is
        /// standing near (and carries the overflow guard and cache drain to them); latency here scales
        /// with world size by design. Runs after the near-player pass now, not before: that pass judges
        /// every container in reach itself, so nothing it does depended on the background's marks.</summary>
        private static void ProcessVacuumBatch()
        {
            _containerSweep!.Run(WonderlandConfig.SweepBudgetMs?.Value ?? 2f, VisitBackgroundContainer);
        }

        private static void ProcessBackgroundContainer(ZDO containerZdo)
        {
            if (_visitedThisPass.Add(containerZdo.m_uid))
            {
                ProcessContainer(containerZdo);
            }
        }

        /// <summary>The pass that makes drop-to-chest feel instant: around each connected player, only the
        /// containers that actually have a loose item within VacuumRadius are loaded. With nothing on the
        /// ground it costs one sector query per player and no container I/O at all.</summary>
        private static void ProcessVacuumNearPlayers()
        {
            float reach = WonderlandConfig.VacuumNearPlayersRadius?.Value ?? 32f;
            float vacuumRadius = WonderlandConfig.VacuumRadius?.Value ?? 10f;
            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                VacuumAround(character.Position, reach, vacuumRadius);
            }
        }

        /// <summary>Loads and processes the containers within <paramref name="vacuumRadius"/> of at least one
        /// ground item that lies within <paramref name="reach"/> of <paramref name="center"/> and that may hold
        /// that item's type - at most MaxContainersPerVacuumAround of them, from a start that rotates over the
        /// containers between calls. Returns how many were processed. Ground items first: no loose item, no
        /// container load. A ground item only counts while it could be taken at all (a dropped item, not
        /// placed as a piece, not excluded, not moved or claimed already this frame, not recently found
        /// unwanted); a container whose contents are known from an earlier load at its current DataRevision
        /// is only loaded when it holds one of those item types. A stack still lying there after every
        /// container in reach had its look is remembered as unwanted for UnwantedRetrySeconds.</summary>
        private static int VacuumAround(Vector3 center, float reach, float vacuumRadius)
        {
            if (ZNetScene.instance == null)
            {
                return 0;
            }

            float now = Time.time;
            _groundItems.Clear();
            _groundNames.Clear();
            _groundTruncated.Clear();
            foreach (ZDO zdo in ZdoSpatialQuery.FindNear(center, reach, _groundBuffer))
            {
                if (IsCandidateGroundItem(zdo, now, out string itemName))
                {
                    _groundItems.Add(zdo);
                    _groundNames.Add(itemName);
                    _groundTruncated.Add(false);
                }
            }
            if (_groundItems.Count == 0)
            {
                return 0;
            }

            // Containers only, so the rotation and the cap count chests, not every tree and rock in the ring.
            _nearContainers.Clear();
            foreach (ZDO zdo in ZdoSpatialQuery.FindNear(center, reach + vacuumRadius, _nearBuffer))
            {
                if (_containerPrefabHashes.Contains(zdo.GetPrefab()) && !_visitedThisPass.Contains(zdo.m_uid))
                {
                    _nearContainers.Add(zdo);
                }
            }

            float radiusSqr = vacuumRadius * vacuumRadius;
            int processed = 0;
            // Index-based on purpose: ProcessContainer runs inside this loop, and the two spatial queries it
            // can reach (VacuumGroundItemsInto, GridGrowth.FindNearestSiblingContainer) allocate their own
            // lists today - an enumerator here would turn any future buffer reuse into an exception mid-pass.
            int count = _nearContainers.Count;
            int start = count > 0 ? (int)((uint)_nearRotation % (uint)count) : 0;
            for (int k = 0; k < count; k++)
            {
                ZDO containerZdo = _nearContainers[(start + k) % count];
                bool busy = ZdoInventoryIO.IsBusy(containerZdo); // a player has it open: not judged this pass
                bool overBudget = processed >= MaxContainersPerVacuumAround;
                bool known = _containerNames.TryGetValue(containerZdo.m_uid, out ContainerNames cached) && cached.Revision == containerZdo.DataRevision;
                Vector3 position = containerZdo.GetPosition();
                bool qualifies = false;
                for (int i = 0; i < _groundItems.Count; i++)
                {
                    ZDO ground = _groundItems[i];
                    if (_destroyedThisBatch.Contains(ground.m_uid) || _holds.ContainsKey(ground.m_uid))
                    {
                        continue; // taken, or claimed, by a container earlier in this call
                    }
                    if ((ground.GetPosition() - position).sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }
                    if (known && !cached.Names.ContainsKey(_groundNames[i]))
                    {
                        continue; // held none of that type when last read, and nothing has written it since
                    }
                    qualifies = true;
                    if (!busy && !overBudget)
                    {
                        break;
                    }
                    _groundTruncated[i] = true; // a container that never got its look wanted it, maybe
                }
                if (!qualifies || busy || overBudget)
                {
                    continue;
                }
                _visitedThisPass.Add(containerZdo.m_uid);
                ProcessContainer(containerZdo);
                processed++;
            }
            _nearRotation += processed; // a room the cap cut short is finished over the next passes, not re-started

            for (int i = 0; i < _groundItems.Count; i++)
            {
                ZDOID uid = _groundItems[i].m_uid;
                if (_groundTruncated[i] || _destroyedThisBatch.Contains(uid) || _holds.ContainsKey(uid) || _movingThisPass.Contains(uid))
                {
                    continue; // never judged, moved, settling, or still in motion - none of those is "unwanted"
                }
                RememberUnwanted(uid, now);
            }
            return processed;
        }

        /// <summary>A ground ZDO that could be vacuumed at all, judged without loading anything or resolving a
        /// prefab: a dropped item (live fish excluded, see Initialize) that is not placed as a piece or
        /// hatching, not on VacuumExcludedItems, not moved or claimed this frame, and not one every container
        /// in reach declined within the last UnwantedRetrySeconds. Gives back the shared item name the
        /// match-required rule compares.</summary>
        private static bool IsCandidateGroundItem(ZDO zdo, float now, out string itemName)
        {
            int prefabHash = zdo.GetPrefab();
            if (!_itemNameByPrefab.TryGetValue(prefabHash, out itemName) || _destroyedThisBatch.Contains(zdo.m_uid) || _holds.ContainsKey(zdo.m_uid))
            {
                return false;
            }
            if (_unwantedUntil.TryGetValue(zdo.m_uid, out float until))
            {
                if (now < until)
                {
                    return false;
                }
                _unwantedUntil.Remove(zdo.m_uid);
            }
            if (IsPlacedOrHatching(zdo))
            {
                return false;
            }
            HashSet<string> excluded = ParsedList(WonderlandConfig.VacuumExcludedItems?.Value, ref _excludedItemsSource, _excludedItems);
            return excluded.Count == 0 || !_prefabNameByHash.TryGetValue(prefabHash, out string prefabName) || !excluded.Contains(prefabName);
        }

        /// <summary>An item ZDO that is part of the world rather than loot: placed as a piece (a feast on a
        /// table - Player.PlacePiece calls ItemDrop.MakePiece, which sets s_piece; its eaten portions live in
        /// s_value, so vacuuming it would put a whole feast in the chest) or an egg that has started to warm
        /// by a fire (EggGrow.GrowUpdate keeps s_growStart above zero while it can grow; an egg dropped
        /// anywhere else reads zero and is ordinary loot).</summary>
        private static bool IsPlacedOrHatching(ZDO zdo)
        {
            return zdo.GetBool(ZDOVars.s_piece) || zdo.GetFloat(ZDOVars.s_growStart) > 0f;
        }

        /// <summary>True when the cache knows this container's contents at its CURRENT DataRevision and the
        /// item (by shared name, e.g. "$item_coal") is not among them - a load that can be skipped. Unknown
        /// or stale entries answer false: load it. Shared with ProductionSupplyEngine since 0.10.11, where a
        /// station visit was loading every chest in range once per fuel and once per ore the station knows,
        /// found or not (70-90 ms per visit in a dense base).</summary>
        public static bool KnownNotToHold(ZDO containerZdo, string sharedName)
        {
            return KnownToHoldAtMost(containerZdo, sharedName, 0);
        }

        /// <summary>True when the cache knows this container at its CURRENT DataRevision and it holds no more
        /// than <paramref name="count"/> of the item - for production supply's reserve: a chest with only the
        /// reserve left would otherwise be loaded on every visit of every station in range and rejected after.</summary>
        public static bool KnownToHoldAtMost(ZDO containerZdo, string sharedName, int count)
        {
            if (!_containerNames.TryGetValue(containerZdo.m_uid, out ContainerNames cached) || cached.Revision != containerZdo.DataRevision)
            {
                return false;
            }
            return !cached.Names.TryGetValue(sharedName, out int held) || held <= count;
        }

        /// <summary>Records what the container holds at its current DataRevision (called after its load, or
        /// after its save when it changed, so the revision on file is the one the cache is keyed to).</summary>
        public static void RememberContainerNames(ZDO containerZdo, Inventory inventory)
        {
            float now = Time.time;
            if (!_containerNames.TryGetValue(containerZdo.m_uid, out ContainerNames entry))
            {
                if (_containerNames.Count >= MaxContainerNameEntries)
                {
                    PruneContainerNames(now, force: true);
                    if (_containerNames.Count >= MaxContainerNameEntries)
                    {
                        return; // this one simply loads next time
                    }
                }
                entry = new ContainerNames();
                _containerNames[containerZdo.m_uid] = entry;
            }
            entry.Names.Clear();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                string name = item?.m_shared?.m_name;
                if (!string.IsNullOrEmpty(name))
                {
                    entry.Names.TryGetValue(name, out int held);
                    entry.Names[name] = held + item.m_stack;
                }
            }
            entry.Revision = containerZdo.DataRevision;
            entry.SeenAt = now;
        }

        /// <summary>Drops entries not refreshed within ContainerNameTtlSeconds; with force, clears everything
        /// when that still leaves the cache full (a cold cache only costs one load per chest).</summary>
        private static void PruneContainerNames(float now, bool force)
        {
            if (_containerNames.Count == 0)
            {
                return;
            }
            _containerNameScratch.Clear();
            foreach (KeyValuePair<ZDOID, ContainerNames> entry in _containerNames)
            {
                if (now - entry.Value.SeenAt > ContainerNameTtlSeconds)
                {
                    _containerNameScratch.Add(entry.Key);
                }
            }
            for (int i = 0; i < _containerNameScratch.Count; i++)
            {
                _containerNames.Remove(_containerNameScratch[i]);
            }
            if (force && _containerNames.Count >= MaxContainerNameEntries)
            {
                _containerNames.Clear();
            }
        }

        private static void RememberUnwanted(ZDOID uid, float now)
        {
            if (_unwantedUntil.Count >= MaxUnwantedEntries && !_unwantedUntil.ContainsKey(uid))
            {
                PruneUnwanted(now);
                if (_unwantedUntil.Count >= MaxUnwantedEntries)
                {
                    return; // still full: this one simply costs a look next pass
                }
            }
            _unwantedUntil[uid] = now + UnwantedRetrySeconds;
        }

        private static void PruneUnwanted(float now)
        {
            if (_unwantedUntil.Count == 0)
            {
                return;
            }
            _unwantedScratch.Clear();
            foreach (KeyValuePair<ZDOID, float> entry in _unwantedUntil)
            {
                if (now >= entry.Value)
                {
                    _unwantedScratch.Add(entry.Key);
                }
            }
            for (int i = 0; i < _unwantedScratch.Count; i++)
            {
                _unwantedUntil.Remove(_unwantedScratch[i]);
            }
        }

        private static void ProcessContainer(ZDO containerZdo)
        {
            if (ZNetScene.instance == null || !containerZdo.IsValid() || ZdoInventoryIO.IsBusy(containerZdo))
            {
                return;
            }

            int prefabHash = containerZdo.GetPrefab();
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
            Container template = ContainerRegistry.ResolveTemplate(prefab);
            if (template == null)
            {
                return;
            }
            string prefabName = prefab.name;
            if (IsContainerExcluded(prefabName))
            {
                return;
            }

            // A hull a client is simulating is off limits whatever its speed: the vacuum moves a stack into the
            // container and destroys the ground ZDO, and a container write that the owner's revision stream
            // discards (a floating hull never sleeps - Ship.CustomFixedUpdate wakes the body every step) would
            // leave nothing behind. Only a hull nobody is simulating - moored and crew gone, or beyond every
            // owner's active area - takes vacuumed stacks. (0.10.8 review: the 0.10.3 velocity-only guard had
            // been covering this by accident.)
            if (prefab.GetComponent<Ship>() != null && ShipAttachment.IsSimulatedByClient(containerZdo))
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
            bool vacuumed = false;
            bool rowsEligible = ContainerRows.IsEnabled && ContainerRows.IsEligible(prefab, template);
            int vanillaHeight = rowsEligible ? GridGrowth.GetVanillaSize(prefabName, template).height : 0;
            int movedBefore = _stacksMoved;
            try
            {
                // Overflow guard rides this same pass - every container Wonderland already has open gets
                // checked, on a short regular interval, well before any real client could load it. Its
                // sibling save and cache store are committed inside the call, so this container's own write
                // follows at once: nothing that can throw on foreign data sits between the two.
                float linkRadius = WonderlandConfig.ContainerLinkRadius?.Value ?? 10f;
                if (GridGrowth.EnforceOverflow(containerZdo, inventory, prefab, template, linkRadius, out bool _, out ZDO _))
                {
                    changed = true;
                    Commit(containerZdo, inventory, rowsEligible, vanillaHeight, height);
                }

                if (inventory.NrOfItems() > 0)
                {
                    vacuumed = VacuumGroundItemsInto(containerZdo, inventory, prefabHash);
                    changed |= vacuumed;
                }

                // The cache drain removes from the cache (and writes its file) inside the call, so it sits
                // directly before the write that keeps its items; the ground stacks - the one input that can
                // be malformed - were all read above.
                if (ItemCache.TryDrainInto(containerZdo, inventory))
                {
                    changed = true;
                }

                if (changed)
                {
                    Commit(containerZdo, inventory, rowsEligible, vanillaHeight, height);
                }
                RememberContainerNames(containerZdo, inventory);
            }
            catch (System.Exception ex)
            {
                // The container write did not happen, so the ground stacks queued behind it are still the
                // only copies: forget the queue WITHOUT destroying anything (a finally-flush would do the
                // opposite) and do not count them as moved. Stacks this call had already claimed stay held;
                // ProcessMaturedHolds hands them back. The frame's other engines carry on.
                for (int i = 0; i < _pendingGroundDestroy.Count; i++)
                {
                    _recentlyMoved.Remove(_pendingGroundDestroy[i].m_uid);
                }
                _pendingGroundDestroy.Clear();
                _stacksMoved = movedBefore;
                WarnVacuumRateLimited($"[Vacuum] {prefabName} at {containerZdo.GetPosition()} skipped this pass: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // The ground copies go the moment the chest is committed; the splash comes after, so a hiccup in
            // the effect can never leave a stack both in the chest and on the ground.
            FlushPendingGroundDestroy();

            if (vacuumed && _effectsThisFrame.Add(containerZdo.m_uid))
            {
                // One splash per chest per frame, however many sub-passes fed it.
                PlayVacuumEffect(containerZdo.GetPosition());
            }
        }

        /// <summary>The container's write, with its grown rows kept visible when ContainerRows applies.</summary>
        private static void Commit(ZDO containerZdo, Inventory inventory, bool rowsEligible, int vanillaHeight, int height)
        {
            if (rowsEligible)
            {
                ContainerRows.EnsureAnchor(inventory, vanillaHeight, height, out _);
            }
            ZdoInventoryIO.Save(containerZdo, inventory);
        }

        private static void WarnVacuumRateLimited(string message)
        {
            if (Time.time - _lastVacuumWarning < TriggerWarningIntervalSeconds)
            {
                return;
            }
            _lastVacuumWarning = Time.time;
            WonderlandDebug.LogWarning(message);
        }

        // === Visual & Audio feedback ===

        /// <summary>
        /// Spawns vanilla's fermenter liquid splash (vfx_fermenter_add) and splash sound (sfx_fermenter_add)
        /// at the container via Valheim's built-in "SpawnObject" routed RPC.
        ///
        /// Dedicated-server fact: Calling Object.Instantiate on a dedicated server does not broadcast
        /// non-persistent prefabs to clients (and the server is headless). Valheim's native ZNetScene.RPC_SpawnObject
        /// ("SpawnObject") is registered on EVERY client out of the box. Broadcasting it directly to connected
        /// peers within audible/visible range (80m) ensures the vanilla client executes Object.Instantiate locally,
        /// playing the particle splash and audio cleanly with zero server overhead and 100% vanilla compatibility.
        /// </summary>
        private static void PlayVacuumEffect(Vector3 position)
        {
            if (WonderlandConfig.VacuumEffectEnabled?.Value != true)
            {
                return;
            }
            if (ZNet.instance == null || ZRoutedRpc.instance == null)
            {
                return;
            }

            string vfxName = WonderlandConfig.VacuumEffectPrefab?.Value ?? "vfx_fermenter_add";
            string sfxName = WonderlandConfig.VacuumSoundPrefab?.Value ?? "sfx_fermenter_add";

            int vfxHash = !string.IsNullOrEmpty(vfxName) ? vfxName.GetStableHashCode() : 0;
            int sfxHash = !string.IsNullOrEmpty(sfxName) ? sfxName.GetStableHashCode() : 0;

            if (vfxHash == 0 && sfxHash == 0)
            {
                return;
            }

            Vector3 spawnPos = position + Vector3.up * 0.5f;
            Quaternion rot = Quaternion.identity;
            const float maxAudibleDistance = 80f;

            // Broadcast to connected vanilla clients near the container
            List<ZNetPeer> peers = ZNet.instance.GetPeers();
            if (peers != null)
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    ZNetPeer peer = peers[i];
                    if (peer != null && peer.IsReady() && Vector3.Distance(peer.GetRefPos(), spawnPos) <= maxAudibleDistance)
                    {
                        if (vfxHash != 0)
                        {
                            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "SpawnObject", spawnPos, rot, vfxHash);
                        }
                        if (sfxHash != 0)
                        {
                            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "SpawnObject", spawnPos, rot, sfxHash);
                        }
                    }
                }
            }

            // If non-dedicated host (player running local listen server), also invoke for local player
            if (!ZNet.instance.IsDedicated() && Vector3.Distance(ZNet.instance.GetReferencePosition(), spawnPos) <= maxAudibleDistance)
            {
                long myId = ZNet.GetUID();
                if (vfxHash != 0)
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(myId, "SpawnObject", spawnPos, rot, vfxHash);
                }
                if (sfxHash != 0)
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(myId, "SpawnObject", spawnPos, rot, sfxHash);
                }
            }
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

        // === The settle step (0.8.4) ===
        //
        // A stack a client owns is never moved in the pass that finds it. Vanilla's pickup is entirely
        // client-side: Humanoid.Pickup (1.0.12 server decompile 7397-7455) adds the stack to the inventory
        // and only then ZNetScene.Destroy -> ZDOMan.DestroyZDO sends the ZDO's destruction; the server
        // learns of a pickup one trip late and cannot veto it. If the server moved the same stack into a
        // chest inside that trip - a couple of hundred milliseconds either side of the pickup - the player
        // keeps theirs and the chest has a copy. At the half-minute cadence of 0.8.2 that overlap was a
        // fluke; the near-player pass of 0.8.3 moves a drop within a second of landing, which is exactly
        // when the player who dropped it, or killed for it, is standing on it with auto-pickup running.
        //
        // So the server first takes the ZDO (SetOwner + DataRevision += 4096 + ForceSendZDO, the same
        // three writes WaterBuoyancyEngine uses to hold an item at the surface: the revision jump makes any
        // position packet the client already had in flight arrive stale, since RPC_ZDOData applies a
        // packet's owner field unconditionally whenever its data revision is newer) and lets it settle
        // for HoldSeconds. Once the client has the new owner, ItemDrop.CanPickup (70558) is false for it,
        // so it can no longer pick up at all; a pickup it committed before it heard comes back as its
        // DestroyZDO and the ZDO is gone by the time the hold matures. A player who wants it meanwhile
        // asks the owner - the server - with RPC_RequestOwn; WaterBuoyancyEngine.HandlePickupRequest grants
        // it (with a 3 s grace the SetOwner prefix and this code both respect), and at maturity the stack
        // is no longer the server's and is left alone: the player wins every tie. Vanilla's own passive
        // hand-out, ZDOMan.ReleaseNearbyZDOS (every 2 s, gives server-owned ZDOs near a player to that
        // player), is blocked for a held stack through the same prefix, or one hold in four would be
        // broken for nothing. Stacks born on the server - a sweep's drops, a floating item - are moved
        // at once: no client can be mid-pickup on a ZDO it does not own, and a grant made later in this
        // frame is refused because HandlePickupRequest checks WasMovedThisFrame.
        //
        // The claim is only made once the stack is at rest (0.10.5, AtRestSpeedSqr). Every other client
        // renders a claimed stack from the ZDO alone: ZSyncTransform.ClientSync lerps it to the server's
        // last-known position, turns gravity off and puts the body to sleep. Claimed while still falling,
        // a stack visibly jumps back up to where the server last saw it and hangs there for the hold -
        // the "jitter" reported against 0.10.4. At rest the same claim is invisible.

        /// <summary>Match-required: only tops up an item type the container already holds at least one of.</summary>
        private static bool VacuumGroundItemsInto(ZDO containerZdo, Inventory inventory, int containerPrefabHash)
        {
            float radius = WonderlandConfig.VacuumRadius?.Value ?? 10f;
            List<ZDO> nearby = ZdoSpatialQuery.FindNear(containerZdo.GetPosition(), radius);
            bool changed = false;
            float now = Time.time;
            long session = ZDOMan.GetSessionID();

            foreach (ZDO groundZdo in nearby)
            {
                if (!groundZdo.IsValid() || groundZdo.GetPrefab() == containerPrefabHash || _destroyedThisBatch.Contains(groundZdo.m_uid))
                {
                    continue;
                }
                if (!_itemDropPrefabHashes.Contains(groundZdo.GetPrefab()) || IsPlacedOrHatching(groundZdo))
                {
                    continue; // not loot: a live fish (see Initialize), a placed feast, an egg by the fire
                }
                if (_holds.TryGetValue(groundZdo.m_uid, out GroundHold settling) && now < settling.Until)
                {
                    continue; // claimed and still settling - nothing to load for it yet
                }
                GameObject groundPrefab = ZNetScene.instance.GetPrefab(groundZdo.GetPrefab());
                if (groundPrefab == null || IsItemExcluded(groundPrefab.name))
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
                try
                {
                    ItemDrop.LoadFromZDO(groundItem, groundZdo);
                    groundItem.m_dropPrefab = groundPrefab;
                    groundItem.m_shared = dropTemplate.m_itemData.m_shared;
                    if (!ItemSanityGuard.IsPlausible(groundItem, out string rejectReason))
                    {
                        ItemLedger.RecordRejection(VacuumTag, groundItem.m_shared.m_name, groundItem.m_stack, rejectReason);
                        continue;
                    }
                }
                catch (System.Exception ex)
                {
                    // One malformed stack must not keep this chest from ever being vacuumed again.
                    RememberUnwanted(groundZdo.m_uid, now);
                    WarnVacuumRateLimited($"[Vacuum] skipped a {groundPrefab.name} at {groundZdo.GetPosition()}: {ex.GetType().Name}: {ex.Message}");
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
                    RecordFullOnce(containerZdo.m_uid, groundItem.m_shared.m_name, stackOnGround, now);
                    continue;
                }

                if (!TryTakeOwnership(groundZdo, containerZdo, now, session))
                {
                    continue; // claimed just now, or a player got it first
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
                _stacksMoved++;
                _destroyedThisBatch.Add(groundZdo.m_uid);
                _pendingGroundDestroy.Add(groundZdo);
                _recentlyMoved[groundZdo.m_uid] = new MovedStack(containerZdo.m_uid, itemName, stackOnGround, now);
                ItemLedger.RecordTransfer(VacuumTag, groundItem.m_shared.m_name, stackOnGround);
            }

            return changed;
        }

        /// <summary>A full chest beside a pile used to say so for every stack, every pass. Once per chest and
        /// item type per FullLogIntervalSeconds is what an operator needs; the ledger sees the same line.</summary>
        private static void RecordFullOnce(ZDOID container, string itemName, int amount, float now)
        {
            if (now - _fullLoggedClearedAt >= FullLogIntervalSeconds)
            {
                _fullLogged.Clear();
                _fullLoggedClearedAt = now;
            }
            if (_fullLogged.Add((container, itemName)))
            {
                ItemLedger.RecordRejection(VacuumTag, itemName, amount, "container full - left on the ground");
            }
        }

        /// <summary>The settle step (section note above). True when the stack is the server's to move now.
        /// Otherwise it has just been claimed and will be looked at again when the hold matures
        /// (ProcessMaturedHolds calls this container again), or a player has it.</summary>
        private static bool TryTakeOwnership(ZDO groundZdo, ZDO containerZdo, float now, long session)
        {
            ZDOID uid = groundZdo.m_uid;
            bool ours = groundZdo.GetOwner() == session;
            if (_holds.TryGetValue(uid, out GroundHold hold))
            {
                if (now < hold.Until)
                {
                    return false;
                }
                _holds.Remove(uid);
                // Not ours any more: a player asked for it while it settled (or a stale packet from a still
                // sliding stack beat the claim - the next pass claims it again, at rest). Either way, not now.
                return ours;
            }
            if (WaterBuoyancyEngine.HasPickupGrace(uid))
            {
                return false; // a player is picking it up right now
            }
            if (ours)
            {
                // Server-owned means no client can be mid-pickup - once the server's ownership is older than
                // one delivery. A ZDO minted by this server session (a sweep's drop: CreateNewZDO stamps the
                // session id into the ZDOID) never had a client owner. One WaterBuoyancy took from a client
                // within the last hold's worth of time may not have landed on that client yet, so it gets the
                // same settle time without a second claim.
                if (uid.UserID == session || !WaterBuoyancyEngine.ClaimedWithin(uid, HoldSeconds))
                {
                    return true;
                }
                _holds[uid] = new GroundHold(now + HoldSeconds, containerZdo.m_uid);
                return false;
            }

            long previousOwner = groundZdo.GetOwner();

            // At-rest gate: a stack its client still reports moving is left to land first. Claiming it now
            // would pin it on every screen at the server's last-known position - a jump backwards on the
            // way down, then a mid-air freeze for the whole hold. The next pass gets it at rest, where the
            // claim changes nothing anyone can see; VacuumAround does not file it as unwanted meanwhile.
            // Only a connected owner is simulating the stack: a velocity left behind by a peer that
            // disconnected mid-slide, or on an unowned ZDO, is stale and would gate it forever.
            if (previousOwner != 0L && IsConnectedPeer(previousOwner)
                && (groundZdo.GetVec3(ZDOVars.s_velHash, Vector3.zero).sqrMagnitude > AtRestSpeedSqr
                    || groundZdo.GetVec3(ZDOVars.s_bodyVelHash, Vector3.zero).sqrMagnitude > AtRestSpeedSqr))
            {
                _movingThisPass.Add(uid);
                return false;
            }

            groundZdo.SetOwner(session);
            groundZdo.DataRevision += 4096;
            groundZdo.Set(ZDOVars.s_velHash, Vector3.zero);
            groundZdo.Set(ZDOVars.s_bodyVelHash, Vector3.zero);
            groundZdo.Set(ZDOVars.s_bodyAVelHash, Vector3.zero);
            // The previous owner is the one client that must hear now; everyone else picks the new owner up
            // on their next regular send (the revision rose).
            ZDOMan.instance.ForceSendZDO(previousOwner, uid);
            ClaimEcho.Note(uid);
            _holds[uid] = new GroundHold(now + HoldSeconds, containerZdo.m_uid);
            return false;
        }

        /// <summary>Every frame: a hold that matured gets its container one more look right away (not on the
        /// next pass - that would add a whole VacuumInterval to every drop). A stack still held after that
        /// look (chest full or open, item excluded meanwhile, container gone) is handed back to the nearest
        /// player and rests as unwanted for a few seconds, so nothing is ever left frozen.</summary>
        private static void ProcessMaturedHolds()
        {
            if (_holds.Count == 0 || ZDOMan.instance == null)
            {
                return;
            }

            float now = Time.time;
            _holdScratch.Clear();
            foreach (KeyValuePair<ZDOID, GroundHold> entry in _holds)
            {
                if (now >= entry.Value.Until)
                {
                    _holdScratch.Add(entry.Key);
                }
            }
            if (_holdScratch.Count == 0)
            {
                return;
            }

            _holdContainersThisFrame.Clear();
            int budget = MaxHoldContainersPerFrame;
            for (int i = 0; i < _holdScratch.Count; i++)
            {
                ZDOID uid = _holdScratch[i];
                if (!_holds.TryGetValue(uid, out GroundHold hold))
                {
                    continue; // consumed by a container processed earlier in this loop
                }
                ZDO ground = ZDOMan.instance.GetZDO(uid);
                if (ground == null || !ground.IsValid())
                {
                    _holds.Remove(uid); // picked up before the claim reached its client - theirs, and gone
                    continue;
                }
                ZDO container = ZDOMan.instance.GetZDO(hold.Container);
                if (container == null || !container.IsValid())
                {
                    ReleaseHold(uid, ground, now);
                    continue;
                }
                if (budget <= 0)
                {
                    if (now >= hold.Until + HoldHardExpirySeconds)
                    {
                        ReleaseHold(uid, ground, now);
                    }
                    continue; // next frame
                }
                if (_holdContainersThisFrame.Add(hold.Container))
                {
                    budget--;
                    ProcessContainer(container); // VacuumGroundItemsInto consumes every matured hold of this chest
                }
                if (_holds.ContainsKey(uid))
                {
                    ReleaseHold(uid, ground, now);
                }
            }
        }

        private static void ReleaseHold(ZDOID uid, ZDO ground, float now)
        {
            _holds.Remove(uid);
            HandBack(ground);
            RememberUnwanted(uid, now);
        }

        /// <summary>Gives a stack the server claimed back to the nearest connected player (vanilla's own
        /// ReleaseNearbyZDOS would do the same within 2 s; this just does not leave it frozen that long), or
        /// leaves it unowned for that pass to assign when nobody is near.</summary>
        private static void HandBack(ZDO ground)
        {
            if (ground.GetOwner() != ZDOMan.GetSessionID())
            {
                return; // someone already has it
            }
            long to = TryFindNearestPlayer(ground.GetPosition(), 96f, out ConnectedCharacter nearest) ? nearest.Peer.m_uid : 0L;
            // Provide a slight downward velocity: when the client takes back ownership,
            // ZSyncTransform.OwnerSync applies s_bodyVelHash to m_body.linearVelocity,
            // which forces Unity PhysX to wake up the sleeping Rigidbody so gravity pulls it down.
            ground.Set(ZDOVars.s_velHash, new Vector3(0f, -0.1f, 0f));
            ground.Set(ZDOVars.s_bodyVelHash, new Vector3(0f, -0.1f, 0f));
            ground.SetOwner(to);
            if (to != 0L)
            {
                ZDOMan.instance.ForceSendZDO(to, ground.m_uid);
                ClaimEcho.Note(ground.m_uid);
            }
        }

        private static void ReleaseAllHolds()
        {
            if (ZDOMan.instance == null)
            {
                _holds.Clear();
                return;
            }
            _holdScratch.Clear();
            _holdScratch.AddRange(_holds.Keys);
            _holds.Clear();
            for (int i = 0; i < _holdScratch.Count; i++)
            {
                ZDO ground = ZDOMan.instance.GetZDO(_holdScratch[i]);
                if (ground != null && ground.IsValid())
                {
                    HandBack(ground);
                }
            }
        }

        /// <summary>For the ZDO.SetOwner prefix in WaterBuoyancyEngine: while a stack settles, vanilla's
        /// passive ReleaseNearbyZDOS must not hand it to a nearby player (that would reopen the very window
        /// the hold closes, one pass in four). A player who asks for it goes through HandlePickupRequest,
        /// which sets its grace before calling SetOwner, so that path is let through; so is anything once
        /// the hold is stale, so an entry can never wedge an item.</summary>
        public static bool ShouldBlockOwnerChange(ZDO zdo, long targetUid)
        {
            if (zdo == null || (_holds.Count == 0 && _destroyedThisBatch.Count == 0) || targetUid == ZDOMan.GetSessionID())
            {
                return false;
            }
            if (_destroyedThisBatch.Contains(zdo.m_uid))
            {
                // Moved this frame, destroy queued: ReleaseNearbyZDOS must not hand it to a peer in the send
                // that precedes the destroy (ZDOMan.Update sends ZDOs before SendDestroyed).
                return true;
            }
            if (!_holds.TryGetValue(zdo.m_uid, out GroundHold hold) || Time.time >= hold.Until + HoldHardExpirySeconds)
            {
                return false;
            }
            return !WaterBuoyancyEngine.HasPickupGrace(zdo.m_uid);
        }

        /// <summary>True for a ground stack this frame's passes already moved into a container (its DestroyZDO
        /// is queued, not yet sent). HandlePickupRequest refuses to grant such a stack to a player.</summary>
        public static bool WasMovedThisFrame(ZDOID uid)
        {
            return _destroyedThisBatch.Contains(uid);
        }

        /// <summary>True while the settle step holds this stack for a container.</summary>
        public static bool IsHeld(ZDOID uid)
        {
            return _holds.ContainsKey(uid);
        }

        /// <summary>
        /// Called by LatePickupPatch for every "DestroyZDO" batch a client sends. Only an owner can send one,
        /// and after the claim a client is not the owner unless the claim never reached it in time - so a
        /// client's destroy for a stack this engine moved within the last RecentMoveSeconds means that client
        /// committed a pickup (or a stack merge) before it heard: the player has the items, and the container
        /// gives its copy back. The one signal the protocol's timing assumption can be checked against, and
        /// what makes the hold length a latency knob rather than a correctness one.
        /// </summary>
        public static void OnClientDestroy(long sender, ZPackage pkg)
        {
            if (_recentlyMoved.Count == 0 || pkg == null || ZDOMan.instance == null || sender == ZDOMan.GetSessionID())
            {
                return;
            }
            int pos = pkg.GetPos();
            try
            {
                pkg.SetPos(0);
                int count = pkg.ReadInt();
                for (int i = 0; i < count && i < 4096; i++)
                {
                    ZDOID uid = pkg.ReadZDOID();
                    if (_recentlyMoved.TryGetValue(uid, out MovedStack moved))
                    {
                        _recentlyMoved.Remove(uid);
                        TakeBack(moved, sender);
                    }
                }
            }
            finally
            {
                pkg.SetPos(pos);
            }
        }

        private static void TakeBack(MovedStack moved, long sender)
        {
            string who = ZNet.instance?.GetPeer(sender)?.m_playerName ?? $"peer {sender}";
            ZDO container = ZDOMan.instance.GetZDO(moved.Container);
            if (container == null || !container.IsValid() || ZNetScene.instance == null)
            {
                WonderlandDebug.LogWarning($"[Vacuum] '{who}' picked up {moved.Stack}x {moved.ItemName} as a chest took it, and that chest is gone - the copy could not be taken back.");
                return;
            }
            if (ZdoInventoryIO.IsBusy(container))
            {
                _pendingTakeBacks.Add(moved); // someone has it open; retried every frame for TakeBackRetrySeconds
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(container.GetPrefab());
            Container template = ContainerRegistry.ResolveTemplate(prefab);
            if (template == null)
            {
                return;
            }
            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            Inventory inventory = ZdoInventoryIO.Load(container, width, height);
            if (inventory == null)
            {
                _pendingTakeBacks.Add(moved);
                return;
            }
            int take = Mathf.Min(inventory.CountItems(moved.ItemName), moved.Stack);
            if (take > 0)
            {
                inventory.RemoveItem(moved.ItemName, take);
                bool rowsEligible = ContainerRows.IsEnabled && ContainerRows.IsEligible(prefab, template);
                Commit(container, inventory, rowsEligible, rowsEligible ? GridGrowth.GetVanillaSize(prefab.name, template).height : 0, height);
                RememberContainerNames(container, inventory);
            }
            ItemLedger.RecordRejection(VacuumTag, moved.ItemName, take, $"'{who}' picked it up first - taken back out of the chest ({take} of {moved.Stack} still there)");
        }

        private static void ProcessPendingTakeBacks()
        {
            float now = Time.time;
            for (int i = _pendingTakeBacks.Count - 1; i >= 0; i--)
            {
                MovedStack moved = _pendingTakeBacks[i];
                _pendingTakeBacks.RemoveAt(i);
                if (now - moved.At > TakeBackRetrySeconds)
                {
                    WonderlandDebug.LogWarning($"[Vacuum] gave up taking {moved.Stack}x {moved.ItemName} back out of a chest that stayed open for {TakeBackRetrySeconds:0} s - a player kept that stack too.");
                    continue;
                }
                TakeBack(moved, 0L); // re-adds itself while the chest stays busy
            }
        }

        private static void PruneRecentlyMoved(float now)
        {
            if (_recentlyMoved.Count == 0)
            {
                return;
            }
            _movedScratch.Clear();
            foreach (KeyValuePair<ZDOID, MovedStack> entry in _recentlyMoved)
            {
                if (now - entry.Value.At > RecentMoveSeconds)
                {
                    _movedScratch.Add(entry.Key);
                }
            }
            for (int i = 0; i < _movedScratch.Count; i++)
            {
                _recentlyMoved.Remove(_movedScratch[i]);
            }
        }

        private static bool IsContainerExcluded(string prefabName) =>
            ParsedList(WonderlandConfig.VacuumExcludedContainers?.Value, ref _excludedContainersSource, _excludedContainers).Contains(prefabName);
        private static bool IsItemExcluded(string prefabName) =>
            ParsedList(WonderlandConfig.VacuumExcludedItems?.Value, ref _excludedItemsSource, _excludedItems).Contains(prefabName);

        /// <summary>The comma list as a set, re-split only when the config string changes (hot reload hands
        /// out a new string; until then the same instance comes back and the check is a reference compare).
        /// Called per ground item per pass now, so no per-call Split.</summary>
        private static HashSet<string> ParsedList(string list, ref string cachedSource, HashSet<string> cache)
        {
            if (!ReferenceEquals(list, cachedSource) && !string.Equals(list, cachedSource, System.StringComparison.Ordinal))
            {
                cache.Clear();
                if (!string.IsNullOrWhiteSpace(list))
                {
                    foreach (string entry in list.Split(','))
                    {
                        string trimmed = entry.Trim();
                        if (trimmed.Length > 0)
                        {
                            cache.Add(trimmed);
                        }
                    }
                }
                cachedSource = list;
            }
            return cache;
        }

        // === Auto-harvest ===
        //
        // Trigger: the picking client's own broadcast. Pickable.RPC_Pick (1.0.12 server decompile 71024-71051)
        // drops the items on the owner's machine and then sends "RPC_SetPicked" true to Everybody; that is
        // routed through the server, whose ZRoutedRpc.HandleRoutedRPC (83646) drops it for lack of an
        // instance - after HarvestTriggerPatch's prefix has seen it. Until 0.8.2 the trigger was a per-frame
        // poll of ZDOVars.s_picked around each player, which vanilla writes only for pickables that respawn
        // or hide when picked (Pickable.SetPicked 71058-71082, the one write of that key in the assembly);
        // every farm crop is destroyed on pick instead, so a carrot patch never swept - 47 of the 67 Pickable
        // prefabs were unreachable. The RPC fires for all of them, once per pick, and costs one int compare
        // per routed message instead of a nine-sector scan per player per frame.
        //
        // The sweep is deferred half a second and run from OnUpdate, never inside the network handler. A
        // scythe swing picks every plant in reach in one client frame and sends one RPC per plant, while the
        // client's DestroyZDO batch for those plants only leaves on its next ZDOMan.Update (76812-76821) -
        // sweeping at once would harvest, server-side, plants the player had already cut. By the time the
        // deferred sweep runs those ZDOs are gone and every trigger of that swing is in the ledger. Two
        // checks stay in the handler: the server's own RPC_SetPicked broadcasts (HarvestPickable) are skipped
        // by sender id - InvokeRoutedRPC(Everybody) is handled locally and synchronously on the sender
        // (83587-83590), so without this the sweep would re-enter itself without bound - and a trigger only
        // counts when its sender is a connected player standing at the plant, because any client can address
        // this RPC at any ZDOID. Sheets read for this: VALHEIM-DEDICATED-SERVER-FACTS (routed RPC visibility
        // table, "a routed RPC aimed at a ZDO with no local instance is dropped, silently"), HEADLESS-AND-
        // EMPTY-SERVER-FACTS §5 (never drive RPC_Pick from the server).

        private readonly struct HarvestTrigger
        {
            public readonly ZDOID Uid;
            public readonly int PrefabHash;
            public readonly Vector3 Position;
            public readonly long Sender;
            public readonly string PickerName;
            public readonly float DueAt;

            public HarvestTrigger(ZDOID uid, int prefabHash, Vector3 position, long sender, string pickerName, float dueAt)
            {
                Uid = uid;
                PrefabHash = prefabHash;
                Position = position;
                Sender = sender;
                PickerName = pickerName;
                DueAt = dueAt;
            }
        }

        private static readonly List<HarvestTrigger> _pendingTriggers = new List<HarvestTrigger>();
        private static readonly HashSet<ZDOID> _pendingUids = new HashSet<ZDOID>();
        private static readonly List<ZDO> _sweepBuffer = new List<ZDO>();
        private static float _ledgerPruneTimer;
        private static float _lastTriggerWarning = -999f;

        /// <summary>Seconds between a pick and its sweep: long enough for the picking client's own DestroyZDO
        /// batch (its next ZDOMan.Update, one client frame plus the round trip) to land first, short enough
        /// to read as instant.</summary>
        private const float HarvestSweepDelaySeconds = 0.5f;
        /// <summary>Load guard: sweeps run from OnUpdate at most this many per frame; the rest wait a frame.</summary>
        private const int MaxSweepsPerFrame = 4;
        /// <summary>Load guard: plants harvested per sweep pass. Crops grow a metre apart, so an 8 m circle can
        /// hold ~200 of them and the config allows 30 m; every plant is an Instantiate, a ZDO, a broadcast to
        /// every peer and a ledger line. Past this the same trigger is re-queued and the rest of the field
        /// follows a few frames later.</summary>
        private const int MaxHarvestsPerSweep = 40;
        /// <summary>Re-queue delay for a sweep that hit MaxHarvestsPerSweep - the half-second reason is spent,
        /// this only spreads the load.</summary>
        private const float HarvestSweepContinueSeconds = 0.1f;
        /// <summary>Load guard: picks queued per player. A scythe swing is a handful; more than this inside
        /// half a second is not a player.</summary>
        private const int MaxPendingTriggersPerPeer = 16;
        private const int MaxPendingTriggers = 128;
        /// <summary>How far from their character ZDO a player's pick may be and still count. Interact range is
        /// 5 m; the rest is headroom for the character ZDO lagging a sprinting player.</summary>
        private const float MaxPickReachMeters = 16f;
        private const float TriggerWarningIntervalSeconds = 30f;
        private const float LedgerPruneIntervalSeconds = 60f;

        /// <summary>
        /// Called by HarvestTriggerPatch for every routed "RPC_SetPicked" the server handles. Validates the
        /// message, ledgers the picked plant and queues the sweep; never sweeps inline (see the section note).
        /// </summary>
        public static void OnPickedRpc(ZRoutedRpc.RoutedRPCData data)
        {
            if (WonderlandConfig.AutoHarvestEnabled?.Value != true)
            {
                return;
            }
            if (ZDOMan.instance == null || ZNetScene.instance == null || ZNet.instance == null)
            {
                return;
            }
            if (data.m_senderPeerID == ZDOMan.GetSessionID())
            {
                // Our own HarvestPickable broadcast - the recursion guard. Dedicated-only by design: on a
                // listen server this id would also be the host's own picks, and Wonderland never runs there.
                return;
            }
            if (!ReadPickedFlag(data.m_parameters, out bool picked) || !picked)
            {
                return; // RPC_SetPicked(false) is a bush respawning (Pickable.UpdateRespawn)
            }

            ZDO zdo = ZDOMan.instance.GetZDO(data.m_targetZDO);
            if (zdo == null || !zdo.IsValid() || _pendingUids.Contains(zdo.m_uid))
            {
                return;
            }

            int prefabHash = zdo.GetPrefab();
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
            Pickable template = prefab != null ? prefab.GetComponent<Pickable>() : null;
            if (template == null || !IsSweepable(template) || IsPickableExcluded(prefab, template))
            {
                return;
            }

            Vector3 position = zdo.GetPosition();
            // Anti-forgery: the sender must be a connected player at all. It is the plant ZDO's OWNER, which is
            // not necessarily who picked it - Pickable.Interact routes RPC_Pick to m_zdo.GetOwner() (82855-82857)
            // and only the owner broadcasts RPC_SetPicked, while ReleaseNearbyZDOS leaves a ZDO with whichever
            // player owns it anywhere inside their ~96 m active area. On a shared base that is routinely someone
            // else, so reach and credit go to whoever is actually standing at the plant.
            if (!IsConnectedPeer(data.m_senderPeerID))
            {
                WarnRateLimited($"[AutoHarvest] ignored a pick of {prefab.name} reported by peer {data.m_senderPeerID}: not a connected player.");
                return;
            }
            if (!TryFindNearestPlayer(position, MaxPickReachMeters, out ConnectedCharacter picker))
            {
                WarnRateLimited($"[AutoHarvest] ignored a pick of {prefab.name}: no connected player within {MaxPickReachMeters:0} m of it.");
                return;
            }

            // The trigger itself is harvested - the picker's client dropped its items - so no sweep queued
            // behind it may take it a second time.
            MarkHarvested(zdo.m_uid);

            if (_pendingTriggers.Count >= MaxPendingTriggers)
            {
                WarnRateLimited($"[AutoHarvest] {_pendingTriggers.Count} picks are already waiting to sweep - '{picker.Name}' picked {prefab.name} and it gets no bonus.");
                return;
            }
            if (CountPendingFor(data.m_senderPeerID) >= MaxPendingTriggersPerPeer)
            {
                // Normal for one scythe swing (up to 200 plants in one client frame - Attack's piece-collider
                // buffer): the triggers already queued cover the same radius, so this one is redundant, not
                // lost, and not worth a warning.
                return;
            }

            _pendingTriggers.Add(new HarvestTrigger(zdo.m_uid, prefabHash, position, data.m_senderPeerID, picker.Name, Time.time + HarvestSweepDelaySeconds));
            _pendingUids.Add(zdo.m_uid);
        }

        /// <summary>The one bool RPC_SetPicked carries. Read from offset 0 (the network path constructs the
        /// package at 0, the local path SetPos(0)s it) and put the cursor back, since vanilla may still read
        /// the buffer after the prefix.</summary>
        private static bool ReadPickedFlag(ZPackage parameters, out bool picked)
        {
            picked = false;
            if (parameters == null || parameters.Size() < 1)
            {
                return false;
            }
            int pos = parameters.GetPos();
            try
            {
                parameters.SetPos(0);
                picked = parameters.ReadBool();
                return true;
            }
            finally
            {
                parameters.SetPos(pos);
            }
        }

        /// <summary>
        /// What a pick may sweep. Pickables that keep their ZDO (respawn or hide when picked - bushes,
        /// mushrooms, branches, flint, core stands) are the set that swept before 0.8.2 and stay in. Of the
        /// ones vanilla destroys on pick, only the harvestable ones - the scythe's own list: carrot, turnip,
        /// onion, barley, flax, the three seed plants, magecap, jotun puffs - are swept; ores, tar, dungeon
        /// loot and crypt remains say m_harvestable = false and are left alone (their value often sits in
        /// m_extraDrops or behind a pit that has to be drained first). Anything the game refuses to pick
        /// while it floats in tar is never swept: that check is client physics the server cannot make.
        /// </summary>
        private static bool IsSweepable(Pickable template)
        {
            if (template.m_tarPreventsPicking)
            {
                return false;
            }
            bool keepsZdo = template.m_respawnTimeMinutes > 0f || template.m_hideWhenPicked != null;
            return keepsZdo || template.m_harvestable;
        }

        /// <summary>VacuumExcludedItems matches either name: the plant (Pickable_Carrot) or what it drops
        /// (Carrot), which is what the vacuum side of the same list already matches on.</summary>
        private static bool IsPickableExcluded(GameObject prefab, Pickable template)
        {
            if (IsItemExcluded(prefab.name))
            {
                return true;
            }
            return template.m_itemPrefab != null && IsItemExcluded(template.m_itemPrefab.name);
        }

        private static bool IsConnectedPeer(long peerId)
        {
            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                if (character.Peer.m_uid == peerId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindNearestPlayer(Vector3 position, float maxMeters, out ConnectedCharacter nearest)
        {
            nearest = default;
            float bestSqr = maxMeters * maxMeters;
            bool found = false;
            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                float sqr = (character.Position - position).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    nearest = character;
                    found = true;
                }
            }
            return found;
        }

        private static int CountPendingFor(long sender)
        {
            int count = 0;
            for (int i = 0; i < _pendingTriggers.Count; i++)
            {
                if (_pendingTriggers[i].Sender == sender)
                {
                    count++;
                }
            }
            return count;
        }

        private static void WarnRateLimited(string message)
        {
            if (Time.time - _lastTriggerWarning < TriggerWarningIntervalSeconds)
            {
                return;
            }
            _lastTriggerWarning = Time.time;
            WonderlandDebug.LogWarning(message);
        }

        private static void ProcessPendingHarvests()
        {
            if (_pendingTriggers.Count == 0 || ZNetScene.instance == null)
            {
                return;
            }

            float now = Time.time;
            int budget = MaxSweepsPerFrame;
            int i = 0;
            while (i < _pendingTriggers.Count && budget > 0)
            {
                HarvestTrigger trigger = _pendingTriggers[i];
                if (trigger.DueAt > now)
                {
                    i++;
                    continue;
                }
                _pendingTriggers.RemoveAt(i);
                _pendingUids.Remove(trigger.Uid);
                budget--;

                GameObject prefab = ZNetScene.instance.GetPrefab(trigger.PrefabHash);
                Pickable template = prefab != null ? prefab.GetComponent<Pickable>() : null;
                if (template == null)
                {
                    continue;
                }
                float radius = WonderlandConfig.AutoHarvestRadius?.Value ?? 8f;
                int swept;
                try
                {
                    swept = SweepBonusHarvest(trigger.Uid, trigger.PrefabHash, prefab, template, trigger.Position, radius);
                }
                catch (System.Exception ex)
                {
                    // One bad drop table must not take the rest of ItemFlow's frame down with it.
                    WarnRateLimited($"[AutoHarvest] sweep of {prefab.name} failed: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (swept > 0)
                {
                    int fed = 0;
                    if (WonderlandConfig.VacuumEnabled?.Value == true)
                    {
                        // The drops exist as ZDOs the moment DropItem returns, so a matching chest in range
                        // takes them now rather than on the next pass - "one keypress, the chest fills".
                        // Born on the server, they skip the settle step (no client has ever owned them).
                        int movedBefore = _stacksMoved;
                        BeginVacuumPass();
                        VacuumAround(trigger.Position, radius, WonderlandConfig.VacuumRadius?.Value ?? 10f);
                        fed = _stacksMoved - movedBefore;
                    }
                    string chest = fed > 0 ? $", {fed} stack{(fed == 1 ? "" : "s")} into a chest" : "";
                    WonderlandDebug.LogInfo($"[AutoHarvest] {prefab.name} picked by '{trigger.PickerName}' - swept {swept} more within {radius:0.#} m{chest}.");
                }
                if (swept >= MaxHarvestsPerSweep && _pendingTriggers.Count < MaxPendingTriggers)
                {
                    // A dense field: the rest follows a few frames later rather than all in this one. The
                    // re-queued entry is due later than now, so this pass skips it.
                    _pendingTriggers.Add(new HarvestTrigger(trigger.Uid, trigger.PrefabHash, trigger.Position, trigger.Sender, trigger.PickerName, now + HarvestSweepContinueSeconds));
                    _pendingUids.Add(trigger.Uid);
                }
            }
        }

        /// <summary>Everything of the trigger's type within the radius that is still standing, skipping
        /// the trigger itself (its ZDO may already be gone), anything already picked and anything the
        /// ledger says was swept within its respawn time. Returns how many were harvested.</summary>
        private static int SweepBonusHarvest(ZDOID triggerUid, int prefabHash, GameObject prefab, Pickable template, Vector3 center, float radius)
        {
            if (IsPickableExcluded(prefab, template))
            {
                return 0; // the list may have been hot-edited between the pick and the sweep
            }

            List<ZDO> nearby = ZdoSpatialQuery.FindNear(center, radius, _sweepBuffer);
            int swept = 0;
            foreach (ZDO zdo in nearby)
            {
                if (swept >= MaxHarvestsPerSweep)
                {
                    break; // the caller re-queues this trigger for the remainder
                }
                if (zdo.m_uid == triggerUid || zdo.GetPrefab() != prefabHash)
                {
                    continue;
                }
                if (zdo.GetBool(ZDOVars.s_picked) || WasHarvestedRecently(zdo.m_uid, template))
                {
                    continue;
                }
                if (HarvestPickable(zdo, prefab, template))
                {
                    swept++;
                }
            }
            return swept;
        }

        private static void MarkHarvested(ZDOID uid)
        {
            if (ZNet.instance != null)
            {
                _harvestedAt[uid] = ZNet.instance.GetTime().Ticks;
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
        /// the same bush every time the flag flips back, spawning items forever. Plants that vanilla
        /// destroys on pick stay in it until PruneHarvestLedger sees their ZDO is gone.
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
        /// Once a minute, and only once the ledger is large: drop entries whose ZDO no longer exists (every
        /// swept crop, once its destroy has gone through) and entries older than a day of game time, which
        /// is past any vanilla respawn. Aged out rather than cleared: dropping a still-fresh entry would
        /// reopen the very re-harvest window the ledger exists to close.
        /// </summary>
        private static void PruneHarvestLedger()
        {
            if (_harvestedAt.Count <= MaxTrackedPickables || ZNet.instance == null || ZDOMan.instance == null)
            {
                return;
            }

            // Ticks arithmetic, not AddDays(-1): a fresh world's clock starts ~34 minutes past DateTime.MinValue
            // (ZNet.m_netTime = 2040 s) and AddDays(-1) throws until it has run a full day.
            long nowTicks = ZNet.instance.GetTime().Ticks;
            long cutoff = nowTicks > System.TimeSpan.TicksPerDay ? nowTicks - System.TimeSpan.TicksPerDay : 0L;
            var stale = new List<ZDOID>();
            foreach (KeyValuePair<ZDOID, long> entry in _harvestedAt)
            {
                if (entry.Value < cutoff || ZDOMan.instance.GetZDO(entry.Key) == null)
                {
                    stale.Add(entry.Key);
                }
            }
            foreach (ZDOID id in stale)
            {
                _harvestedAt.Remove(id);
            }
            if (stale.Count > 0)
            {
                WonderlandDebug.LogInfo($"[AutoHarvest] ledger pruned: {stale.Count} finished entr{(stale.Count == 1 ? "y" : "ies")} dropped, {_harvestedAt.Count} kept.");
            }
        }

        /// <summary>
        /// Mirrors Pickable.RPC_Pick's essentials (scaled item, then the extra-drop table - magecap and jotun
        /// puffs carry two more of themselves there) without the parts that only matter with a live GameObject
        /// watching (spawn effects, aggro range) or that live on the picking client (the Farming-skill bonus
        /// yield, which the server cannot roll) - a documented scope trim, not a bug. The triggering pick
        /// itself already ran for real on the harvesting player's own client with full vanilla behavior;
        /// this only covers the "swept in" bonus. Order matters: ledger and ZDO state first, the broadcast
        /// last, so nothing that reacts to the broadcast can find this one unharvested.
        /// </summary>
        private static bool HarvestPickable(ZDO zdo, GameObject prefab, Pickable template)
        {
            if (template == null || template.m_itemPrefab == null)
            {
                return false;
            }

            ItemDrop dropTemplate = template.m_itemPrefab.GetComponent<ItemDrop>();
            if (dropTemplate == null || dropTemplate.m_itemData?.m_shared == null || Game.instance == null)
            {
                return false;
            }

            int amount = template.m_dontScale
                ? template.m_amount
                : Mathf.Max(template.m_minAmountScaled, Game.instance.ScaleDrops(template.m_itemPrefab, template.m_amount));
            if (amount <= 0)
            {
                return false;
            }

            MarkHarvested(zdo.m_uid);
            zdo.SetOwner(ZDOMan.GetSessionID());
            bool keepsZdo = template.m_respawnTimeMinutes > 0f || template.m_hideWhenPicked != null;
            if (keepsZdo)
            {
                zdo.Set(ZDOVars.s_picked, true);
                if (ZNet.instance != null)
                {
                    zdo.Set(ZDOVars.s_pickedTime, ZNet.instance.GetTime().Ticks);
                }
            }

            // A prefab's template ItemData only gets m_dropPrefab from ItemDrop.Awake, which never runs
            // for the prefab itself - and ItemDrop.DropItem instantiates from exactly that field.
            Vector3 dropPosition = zdo.GetPosition() + Vector3.up * 0.3f;
            ItemDrop.ItemData itemData = dropTemplate.m_itemData.Clone();
            itemData.m_dropPrefab = template.m_itemPrefab;
            itemData.m_worldLevel = (byte)Game.m_worldLevel; // what ItemDrop.OnCreateNew stamps on a vanilla pick; DropItem does not
            ItemDrop.DropItem(itemData, amount, dropPosition, Quaternion.identity);
            ItemLedger.RecordTransfer(HarvestTag, itemData.m_shared.m_name, amount);

            if (!template.m_extraDrops.IsEmpty())
            {
                // DropTable.GetDropListItems is pure roll-and-clone (it sets m_dropPrefab itself) and hands
                // back a shared static list, so the roll is copied out before anything else can touch it.
                var extras = new List<ItemDrop.ItemData>(template.m_extraDrops.GetDropListItems());
                foreach (ItemDrop.ItemData extra in extras)
                {
                    if (extra?.m_dropPrefab == null || extra.m_shared == null || extra.m_stack <= 0)
                    {
                        continue;
                    }
                    ItemDrop.DropItem(extra, extra.m_stack, dropPosition + Vector3.up * 0.2f, Quaternion.identity);
                    ItemLedger.RecordTransfer(HarvestTag, extra.m_shared.m_name, extra.m_stack);
                }
            }

            // Vanilla's own RPC_SetPicked to everyone: clients that have the plant loaded set m_picked and
            // hide the berries / model at once. The server's copy of this broadcast comes straight back
            // through HandleRoutedRPC and is skipped there by sender id.
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZNetView.Everybody, zdo.m_uid, "RPC_SetPicked", true);

            if (!keepsZdo)
            {
                // What vanilla does for a crop: the ZDO goes (DestroyZDO queues it, SendDestroyed sends it
                // later this frame). Every loaded instance is destroyed by that message on every client.
                ZDOMan.instance.DestroyZDO(zdo);
            }
            return true;
        }
    }
}
