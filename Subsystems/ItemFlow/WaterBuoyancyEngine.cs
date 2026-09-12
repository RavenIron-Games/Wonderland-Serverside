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
        private static readonly HashSet<ZDOID> _sweptThisTick = new HashSet<ZDOID>();
        private static readonly List<ZDO> _scratch = new List<ZDO>();
        private static float _timer;

        public static void Initialize(Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(ZdoSetOwnerPatch));
            SubsystemRegistry.SafePatch(harmony, typeof(RoutedRpcHandlerPatch));
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
            if (WonderlandConfig.AllItemsFloatEnabled?.Value != true || ZDOMan.instance == null || ZNet.instance == null)
            {
                return;
            }

            _timer += dt;
            float interval = WonderlandConfig.FloatSweepInterval?.Value ?? 0.3f;
            if (_timer < interval)
            {
                return;
            }
            _timer = 0f;

            PurgeStalePickupAllowances();
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

                    if (IsWaterborneItem(zdo, out float targetY, surfaceOffset))
                    {
                        Vector3 pos = zdo.GetPosition();
                        bool isServerOwner = zdo.IsOwner();

                        // If not owned by server, or if sunken below surface, lift and claim
                        if (!isServerOwner || Mathf.Abs(pos.y - targetY) > 0.05f)
                        {
                            pos.y = targetY;
                            zdo.SetOwner(serverSession);
                            zdo.SetPosition(pos);
                            zdo.DataRevision += 4096;
                            zdo.Set(ZDOVars.s_velHash, Vector3.zero);
                            zdo.Set(ZDOVars.s_bodyVelHash, Vector3.zero);
                            zdo.Set(ZDOVars.s_bodyAVelHash, Vector3.zero);
                            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                            WonderlandDebug.LogAlways($"[WaterBuoyancy] Lifted waterborne item {zdo.m_uid} ({zdo.GetPrefab()}) to water surface y={targetY:0.00}m (was y={pos.y:0.00}m)");
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

            targetY = liquidLevel + surfaceOffset;

            // An item is waterborne if it is at or submerged below the liquid surface,
            // but not higher than the surface offset tolerance (e.g. resting on a dock or boat deck).
            // Upper bound: liquidLevel + 0.35f (covers surface waves while excluding boat decks at y >= 30.8m)
            return pos.y <= liquidLevel + 0.35f;
        }

        public static void HandlePickupRequest(ZDOID targetZDO, long senderPeerID)
        {
            if (ZDOMan.instance == null || targetZDO.IsNone()) return;
            ZDO zdo = ZDOMan.instance.GetZDO(targetZDO);
            if (zdo == null) return;
            if (!_itemDropPrefabHashes.Contains(zdo.GetPrefab())) return;

            if (zdo.IsOwner() || !zdo.HasOwner())
            {
                _recentPickups[targetZDO] = Time.time + 3f;
                zdo.SetOwner(senderPeerID);
                ZDOMan.instance.ForceSendZDO(targetZDO);
                WonderlandDebug.LogAlways($"[WaterBuoyancy] Granted pickup ownership of item {targetZDO} ({zdo.GetPrefab()}) to peer {senderPeerID}");
            }
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
            if (_recentPickups.Count == 0) return;
            float now = Time.time;
            var expired = new List<ZDOID>();
            foreach (KeyValuePair<ZDOID, float> kvp in _recentPickups)
            {
                if (now >= kvp.Value)
                {
                    expired.Add(kvp.Key);
                }
            }
            for (int i = 0; i < expired.Count; i++)
            {
                _recentPickups.Remove(expired[i]);
            }
        }
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
    public static class ZdoSetOwnerPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ZDO __instance, long uid)
        {
            if (WaterBuoyancyEngine.ShouldBlockOwnerChange(__instance, uid))
            {
                return false;
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
            if (data.m_methodHash == RequestOwnHash && !data.m_targetZDO.IsNone())
            {
                WaterBuoyancyEngine.HandlePickupRequest(data.m_targetZDO, data.m_senderPeerID);
            }
        }
    }
}
