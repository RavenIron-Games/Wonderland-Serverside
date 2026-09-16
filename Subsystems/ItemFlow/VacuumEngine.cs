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
    ///    chunks per pass gave any one chest its turn about every half minute.
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

        private static ZdoSpatialQuery.PrefabSetScanner _containerScanner;
        private static float _vacuumTimer;
        private static readonly List<ZDO> _scanBuffer = new List<ZDO>();
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

        public static void Initialize()
        {
            _containerScanner = new ZdoSpatialQuery.PrefabSetScanner(ContainerRegistry.PrefabNames);

            _containerPrefabHashes.Clear();
            foreach (string name in ContainerRegistry.PrefabNames)
            {
                _containerPrefabHashes.Add(name.GetStableHashCode());
            }

            // Every prefab that is a dropped item, hashed the way ZDO.GetPrefab reports it (ZNetScene keys
            // its prefab table by name.GetStableHashCode). Built here rather than borrowed from
            // WaterBuoyancyEngine so the vacuum does not depend on a sibling feature's init order.
            _itemDropPrefabHashes.Clear();
            if (ZNetScene.instance != null)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab != null && prefab.GetComponent<ItemDrop>() != null)
                    {
                        _itemDropPrefabHashes.Add(prefab.name.GetStableHashCode());
                    }
                }
            }
        }

        public static void OnUpdate(float dt)
        {
            if (_containerScanner == null)
            {
                return;
            }

            // Ground stacks moved last frame are gone from the sector index by now (their DestroyZDO went
            // out in ZDOMan.Update), so the "already moved" set starts empty each frame.
            _destroyedThisBatch.Clear();

            if (WonderlandConfig.VacuumEnabled?.Value == true)
            {
                _vacuumTimer += dt;
                if (_vacuumTimer >= (WonderlandConfig.VacuumInterval?.Value ?? 2f))
                {
                    _vacuumTimer = 0f;
                    BeginVacuumPass();
                    ProcessVacuumBatch();
                    ProcessVacuumNearPlayers();
                }
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
            }
        }

        /// <summary>One pass = one clear of the visited-container set, so a pass never loads the same chest
        /// twice. _destroyedThisBatch (ground stacks already moved and queued for destruction) is NOT
        /// cleared here but once per frame in OnUpdate: a queued DestroyZDO only leaves the sector index
        /// in ZDOMan.Update, so a sweep's instant vacuum running later in the same frame would otherwise
        /// see a stack the regular pass had just moved and move it a second time.</summary>
        private static void BeginVacuumPass()
        {
            _visitedThisPass.Clear();
        }

        /// <summary>The world-wide background round-robin: VacuumBatchSize chunks of the container-type
        /// scanner per pass. Covers chests nobody is standing near (and carries the overflow guard and
        /// cache drain to them); latency here scales with world size by design.</summary>
        private static void ProcessVacuumBatch()
        {
            _scanBuffer.Clear();
            int budget = Mathf.Max(1, WonderlandConfig.VacuumBatchSize?.Value ?? 25);
            for (int i = 0; i < budget; i++)
            {
                _containerScanner.Advance(_scanBuffer);
            }

            foreach (ZDO containerZdo in _scanBuffer)
            {
                if (_visitedThisPass.Add(containerZdo.m_uid))
                {
                    ProcessContainer(containerZdo);
                }
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

        /// <summary>Loads and processes every container within <paramref name="vacuumRadius"/> of at least
        /// one ground item that lies within <paramref name="reach"/> of <paramref name="center"/>. Returns how
        /// many containers were processed. Ground items first: no loose item, no container load.</summary>
        private static int VacuumAround(Vector3 center, float reach, float vacuumRadius)
        {
            if (ZNetScene.instance == null)
            {
                return 0;
            }

            _groundItems.Clear();
            foreach (ZDO zdo in ZdoSpatialQuery.FindNear(center, reach, _groundBuffer))
            {
                if (_itemDropPrefabHashes.Contains(zdo.GetPrefab()) && !_destroyedThisBatch.Contains(zdo.m_uid))
                {
                    _groundItems.Add(zdo);
                }
            }
            if (_groundItems.Count == 0)
            {
                return 0;
            }

            float radiusSqr = vacuumRadius * vacuumRadius;
            int processed = 0;
            foreach (ZDO containerZdo in ZdoSpatialQuery.FindNear(center, reach + vacuumRadius, _nearBuffer))
            {
                if (!_containerPrefabHashes.Contains(containerZdo.GetPrefab()) || _visitedThisPass.Contains(containerZdo.m_uid))
                {
                    continue;
                }
                Vector3 position = containerZdo.GetPosition();
                bool hasLooseItemNearby = false;
                for (int i = 0; i < _groundItems.Count; i++)
                {
                    if ((_groundItems[i].GetPosition() - position).sqrMagnitude <= radiusSqr)
                    {
                        hasLooseItemNearby = true;
                        break;
                    }
                }
                if (!hasLooseItemNearby)
                {
                    continue;
                }
                _visitedThisPass.Add(containerZdo.m_uid);
                ProcessContainer(containerZdo);
                processed++;
            }
            return processed;
        }

        private static void ProcessContainer(ZDO containerZdo)
        {
            if (!containerZdo.IsValid() || ZdoInventoryIO.IsBusy(containerZdo))
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

            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            Inventory inventory = ZdoInventoryIO.Load(containerZdo, width, height);
            if (inventory == null)
            {
                return;
            }

            bool changed = false;

            // Overflow guard rides this same pass - every container Wonderland already has open gets
            // checked, on a short regular interval, well before any real client could load it.
            float linkRadius = WonderlandConfig.ContainerLinkRadius?.Value ?? 10f;
            if (GridGrowth.EnforceOverflow(containerZdo, inventory, prefab, template, linkRadius, out bool siblingChanged, out ZDO siblingZdo))
            {
                changed = true;
                if (siblingChanged && siblingZdo != null)
                {
                    // sibling's own inventory object was already saved inside EnforceOverflow
                }
            }

            // Try draining any previously overflowed items from ItemCache into this container
            changed |= ItemCache.TryDrainInto(containerZdo, inventory);

            bool vacuumed = false;
            if (inventory.NrOfItems() > 0)
            {
                vacuumed = VacuumGroundItemsInto(containerZdo, inventory, prefabHash);
                changed |= vacuumed;
            }

            if (changed && ContainerRows.IsEnabled && ContainerRows.IsEligible(prefab, template))
            {
                // Already committing this chest - make the same write leave its grown rows visible.
                ContainerRows.EnsureAnchor(inventory, GridGrowth.GetVanillaSize(prefabName, template).height, height, out _);
            }

            if (changed)
            {
                ZdoInventoryIO.Save(containerZdo, inventory);
            }

            if (vacuumed)
            {
                PlayVacuumEffect(containerZdo.GetPosition());
            }

            FlushPendingGroundDestroy();
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

        /// <summary>Match-required: only tops up an item type the container already holds at least one of.</summary>
        private static bool VacuumGroundItemsInto(ZDO containerZdo, Inventory inventory, int containerPrefabHash)
        {
            float radius = WonderlandConfig.VacuumRadius?.Value ?? 10f;
            List<ZDO> nearby = ZdoSpatialQuery.FindNear(containerZdo.GetPosition(), radius);
            bool changed = false;

            foreach (ZDO groundZdo in nearby)
            {
                if (!groundZdo.IsValid() || groundZdo.GetPrefab() == containerPrefabHash || _destroyedThisBatch.Contains(groundZdo.m_uid))
                {
                    continue;
                }
                GameObject groundPrefab = ZNetScene.instance.GetPrefab(groundZdo.GetPrefab());
                if (groundPrefab == null || groundPrefab.GetComponent<ItemDrop>() == null)
                {
                    continue;
                }
                if (IsItemExcluded(groundPrefab.name))
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
                ItemDrop.LoadFromZDO(groundItem, groundZdo);
                groundItem.m_dropPrefab = groundPrefab;
                groundItem.m_shared = dropTemplate.m_itemData.m_shared;

                if (!ItemSanityGuard.IsPlausible(groundItem, out string rejectReason))
                {
                    ItemLedger.RecordRejection(VacuumTag, groundItem.m_shared.m_name, groundItem.m_stack, rejectReason);
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
                    ItemLedger.RecordRejection(VacuumTag, groundItem.m_shared.m_name, stackOnGround, "container full - left on the ground");
                    continue;
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
                _destroyedThisBatch.Add(groundZdo.m_uid);
                _pendingGroundDestroy.Add(groundZdo);
                ItemLedger.RecordTransfer(VacuumTag, groundItem.m_shared.m_name, stackOnGround);
            }

            return changed;
        }

        private static bool IsContainerExcluded(string prefabName) => MatchesList(WonderlandConfig.VacuumExcludedContainers?.Value, prefabName);
        private static bool IsItemExcluded(string prefabName) => MatchesList(WonderlandConfig.VacuumExcludedItems?.Value, prefabName);

        private static bool MatchesList(string list, string prefabName)
        {
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
            string list = WonderlandConfig.VacuumExcludedItems?.Value;
            if (MatchesList(list, prefab.name))
            {
                return true;
            }
            return template.m_itemPrefab != null && MatchesList(list, template.m_itemPrefab.name);
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
                    WonderlandDebug.LogInfo($"[AutoHarvest] {prefab.name} picked by '{trigger.PickerName}' - swept {swept} more within {radius:0} m.");
                    if (WonderlandConfig.VacuumEnabled?.Value == true)
                    {
                        // The drops exist as ZDOs the moment DropItem returns, so a matching chest in range
                        // takes them now rather than on the next pass - "one keypress, the chest fills".
                        BeginVacuumPass();
                        VacuumAround(trigger.Position, radius, WonderlandConfig.VacuumRadius?.Value ?? 10f);
                    }
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
