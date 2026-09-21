using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Server-side mod enforcement engine for Wonderland.
    ///
    /// Verifies that connecting clients are authentic, unmodded vanilla Valheim clients.
    /// Legitimate vanilla clients (Windows, Linux, Steam Deck Proton/Native, Microsoft Store, Switch 2)
    /// communicate using a strictly predictable set of ZRpc wire packets, vanilla version strings,
    /// and standard game routed RPCs.
    ///
    /// If a non-vanilla client connects (e.g. sending BepInEx ServerSync probes, Jotunn synchronization
    /// packets, modded ZRpc methods, or non-vanilla version strings), they are flagged and kicked with
    /// the explicit disconnect message: "Vanilla enforcement enabled, connect fairly with vanilla only client".
    ///
    /// Server administrators on the server's authoritative adminlist.txt are completely bypassed.
    /// </summary>
    public static class ModEnforcement
    {
        private static readonly Regex VanillaVersionRegex = new Regex(@"^([a-z0-9]+-)?\d+\.\d+\.\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Vanilla ZRpc methods legitimate vanilla clients send to the dedicated server
        private static readonly HashSet<int> VanillaZRpcHashes = new HashSet<int>();

        // Known mod traps / framework signatures
        private static readonly Dictionary<int, string> ModTraps = new Dictionary<int, string>();

        // Vanilla RoutedRPC methods (global + ZNetView instance methods)
        private static readonly HashSet<int> VanillaRoutedRpcHashes = new HashSet<int>();

        // Plant/crop prefab hashes for placement cadence guard
        private static readonly HashSet<int> PlantPrefabHashes = new HashSet<int>();

        // Pending ZDOs awaiting Deserialize to inspect their prefab
        private static readonly HashSet<ZDOID> _pendingPlacements = new HashSet<ZDOID>();
        private const int PendingCap = 4096;

        private struct PlacementRecord
        {
            public float Time;
            public ZDOID Zdoid;

            public PlacementRecord(float time, ZDOID zdoid)
            {
                Time = time;
                Zdoid = zdoid;
            }
        }

        private static readonly Dictionary<long, Queue<PlacementRecord>> _peerPlacementHistory = new Dictionary<long, Queue<PlacementRecord>>();

        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            InitVanillaZRpcWhitelist();
            InitModTraps();
            InitVanillaRoutedRpcWhitelist();

            WonderlandDebug.LogAlways("[ModEnforcement] Initialized mod enforcement engine (vanilla whitelist & admin bypass ready).");
        }

        public static void OnWorldReady()
        {
            // Expand routed RPC whitelist with any dynamically registered methods from ZRoutedRpc
            if (ZRoutedRpc.instance?.m_functions != null)
            {
                foreach (int hash in ZRoutedRpc.instance.m_functions.Keys)
                {
                    VanillaRoutedRpcHashes.Add(hash);
                }
            }

            InitPlantPrefabHashes();

            WonderlandDebug.LogInfo($"[ModEnforcement] World ready: {VanillaZRpcHashes.Count} ZRpc hashes, {VanillaRoutedRpcHashes.Count} RoutedRPC hashes registered in vanilla whitelist.");
        }

        public static void RegisterPendingPlacement(ZDOID id)
        {
            if (_pendingPlacements.Count >= PendingCap)
            {
                _pendingPlacements.Clear();
            }
            _pendingPlacements.Add(id);
        }

        public static bool IsPendingPlacement(ZDOID id)
        {
            if (_pendingPlacements.Count == 0) return false;
            return _pendingPlacements.Remove(id);
        }

        public static bool IsPlantPrefab(int prefabHash)
        {
            return PlantPrefabHashes.Contains(prefabHash);
        }

        public static void OnPeerDisconnected(ZNetPeer? peer)
        {
            if (peer != null)
            {
                _peerPlacementHistory.Remove(peer.m_uid);
            }
        }

        public static void EvaluatePlacement(ZDO zdo)
        {
            if (zdo == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (WonderlandConfig.ModEnforcementEnabled?.Value == false) return;
            if (WonderlandConfig.ModEnforcementPlacementGuard?.Value == false) return;

            int prefab = zdo.GetPrefab();
            if (!IsPlantPrefab(prefab)) return;

            long owner = zdo.GetOwner();
            ZNetPeer peer = ZNet.instance.GetPeer(owner);
            if (peer == null) return;
            if (IsAdmin(peer)) return;

            float now = Time.time;
            if (!_peerPlacementHistory.TryGetValue(owner, out var queue))
            {
                queue = new Queue<PlacementRecord>();
                _peerPlacementHistory[owner] = queue;
            }

            // Purge records older than 0.5 seconds
            while (queue.Count > 0 && (now - queue.Peek().Time) > 0.5f)
            {
                queue.Dequeue();
            }

            queue.Enqueue(new PlacementRecord(now, zdo.m_uid));

            int maxAllowed = WonderlandConfig.ModEnforcementMaxPlantBatch?.Value ?? 4;
            if (queue.Count > maxAllowed)
            {
                int count = queue.Count;
                string hostName = peer.m_socket?.GetHostName() ?? "Unknown";
                string playerName = peer.m_playerName ?? hostName;
                WonderlandDebug.LogAlways($"[SECURITY:ModEnforcement] Client {playerName} ({hostName}) placed {count} plants within 0.5s (limit: {maxAllowed}) - batch planting mod detected!");

                // Purge all plants from this burst batch so no modded crops remain in the world
                if (ZDOMan.instance != null)
                {
                    foreach (var record in queue)
                    {
                        ZDO pastZdo = ZDOMan.instance.GetZDO(record.Zdoid);
                        if (pastZdo != null)
                        {
                            pastZdo.SetOwner(ZDOMan.GetSessionID());
                            ZDOMan.instance.DestroyZDO(pastZdo);
                        }
                    }
                }
                queue.Clear();

                KickNonVanillaClient(peer.m_rpc, peer, $"automated placement mod detected ({count} plants in <0.5s)");
            }
        }

        public static void SendActiveModProbes(ZNetPeer peer)
        {
            if (peer?.m_rpc == null || !peer.m_rpc.IsConnected()) return;
            if (WonderlandConfig.ModEnforcementActiveProbe?.Value == false) return;

            try
            {
                // 1. ServerSync challenge probe
                ZPackage serverSyncPkg = new ZPackage();
                serverSyncPkg.Write("WonderlandEnforcement");
                serverSyncPkg.Write("1.0.0");
                peer.m_rpc.Invoke("ServerSync VersionCheck", serverSyncPkg);

                // 2. Jotunn version sync probes
                ZPackage jotunnPkg = new ZPackage();
                jotunnPkg.Write(0);
                peer.m_rpc.Invoke("RPC_Jotunn_ReceiveVersionData", jotunnPkg);
                peer.m_rpc.Invoke("Jotunn_VersionCheck", jotunnPkg);

                // 3. ValheimPlus probe
                ZPackage vplusPkg = new ZPackage();
                vplusPkg.Write("WonderlandEnforcement");
                vplusPkg.Write("1.0.0");
                peer.m_rpc.Invoke("ValheimPlus_VersionCheck", vplusPkg);

                // 4. AzuAntiCheat probe
                ZPackage azuPkg = new ZPackage();
                azuPkg.Write("WonderlandEnforcement");
                azuPkg.Write("1.0.0");
                peer.m_rpc.Invoke("AzuAntiCheat_VersionCheck", azuPkg);

                WonderlandDebug.LogInfo($"[ModEnforcement] Sent active mod probes to connecting peer {peer.m_playerName} ({peer.m_socket?.GetHostName()}).");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[ModEnforcement] Error sending active mod probes: {ex.Message}");
            }
        }

        private static void InitPlantPrefabHashes()
        {
            PlantPrefabHashes.Clear();

            // 1. Scan active prefabs registered in ZNetScene
            if (ZNetScene.instance?.m_prefabs != null)
            {
                foreach (GameObject go in ZNetScene.instance.m_prefabs)
                {
                    if (go == null) continue;
                    if (go.GetComponent<Plant>() != null ||
                        (go.GetComponent<Piece>() != null && go.GetComponent<Pickable>() != null))
                    {
                        PlantPrefabHashes.Add(go.name.GetStableHashCode());
                    }
                }
            }

            // 2. Built-in vanilla plant and crop prefab names as static fallback
            string[] knownVanillaPlants = new[]
            {
                "Sapling_Carrot", "Sapling_Turnip", "Sapling_Onion",
                "Sapling_SeedCarrot", "Sapling_SeedTurnip", "Sapling_SeedOnion",
                "Sapling_Barley", "Sapling_Flax", "Sapling_JotunPuffs", "Sapling_Magecap",
                "Sapling_Dandelion", "Sapling_Fir", "Sapling_Pine", "Sapling_Birch",
                "Sapling_Oak", "Sapling_Ash", "Vineberry_sapling", "Fiddlehead_sapling",
                "Sapling_Mushroom", "Sapling_MushroomYellow", "Sapling_MushroomBlue",
                "Sapling_SmokePuff", "Sapling_VolcanoPuff"
            };

            foreach (string name in knownVanillaPlants)
            {
                PlantPrefabHashes.Add(name.GetStableHashCode());
            }

            WonderlandDebug.LogInfo($"[ModEnforcement] Indexed {PlantPrefabHashes.Count} plant/crop prefabs for placement cadence guard.");
        }

        /// <summary>
        /// Admin bypass based solely on the server passing the adminlist check.
        /// </summary>
        public static bool IsAdmin(string? hostName)
        {
            if (string.IsNullOrEmpty(hostName) || ZNet.instance == null) return false;
            if (WonderlandConfig.ModEnforcementAdminBypass?.Value == false) return false;
            return ZNet.instance.IsAdmin(hostName);
        }

        public static bool IsAdmin(ZNetPeer? peer)
        {
            if (peer?.m_socket == null) return false;
            return IsAdmin(peer.m_socket.GetHostName());
        }

        public static bool IsAdmin(ZRpc? rpc)
        {
            if (rpc?.GetSocket() == null) return false;
            return IsAdmin(rpc.GetSocket().GetHostName());
        }

        public static bool IsVanillaVersionString(string versionString)
        {
            if (string.IsNullOrEmpty(versionString)) return false;

            // Reject strings with obvious mod markers
            if (versionString.Contains("@") ||
                versionString.IndexOf("mod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                versionString.IndexOf("bepinex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                versionString.IndexOf("vplus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                versionString.IndexOf("valheimplus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                versionString.IndexOf("patch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (WonderlandConfig.ModEnforcementStrictVersion?.Value != false)
            {
                return VanillaVersionRegex.IsMatch(versionString.Trim());
            }

            return true;
        }

        public static bool IsVanillaZRpc(int methodHash)
        {
            if (methodHash == 0) return true; // Ping
            return VanillaZRpcHashes.Contains(methodHash);
        }

        public static bool IsKnownModTrap(int methodHash, out string modName)
        {
            return ModTraps.TryGetValue(methodHash, out modName);
        }

        public static bool IsVanillaRoutedRpc(int methodHash, ZDOID targetZDO = default)
        {
            if (VanillaRoutedRpcHashes.Contains(methodHash)) return true;

            // Dynamic resilience 1: Check server ZRoutedRpc registered functions
            if (ZRoutedRpc.instance?.m_functions != null && ZRoutedRpc.instance.m_functions.ContainsKey(methodHash))
            {
                VanillaRoutedRpcHashes.Add(methodHash);
                return true;
            }

            // Dynamic resilience 2: Check target entity's active ZNetView instance functions
            if (!targetZDO.IsNone() && ZNetScene.instance != null)
            {
                GameObject instance = ZNetScene.instance.FindInstance(targetZDO);
                if (instance != null)
                {
                    ZNetView nview = instance.GetComponent<ZNetView>();
                    if (nview?.m_functions != null && nview.m_functions.ContainsKey(methodHash))
                    {
                        VanillaRoutedRpcHashes.Add(methodHash);
                        return true;
                    }
                }
            }

            return false;
        }

        public static void KickNonVanillaClient(ZRpc? rpc, ZNetPeer? peer, string reason)
        {
            string hostName = peer?.m_socket?.GetHostName() ?? rpc?.GetSocket()?.GetHostName() ?? "Unknown";
            string playerName = peer?.m_playerName ?? hostName;
            long playerId = peer?.m_playerID ?? 0L;

            string utcStamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            string detectionStamp = $"[STAMP: {utcStamp}] [DETECTED: {reason}]";

            if (IsAdmin(hostName))
            {
                WonderlandDebug.LogAlways($"[SECURITY:ModEnforcement] {detectionStamp} Admin {playerName} ({hostName}) triggered mod detection ({reason}) but was BYPASSED due to adminlist.");
                return;
            }

            string kickMessage = WonderlandConfig.ModEnforcementKickMessage?.Value ?? "Vanilla enforcement enabled, connect fairly with vanilla only client";

            AuditLog.Flag("ModEnforcement", playerName, $"{detectionStamp} Non-vanilla client detected ({reason}) - KICKING. Notice: \"{kickMessage}\"", playerId);

            if (WonderlandConfig.ModEnforcementKick?.Value == false)
            {
                WonderlandDebug.LogAlways($"[SECURITY:ModEnforcement] {detectionStamp} Auto-kick disabled by config for {playerName}.");
                return;
            }

            try
            {
                // 1. Deliver center HUD banner and chat broadcast if peer has entered world
                if (peer != null && peer.m_uid != 0 && ZRoutedRpc.instance != null)
                {
                    if (peer.m_characterID != ZDOID.None)
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, peer.m_characterID, "Message", (int)MessageHud.MessageType.Center, kickMessage, 0);
                    }

                    var serverUser = new UserInfo
                    {
                        Name = "Server",
                        UserId = new Splatform.PlatformUserID("0")
                    };
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ChatMessage", Vector3.zero, (int)Talker.Type.Normal, serverUser, kickMessage);
                }

                bool useIncompat = WonderlandConfig.ModEnforcementUseIncompatibleVersion?.Value != false;
                ZNet.ConnectionStatus status = useIncompat
                    ? ZNet.ConnectionStatus.ErrorVersion
                    : ZNet.ConnectionStatus.ErrorKicked;

                // 2. Deliver console RemotePrint and status over ZRpc wire
                ZRpc activeRpc = rpc ?? peer?.m_rpc;
                if (activeRpc != null && activeRpc.IsConnected())
                {
                    activeRpc.Invoke("RemotePrint", kickMessage);
                    activeRpc.Invoke("Error", (int)status);
                    if (!useIncompat)
                    {
                        activeRpc.Invoke("Kicked");
                    }
                }

                // 3. Trigger server-side peer disconnect
                ZNetPeer activePeer = peer ?? (activeRpc != null && ZNet.instance != null ? ZNet.instance.GetPeer(activeRpc) : null);
                if (activePeer != null)
                {
                    if (!useIncompat)
                    {
                        ZNet.instance?.InternalKick(activePeer);
                    }
                    else
                    {
                        ZNet.PeersToDisconnectAfterKick[activePeer] = Time.time + 1f;
                    }
                }
                else if (activeRpc != null)
                {
                    activeRpc.GetSocket()?.Dispose();
                }
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[SECURITY:ModEnforcement] Error kicking client {playerName}: {ex.Message}");
            }
        }

        private static void InitVanillaZRpcWhitelist()
        {
            string[] vanillaMethods = new[]
            {
                "ServerHandshake",
                "ClientHandshake",
                "PeerInfo",
                "RPC_RequestValidSimulationDistance",
                "RPC_ValidatedSimulationDistance",
                "CharacterID",
                "PlayerID",
                "RPC_PeerRealtimeMultiplaying",
                "ServerSyncedPlayerData",
                "SavePlayerProfile",
                "Disconnect",
                "ZDOData",
                "RoutedRPC",
                "NetTime",
                "RemotePrint",
                "PlayerList",
                "HistoricalPlayerList",
                "AdminList",
                "Kicked",
                "Error",
                // Admin commands
                "Kick",
                "Ban",
                "Unban",
                "Save",
                "PrintBanned",
                "RPC_RemoteCommand"
            };

            foreach (string name in vanillaMethods)
            {
                VanillaZRpcHashes.Add(name.GetStableHashCode());
            }
        }

        private static void InitModTraps()
        {
            RegisterModTrap("ServerSync VersionCheck", "ServerSync probe");
            RegisterModTrap("RPC_Jotunn_ReceiveVersionData", "Jotunn version sync probe");
            RegisterModTrap("Jotunn_VersionCheck", "Jotunn version check probe");
            RegisterModTrap("RPC_Jotunn_RequestModList", "Jotunn mod list probe");
            RegisterModTrap("RPC_Jotunn_ModList", "Jotunn mod list probe");
            RegisterModTrap("ValheimPlus_VersionCheck", "ValheimPlus probe");
            RegisterModTrap("VPA_VersionCheck", "Valheim Plus/Additions probe");
            RegisterModTrap("AzuAntiCheat_VersionCheck", "AzuAntiCheat probe");
            RegisterModTrap("AzuModList_VersionCheck", "AzuModList probe");
            RegisterModTrap("ServerSync_SyncModData", "ServerSync data sync probe");
            RegisterModTrap("ServerSync_ClientInfo", "ServerSync client info probe");
            RegisterModTrap("WardIsLove_VersionCheck", "WardIsLove probe");
            RegisterModTrap("ExpandWorld_VersionCheck", "ExpandWorld probe");
        }

        private static void RegisterModTrap(string methodName, string description)
        {
            ModTraps[methodName.GetStableHashCode()] = $"'{methodName}' ({description})";
        }

        private static void InitVanillaRoutedRpcWhitelist()
        {
            // Global routed RPC methods
            string[] globalMethods = new[]
            {
                "ChatMessage",
                "RPC_TeleportPlayer",
                "RPC_SetDreamCinematic",
                "RPC_DamageText",
                "ShowMessage",
                "DestroyZDO",
                "RequestZDO",
                "SpawnObject",
                "SleepStart",
                "SleepStop",
                "RPC_Ping",
                "RPC_Pong",
                "RPC_SetConnection",
                "RPC_DiscoverLocationResponse",
                "RPC_RegisterKill",
                "RPC_DiscoverClosestLocation",
                // PersistentEventSystem (Bog Witch / 1.0.15)
                "RequestActiveEventsList",
                "RequestStartEvent",
                "RequestStopEvent",
                "UpdateClientEventsList",
                "RPC_RequestActiveEventsList",
                "RPC_RequestStartEvent",
                "RPC_RequestStopEvent",
                "RPC_UpdateClientEventsList",
                // RandEventSystem
                "SetEvent",
                "startrandomevent",
                "resetrandomevent",
                // ZoneSystem
                "SetGlobalKey",
                "RemoveGlobalKey",
                "GlobalKeys",
                "LocationIcons"
            };

            foreach (string m in globalMethods)
            {
                VanillaRoutedRpcHashes.Add(m.GetStableHashCode());
            }

            // Standard vanilla ZNetView instance methods across characters, pieces, and items
            string[] nviewMethods = new[]
            {
                "AddSaddle",
                "Alert",
                "Backward",
                "Command",
                "Controls",
                "FlashShield",
                "Forward",
                "Hide",
                "Hit",
                "MapData",
                "Message",
                "OnDeath",
                "OnNearProjectileHit",
                "OnTargeted",
                "Pick",
                "Pickup",
                "ReleaseControl",
                "RemoveSaddle",
                "RequestControl",
                "RequestPickup",
                "RequestRespons",
                "RPC_AddAdrenaline",
                "RPC_AddAmmo",
                "RPC_AddFuel",
                "RPC_AddFuelAmount",
                "RPC_AddItem",
                "RPC_AddNoise",
                "RPC_AddOre",
                "RPC_AddStatusEffect",
                "RPC_AnimateLever",
                "RPC_AnimateLeverReturn",
                "RPC_ApplyOperation",
                "RPC_Attach",
                "RPC_Attack",
                "RPC_BossSpawnInitiated",
                "RPC_ClearCachedSupport",
                "RPC_CreateFragments",
                "RPC_Damage",
                "RPC_DestroyAttachment",
                "RPC_Discovered",
                "RPC_Drain",
                "RPC_DropArrows",
                "RPC_DropItem",
                "RPC_DropItemByName",
                "RPC_EatConfirmation",
                "RPC_EmptyProcessed",
                "RPC_Extract",
                "RPC_FreezeFrame",
                "RPC_Grow",
                "RPC_Heal",
                "RPC_HealthChanged",
                "RPC_HitNow",
                "RPC_HitWhileDodging",
                "RPC_IncinerateRespons",
                "RPC_Left",
                "RPC_MakePiece",
                "RPC_Nibble",
                "RPC_OnEat",
                "RPC_OnHit",
                "RPC_OnLegUse",
                "RPC_OnStateChanged",
                "RPC_OpenResponse",
                "RPC_Pick",
                "RPC_PlayMusic",
                "RPC_ProjectileHit",
                "RPC_Remove",
                "RPC_RemoveBossSpawnInventoryItems",
                "RPC_RemoveDoneItem",
                "RPC_Repair",
                "RPC_RequestDenied",
                "RPC_RequestIncinerate",
                "RPC_RequestOpen",
                "RPC_RequestOwn",
                "RPC_RequestStack",
                "RPC_RequestStateChange",
                "RPC_RequestTakeAll",
                "RPC_ResetCloth",
                "RPC_SetAreaHealth",
                "RPC_SetConnected",
                "RPC_SetFuel",
                "RPC_SetFuelAmount",
                "RPC_SetLoadedVisual",
                "RPC_SetPicked",
                "RPC_SetPose",
                "RPC_SetSlotVisual",
                "RPC_SetSnow",
                "RPC_SetStayTTL",
                "RPC_SetTag",
                "RPC_SetTamed",
                "RPC_SetTarget",
                "RPC_SetVisualItem",
                "RPC_Shake",
                "RPC_Shoot",
                "RPC_Sleep",
                "RPC_SpawnBoss",
                "RPC_StackResponse",
                "RPC_Stagger",
                "RPC_StateChanged",
                "RPC_TakeAllResponse",
                "RPC_Tap",
                "RPC_TeleportTo",
                "RPC_ToggleOn",
                "RPC_TryEat",
                "RPC_UnSummon",
                "RPC_UpdateEffects",
                "RPC_UpdateMaterial",
                "RPC_UpdateVisual",
                "RPC_Wakeup",
                "Rudder",
                "Say",
                "SetAggravated",
                "SetName",
                "SetOwner",
                "SetPlayed",
                "SetSaddle",
                "SetTrigger",
                "SetVisualItem",
                "Step",
                "Stop",
                "ToggleEnabled",
                "TogglePermitted",
                "Trigger",
                "UseDoor",
                "UseStamina",
                "UseEitr",
                "discovered",
                // Bidirectional alias coverage (methods registered or invoked with/without RPC_ prefix)
                "RPC_UseEitr",
                "RPC_UseStamina",
                "RPC_UseDoor",
                "RPC_SetTrigger",
                "RPC_MapData",
                "RPC_Command",
                "RPC_Alert",
                "RPC_Controls",
                "RPC_Forward",
                "RPC_Backward",
                "RPC_Rudder",
                "RPC_Stop",
                "RPC_Step",
                "RPC_Trigger",
                "RPC_Say",
                "RPC_FlashShield",
                "RPC_TogglePermitted",
                "RPC_ToggleEnabled",
                "RPC_ReleaseControl",
                "RPC_RequestControl",
                "RPC_RequestRespons",
                "RPC_SetSaddle",
                "RPC_AddSaddle",
                "RPC_RemoveSaddle",
                "RPC_SetAggravated",
                "RPC_SetName",
                "RPC_SetOwner",
                "RPC_SetPlayed",
                "RPC_Hide",
                "RPC_Hit",
                "RPC_Message",
                "RPC_OnDeath",
                "RPC_OnNearProjectileHit",
                "RPC_OnTargeted",
                "RPC_Pickup",
                "RPC_RequestPickup"
            };

            foreach (string m in nviewMethods)
            {
                VanillaRoutedRpcHashes.Add(m.GetStableHashCode());
            }
        }
    }
}
