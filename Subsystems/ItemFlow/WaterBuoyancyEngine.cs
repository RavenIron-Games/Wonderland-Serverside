using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// Forces all dropped items (ores, metals, weapons, armor, serpent scales, trophies) to float on water,
    /// strictly server-side.
    ///
    /// Background & Valheim 1.0 Decompile Audit:
    /// In vanilla Valheim, only certain prefabs (Wood, Fish, TombStone) have the Floating MonoBehaviour.
    /// Vanilla clients run local physics on any item they own. Because heavy items lack Floating in the client's
    /// local files, a client owning an item drops it to the ocean floor and sends sunken coordinates via ZSyncTransform.
    ///
    /// The Server-Side Solution:
    /// 1. In OnWorldReady, all ItemDrop prefabs in ZNetScene are indexed. Floating is also attached server-side
    ///    to any prefabs missing it.
    /// 2. Active item ZDOs submerged in water (or resting on the seabed) are claimed by the server
    ///    (zdo.SetOwner(serverSession)) and their elevation is lifted to the water surface (pos.y = liquidLevel + offset)
    ///    with DataRevision += 4096 and zeroed velocity.
    /// 3. Non-owner vanilla clients receive the position via ZSyncTransform. In Valheim 1.0, non-owner clients
    ///    automatically disable gravity (m_body.useGravity = false; m_body.Sleep()) and lock the item's transform
    ///    to zdo.GetPosition(), rendering it floating smoothly on the water surface with zero client mods!
    /// 4. ZDO.SetOwner is guarded so ZDOMan.ReleaseNearbyZDOS does not passively re-assign floating items to nearby
    ///    clients (which would trigger client-side gravity and cause them to sink).
    /// 5. When a player presses E or enters auto-pickup range, the client sends "RPC_RequestOwn". ZRoutedRpc.HandleRoutedRPC
    ///    is intercepted to immediately grant ownership to the requesting player for 3 seconds so they execute
    ///    Pickup() into inventory.
    /// </summary>
    public static class WaterBuoyancyEngine
    {
        private static readonly HashSet<int> _itemDropPrefabHashes = new HashSet<int>();
        private static readonly Dictionary<ZDOID, float> _recentPickups = new Dictionary<ZDOID, float>();
        /// <summary>Who was last granted which item, and when: a peer that asks again for a stack it was
        /// granted and did not take (full inventory, over weight) is refused while the vacuum holds it.</summary>
        private static readonly Dictionary<ZDOID, (long peer, float at)> _grants = new Dictionary<ZDOID, (long, float)>();
        /// <summary>Grants that did not turn into a pickup: the sweep found the item still there, still that
        /// peer's, once the grace was over. Two in a row and that peer is refused for FailedGrantCooldownSeconds
        /// - the stack stays floating instead of sinking on every request from someone who cannot take it.</summary>
        private static readonly Dictionary<ZDOID, (long peer, int failures, float at)> _failedGrants = new Dictionary<ZDOID, (long, int, float)>();
        /// <summary>When this engine last took a waterborne item from a client, for VacuumEngine's fast path.</summary>
        private static readonly Dictionary<ZDOID, float> _claimedAt = new Dictionary<ZDOID, float>();
        private static readonly List<ZDOID> _purgeScratch = new List<ZDOID>();
        private const float GrantMemorySeconds = 60f;
        private const float ClaimMemorySeconds = 10f;
        /// <summary>How long a grant keeps the sweep (and the vacuum) off a stack. A pickup needs one ZDO
        /// delivery, the auto-pickup pull (15 m/s over at most 2 m) and the DestroyZDO back - well under a
        /// second on a bad day. 0.10.4 raised this to 15 s; a client's copy has no Floating, so for every one
        /// of those seconds a stack the player could not take sank in front of them, then jumped back to
        /// the surface when the sweep reclaimed it. Three seconds is the 0.8.4 value the settle step was
        /// reviewed against.</summary>
        private const float PickupGraceSeconds = 3f;
        private const int FailedGrantsBeforeCooldown = 2;
        private const float FailedGrantCooldownSeconds = 20f;
        private static readonly HashSet<ZDOID> _sweptThisTick = new HashSet<ZDOID>();
        private static readonly List<ZDO> _scratch = new List<ZDO>();
        private static float _timer;

        public static void Initialize(Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(ZdoSetOwnerPatch));
            SubsystemRegistry.SafePatch(harmony, typeof(RoutedRpcHandlerPatch));
            SubsystemRegistry.SafePatch(harmony, typeof(RoutedRpcRoutePatch));
        }

        public static void OnWorldReady()
        {
            _itemDropPrefabHashes.Clear();
            int addedFloatingCount = 0;

            if (ZNetScene.instance != null)
            {
                foreach (KeyValuePair<int, GameObject> kvp in ZNetScene.instance.m_namedPrefabs)
                {
                    if (kvp.Value == null || kvp.Value.GetComponent<ItemDrop>() == null)
                    {
                        continue;
                    }

                    // Live Fish creatures carry an ItemDrop on the same prefab (used for direct-pickup fish
                    // and to remember quality/variant), but they are not dropped loot: Fish.Update() drives
                    // its own Rigidbody every tick (swimming, wave bobbing, hooked/escape physics). Sweeping
                    // them here would steal ZDO ownership and freeze them at a fixed surface height, killing
                    // their swim AI. Vanilla already gives Fish a native Floating component, so skip them.
                    if (kvp.Value.GetComponent<Fish>() != null)
                    {
                        continue;
                    }

                    _itemDropPrefabHashes.Add(kvp.Key);
                    if (kvp.Value.GetComponent<Floating>() == null)
                    {
                        Floating f = kvp.Value.AddComponent<Floating>();
                        f.m_waterLevelOffset = 0.15f;
                        f.m_forceDistance = 0.5f;
                        f.m_force = 0.5f;
                        f.m_damping = 0.05f;
                        addedFloatingCount++;
                    }
                }
            }

            if (ObjectDB.instance != null)
            {
                foreach (GameObject itemPrefab in ObjectDB.instance.m_items)
                {
                    if (itemPrefab == null) continue;
                    ItemDrop drop = itemPrefab.GetComponent<ItemDrop>();
                    if (drop == null) continue;

                    _itemDropPrefabHashes.Add(itemPrefab.name.GetStableHashCode());

                    if (itemPrefab.GetComponent<Floating>() == null)
                    {
                        Floating f = itemPrefab.AddComponent<Floating>();
                        f.m_waterLevelOffset = 0.15f;
                        f.m_forceDistance = 0.5f;
                        f.m_force = 0.5f;
                        f.m_damping = 0.05f;
                        addedFloatingCount++;
                    }
                }
            }

            WonderlandDebug.LogAlways($"[WaterBuoyancy] Tracked {_itemDropPrefabHashes.Count} item prefab(s), enhanced {addedFloatingCount} server-side prefab(s) with buoyancy.");
        }

        public static void OnUpdate(float dt)
        {
            _timer += dt;
            float interval = WonderlandConfig.FloatSweepInterval?.Value ?? 0.3f;
            if (_timer < interval)
            {
                return;
            }
            _timer = 0f;

            // The pickup grace and grant memory serve the vacuum too, so they are kept tidy whether or not
            // items float.
            PurgeStalePickupAllowances();
            if (WonderlandConfig.AllItemsFloatEnabled?.Value != true || ZDOMan.instance == null || ZNet.instance == null)
            {
                return;
            }

            _sweptThisTick.Clear();

            float surfaceOffset = WonderlandConfig.FloatSurfaceOffset?.Value ?? -0.25f;
            long serverSession = ZDOMan.GetSessionID();

            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                _scratch.Clear();
                ZdoSpatialQuery.FindNear(character.Position, 128f, _scratch);
                for (int i = 0; i < _scratch.Count; i++)
                {
                    ZDO zdo = _scratch[i];
                    if (!zdo.IsValid()) continue;
                    if (!_itemDropPrefabHashes.Contains(zdo.GetPrefab())) continue;

                    // Avoid duplicate processing across overlapping player sectors
                    if (!_sweptThisTick.Add(zdo.m_uid)) continue;

                    // Skip items that a player is currently picking up
                    if (_recentPickups.TryGetValue(zdo.m_uid, out float pickupTime) && Time.time < pickupTime)
                    {
                        continue;
                    }

                    Vector3 pos = zdo.GetPosition();
                    bool isServerOwner = zdo.IsOwner();

                    // No "player nearby" hold-off here (0.10.4 had one, 5 m): a client only owns a waterborne
                    // item because it dropped it or was granted it, and a grant carries its own grace above.
                    // Holding off while a player stands beside a sunk stack meant the stack sank as they
                    // approached and rose again when they walked away - the opposite of what they came for.

                    if (IsWaterborneItem(zdo, out float targetY, surfaceOffset))
                    {
                        // Only a stack that is actually SINKING is lifted (0.10.11). Up to 0.10.10 a client-owned
                        // stack was claimed even at the surface, and one above it (a raft deck, a shoreline) was
                        // pulled DOWN to the surface line - 325 of 547 lifts in one evening were of stacks at or
                        // above the surface, each a fresh ownership fight with the client beside it and a fresh
                        // window for the ghost ClaimEcho cleans up. A stack at or above the surface is left with
                        // whoever owns it; a client that sinks it gets it lifted on the next tick.
                        if (pos.y < targetY - 0.05f)
                        {
                            float wasY = pos.y;
                            pos.y = targetY;
                            if (!isServerOwner)
                            {
                                _claimedAt[zdo.m_uid] = Time.time;
                                NoteGrantOutcome(zdo);
                            }
                            zdo.SetOwner(serverSession);
                            zdo.SetPosition(pos);
                            zdo.DataRevision += 4096;
                            zdo.Set(ZDOVars.s_velHash, Vector3.zero);
                            zdo.Set(ZDOVars.s_bodyVelHash, Vector3.zero);
                            zdo.Set(ZDOVars.s_bodyAVelHash, Vector3.zero);
                            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                            ClaimEcho.Note(zdo.m_uid);
                            WonderlandDebug.LogAlways($"[WaterBuoyancy] Lifted waterborne item {zdo.m_uid} ({zdo.GetPrefab()}) to water surface y={targetY:0.00}m (was y={wasY:0.00}m)");
                        }
                    }
                }
            }
        }

        public static bool GetTerrainHeight(Vector3 pos, out float height)
        {
            if (Heightmap.GetHeight(pos, out height))
            {
                return true;
            }
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out height))
            {
                return true;
            }
            if (WorldGenerator.instance != null)
            {
                height = WorldGenerator.instance.GetHeight(pos.x, pos.z);
                return true;
            }
            height = 0f;
            return false;
        }

        public static bool IsWaterborneItem(ZDO zdo, out float targetY, float surfaceOffset = -0.25f)
        {
            targetY = 0f;
            if (zdo == null || !_itemDropPrefabHashes.Contains(zdo.GetPrefab()))
            {
                return false;
            }

            Vector3 pos = zdo.GetPosition();
            // Skip interior/dungeon instances (placed in high sky or deep negative space)
            if (pos.y > 2000f || pos.y < -500f)
            {
                return false;
            }

            float liquidLevel = -10000f;
            float measuredLiquid = Floating.GetLiquidLevel(pos, 1f, LiquidType.All);
            if (measuredLiquid > -9000f)
            {
                liquidLevel = measuredLiquid;
            }
            else if (GetTerrainHeight(pos, out float groundHeight))
            {
                float seaLevel = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
                // Open water exists where the terrain floor is below sea level
                if (groundHeight < seaLevel)
                {
                    liquidLevel = seaLevel;
                }
            }

            if (liquidLevel <= -9000f)
            {
                return false;
            }

            // Guard: An item resting on solid dry terrain above the liquid table is not waterborne.
            if (GetTerrainHeight(pos, out float terrainHeight))
            {
                if (pos.y >= terrainHeight - 0.15f && terrainHeight >= liquidLevel)
                {
                    return false;
                }
                float seaLevel = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
                if (terrainHeight >= seaLevel && liquidLevel <= seaLevel + 0.01f)
                {
                    return false;
                }
            }

            targetY = liquidLevel + surfaceOffset;

            // An item is waterborne if it is at or submerged below the liquid surface,
            // but not higher than the surface offset tolerance (e.g. resting on a dock or boat deck).
            // Upper bound: liquidLevel + 0.35f (covers surface waves while excluding boat decks at y >= 30.8m)
            return pos.y <= liquidLevel + 0.35f;
        }

        public static void HandlePickupRequest(ZDOID targetZDO, long senderPeerID)
        {
            if (ZDOMan.instance == null || targetZDO.IsNone() || senderPeerID == 0L) return;
            ZDO zdo = ZDOMan.instance.GetZDO(targetZDO);
            if (zdo == null || !zdo.IsValid()) return;
            if (!_itemDropPrefabHashes.Contains(zdo.GetPrefab())) return;
            if (VacuumEngine.WasMovedThisFrame(targetZDO))
            {
                // The vacuum put this stack in a chest earlier this frame and its DestroyZDO is queued: granting
                // it now would let the player pick up a copy in the frame between the two messages.
                return;
            }

            float now = Time.time;

            if (zdo.GetOwner() == senderPeerID)
            {
                // The requester already owns it on the server, yet asks - its local client state is either
                // awaiting confirmation or needs a higher revision to accept ownership.
                // Advance revisions and force-send to the client so its ItemDrop.CanPickup() clears.
                _recentPickups[targetZDO] = now + PickupGraceSeconds;
                _grants[targetZDO] = (senderPeerID, now);
                zdo.IncreaseOwnerRevision();
                zdo.DataRevision += 4096;
                ZDOMan.instance.ForceSendZDO(targetZDO);
                ClaimEcho.Note(targetZDO);
                WonderlandDebug.LogInfo($"[WaterBuoyancy] Confirmed item {targetZDO} ({zdo.GetPrefab()}) ownership to peer {senderPeerID} (rev bumped)");
                return;
            }

            if (_failedGrants.TryGetValue(targetZDO, out (long peer, int failures, float at) failed)
                && failed.peer == senderPeerID && failed.failures >= FailedGrantsBeforeCooldown
                && now - failed.at < FailedGrantCooldownSeconds)
            {
                // This peer was given the stack twice and it was still lying there each time the grace ran
                // out - they cannot take it (full, over weight, swimming). Vanilla's ItemDrop.RequestOwn
                // backs off exponentially, so refusing for a while costs them nothing they would have had;
                // granting again would only sink the stack in front of them once more. Not logged per
                // request: their retries are what this is here to absorb.
                return;
            }

            if (_grants.TryGetValue(targetZDO, out (long peer, float at) last) && last.peer == senderPeerID
                && now - last.at > PickupGraceSeconds && VacuumEngine.IsHeld(targetZDO))
            {
                // Granted to this same player once already, grace expired, stack still here: Vacuum gets its turn.
                return;
            }

            _recentPickups[targetZDO] = now + PickupGraceSeconds;
            _grants[targetZDO] = (senderPeerID, now);

            // Authoritative grant: Transfer ownership directly to the requesting player,
            // regardless of whether server or another peer was previously recorded.
            zdo.SetOwner(senderPeerID);
            zdo.DataRevision += 4096;
            ZDOMan.instance.ForceSendZDO(targetZDO);
            ClaimEcho.Note(targetZDO);
            WonderlandDebug.LogInfo($"[WaterBuoyancy] Granted pickup ownership of item {targetZDO} ({zdo.GetPrefab()}) to peer {senderPeerID}");
        }

        /// <summary>Called by the sweep as it takes a waterborne item back from a client. If that client was
        /// granted the item and the grace has run out, the pickup did not happen: count it against that
        /// peer. A different peer, or a claim inside the grace (cannot happen - the sweep skips graced
        /// items - but cheap to be exact about), starts the count over.</summary>
        private static void NoteGrantOutcome(ZDO zdo)
        {
            if (!_grants.TryGetValue(zdo.m_uid, out (long peer, float at) grant))
            {
                return;
            }
            float now = Time.time;
            if (zdo.GetOwner() != grant.peer || now - grant.at < PickupGraceSeconds)
            {
                return;
            }
            int failures = _failedGrants.TryGetValue(zdo.m_uid, out (long peer, int failures, float at) previous) && previous.peer == grant.peer
                ? previous.failures + 1
                : 1;
            _failedGrants[zdo.m_uid] = (grant.peer, failures, now);
            if (failures == FailedGrantsBeforeCooldown)
            {
                WonderlandDebug.LogInfo($"[WaterBuoyancy] Peer {grant.peer} was granted item {zdo.m_uid} ({zdo.GetPrefab()}) {failures} times without picking it up - refusing that peer for {FailedGrantCooldownSeconds:0}s, the item stays afloat.");
            }
        }

        /// <summary>True when this engine took the item from a client less than <paramref name="seconds"/> ago -
        /// that client may not have heard yet.</summary>
        public static bool ClaimedWithin(ZDOID uid, float seconds)
        {
            return _claimedAt.TryGetValue(uid, out float at) && Time.time - at < seconds;
        }

        /// <summary>True while a player who asked for this item with RPC_RequestOwn is inside the 3 s pickup
        /// grace HandlePickupRequest granted. VacuumEngine leaves such a stack alone (it is theirs), and the
        /// SetOwner prefix lets the grant through even while the vacuum holds the stack.</summary>
        public static bool HasPickupGrace(ZDOID uid)
        {
            return _recentPickups.TryGetValue(uid, out float pickupTime) && Time.time < pickupTime;
        }

        public static bool ShouldBlockOwnerChange(ZDO zdo, long targetUid)
        {
            if (targetUid == 0L || (ZDOMan.instance != null && targetUid == ZDOMan.GetSessionID()))
            {
                return false;
            }

            if (WonderlandConfig.AllItemsFloatEnabled?.Value != true)
            {
                return false;
            }

            if (!_itemDropPrefabHashes.Contains(zdo.GetPrefab()))
            {
                return false;
            }

            if (_recentPickups.TryGetValue(zdo.m_uid, out float pickupTime) && Time.time < pickupTime)
            {
                return false; // Allowed because player requested pickup via RPC_RequestOwn
            }

            // If the item is in water, block passive handover from ReleaseNearbyZDOS so the server retains control
            if (IsWaterborneItem(zdo, out _))
            {
                return true;
            }

            return false;
        }

        private static void PurgeStalePickupAllowances()
        {
            float now = Time.time;
            if (_recentPickups.Count > 0)
            {
                _purgeScratch.Clear();
                foreach (KeyValuePair<ZDOID, float> kvp in _recentPickups)
                {
                    if (now >= kvp.Value)
                    {
                        _purgeScratch.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < _purgeScratch.Count; i++)
                {
                    _recentPickups.Remove(_purgeScratch[i]);
                }
            }
            if (_grants.Count > 0)
            {
                _purgeScratch.Clear();
                foreach (KeyValuePair<ZDOID, (long peer, float at)> kvp in _grants)
                {
                    if (now - kvp.Value.at > GrantMemorySeconds)
                    {
                        _purgeScratch.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < _purgeScratch.Count; i++)
                {
                    _grants.Remove(_purgeScratch[i]);
                }
            }
            if (_claimedAt.Count > 0)
            {
                _purgeScratch.Clear();
                foreach (KeyValuePair<ZDOID, float> kvp in _claimedAt)
                {
                    if (now - kvp.Value > ClaimMemorySeconds)
                    {
                        _purgeScratch.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < _purgeScratch.Count; i++)
                {
                    _claimedAt.Remove(_purgeScratch[i]);
                }
            }
            if (_failedGrants.Count > 0)
            {
                _purgeScratch.Clear();
                foreach (KeyValuePair<ZDOID, (long peer, int failures, float at)> kvp in _failedGrants)
                {
                    if (now - kvp.Value.at > FailedGrantCooldownSeconds)
                    {
                        _purgeScratch.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < _purgeScratch.Count; i++)
                {
                    _failedGrants.Remove(_purgeScratch[i]);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
    public static class ZdoSetOwnerPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ZDO __instance, long uid)
        {
            // Two holders of server ownership, one gate: WaterBuoyancy keeps waterborne items at the surface,
            // VacuumEngine keeps a claimed ground stack for its settle step. Both let a player's own pickup
            // request through (HandlePickupRequest sets the grace before it calls SetOwner).
            if (WaterBuoyancyEngine.ShouldBlockOwnerChange(__instance, uid) || VacuumEngine.ShouldBlockOwnerChange(__instance, uid)
                || Wonderland.Core.Data.HullBorrow.ShouldBlockOwnerChange(__instance, uid))
            {
                return false; // (third: a hull borrowed for a cargo write must not be handed back by ReleaseZDOS before the write runs)
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    public static class RoutedRpcHandlerPatch
    {
        private static readonly int RequestOwnHash = "RPC_RequestOwn".GetStableHashCode();

        [HarmonyPrefix]
        public static void Prefix(ZRoutedRpc.RoutedRPCData data)
        {
            if (data != null && data.m_methodHash == RequestOwnHash && !data.m_targetZDO.IsNone())
            {
                WaterBuoyancyEngine.HandlePickupRequest(data.m_targetZDO, data.m_senderPeerID);
            }
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    public static class RoutedRpcRoutePatch
    {
        private static readonly int RequestOwnHash = "RPC_RequestOwn".GetStableHashCode();

        [HarmonyPrefix]
        public static void Prefix(ZRoutedRpc.RoutedRPCData rpcData)
        {
            if (rpcData != null && rpcData.m_methodHash == RequestOwnHash && !rpcData.m_targetZDO.IsNone())
            {
                WaterBuoyancyEngine.HandlePickupRequest(rpcData.m_targetZDO, rpcData.m_senderPeerID);
            }
        }
    }
}
