using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Harmony wire interception patches for ModEnforcement.
    /// Catches non-vanilla version strings, unauthorized ZRpc wire packets, mod framework probes,
    /// non-vanilla routed RPCs, and custom synced player data keys.
    ///
    /// Server admins are checked against the authoritative server adminlist.txt and fully bypassed.
    /// </summary>
    public static class ModEnforcementPatches
    {
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        public static class Patch_ZNet_RPC_PeerInfo
        {
            [HarmonyPrefix]
            public static bool Prefix(ZNet __instance, ZRpc rpc, ZPackage pkg)
            {
                if (!__instance.IsServer()) return true;
                if (WonderlandConfig.ModEnforcementEnabled?.Value == false) return true;

                ISocket socket = rpc.GetSocket();
                if (socket == null) return true;
                string hostName = socket.GetHostName();

                // Admin bypass based solely on server adminlist
                if (ModEnforcement.IsAdmin(hostName)) return true;

                // Peek versionString without consuming original package reader position
                int oldPos = pkg.GetPos();
                try
                {
                    long uid = pkg.ReadLong();
                    string versionString = pkg.ReadString();

                    if (!ModEnforcement.IsVanillaVersionString(versionString))
                    {
                        ModEnforcement.KickNonVanillaClient(rpc, null, $"non-vanilla version string '{versionString}'");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    WonderlandDebug.LogWarning($"[ModEnforcement] Error parsing PeerInfo version packet: {ex.Message}");
                }
                finally
                {
                    pkg.SetPos(oldPos);
                }

                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(ZNet __instance, ZRpc rpc)
            {
                if (!__instance.IsServer()) return;
                if (WonderlandConfig.ModEnforcementEnabled?.Value == false) return;
                if (WonderlandConfig.ModEnforcementActiveProbe?.Value == false) return;

                ZNetPeer peer = __instance.GetPeer(rpc);
                if (peer == null || !peer.IsReady()) return;
                if (ModEnforcement.IsAdmin(peer)) return;

                ModEnforcement.SendActiveModProbes(peer);
            }
        }

        [HarmonyPatch(typeof(ZRpc), "HandlePackage")]
        public static class Patch_ZRpc_HandlePackage
        {
            [HarmonyPrefix]
            public static bool Prefix(ZRpc __instance, ZPackage package)
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return true;
                if (WonderlandConfig.ModEnforcementEnabled?.Value == false) return true;

                ISocket socket = __instance.GetSocket();
                if (socket == null) return true;
                string hostName = socket.GetHostName();

                // Admin bypass based solely on server adminlist
                if (ModEnforcement.IsAdmin(hostName)) return true;

                int oldPos = package.GetPos();
                int methodHash = package.ReadInt();
                package.SetPos(oldPos);

                if (methodHash == 0) return true; // Ping

                // Catch known mod framework traps
                if (ModEnforcement.IsKnownModTrap(methodHash, out string trapDescription))
                {
                    ZNetPeer peer = ZNet.instance.GetPeer(__instance);
                    ModEnforcement.KickNonVanillaClient(__instance, peer, $"sent mod probe: {trapDescription}");
                    return false;
                }

                // Check against vanilla ZRpc whitelist
                if (!ModEnforcement.IsVanillaZRpc(methodHash))
                {
                    ZNetPeer peer = ZNet.instance.GetPeer(__instance);
                    ModEnforcement.KickNonVanillaClient(__instance, peer, $"sent unauthorized ZRpc method hash 0x{methodHash:X8} ({methodHash})");
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
        public static class Patch_ZRoutedRpc_HandleRoutedRPC
        {
            [HarmonyPrefix]
            public static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData data)
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return true;
                if (WonderlandConfig.ModEnforcementEnabled?.Value == false) return true;
                if (WonderlandConfig.ModEnforcementInspectRoutedRpc?.Value == false) return true;
                if (data.m_senderPeerID == 0) return true; // Local server routed RPC

                ZNetPeer peer = ZNet.instance.GetPeer(data.m_senderPeerID);
                if (peer == null) return true;

                // Admin bypass based solely on server adminlist
                if (ModEnforcement.IsAdmin(peer)) return true;

                if (!ModEnforcement.IsVanillaRoutedRpc(data.m_methodHash, data.m_targetZDO))
                {
                    ModEnforcement.KickNonVanillaClient(peer.m_rpc, peer, $"sent unauthorized RoutedRPC hash 0x{data.m_methodHash:X8} ({data.m_methodHash})");
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_ServerSyncedPlayerData")]
        public static class Patch_ZNet_RPC_ServerSyncedPlayerData
        {
            private static readonly HashSet<string> VanillaSyncKeys = new HashSet<string>
            {
                "platformDisplayName",
                "baseValue",
                "possibleEvents",
                "defeatedBosses"
            };

            [HarmonyPrefix]
            public static bool Prefix(ZNet __instance, ZRpc rpc, ZPackage data)
            {
                if (!__instance.IsServer()) return true;
                if (WonderlandConfig.ModEnforcementEnabled?.Value == false) return true;

                ISocket socket = rpc.GetSocket();
                if (socket == null) return true;
                string hostName = socket.GetHostName();

                // Admin bypass based solely on server adminlist
                if (ModEnforcement.IsAdmin(hostName)) return true;

                int oldPos = data.GetPos();
                try
                {
                    data.ReadVector3();
                    data.ReadBool();
                    int num = data.ReadInt();
                    for (int i = 0; i < num; i++)
                    {
                        string key = data.ReadString();
                        data.ReadString();
                        if (!VanillaSyncKeys.Contains(key))
                        {
                            ZNetPeer peer = __instance.GetPeer(rpc);
                            ModEnforcement.KickNonVanillaClient(rpc, peer, $"injected custom player sync key '{key}'");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    WonderlandDebug.LogWarning($"[ModEnforcement] Error inspecting ServerSyncedPlayerData: {ex.Message}");
                }
                finally
                {
                    data.SetPos(oldPos);
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(ZNet), "Disconnect", typeof(ZNetPeer))]
        public static class Patch_ZNet_Disconnect
        {
            [HarmonyPrefix]
            public static void Prefix(ZNetPeer peer)
            {
                ModEnforcement.OnPeerDisconnected(peer);
                EquipmentGuard.OnPeerDisconnected(peer);
            }
        }

        [HarmonyPatch(typeof(ZDOMan), "CreateNewZDO", typeof(ZDOID), typeof(Vector3), typeof(int))]
        public static class Patch_ZDOMan_CreateNewZDO
        {
            [HarmonyPostfix]
            public static void Postfix(ZDO __result, [HarmonyArgument(2)] int prefabHash)
            {
                if (__result == null || prefabHash != 0) return;
                if (WonderlandConfig.ModEnforcementEnabled?.Value == false) return;
                if (WonderlandConfig.ModEnforcementPlacementGuard?.Value == false) return;

                ModEnforcement.RegisterPendingPlacement(__result.m_uid);
            }
        }

        [HarmonyPatch(typeof(ZDO), "Deserialize", typeof(ZPackage))]
        public static class Patch_ZDO_Deserialize
        {
            [HarmonyPostfix]
            public static void Postfix(ZDO __instance)
            {
                if (WonderlandConfig.ModEnforcementEnabled?.Value == false) return;
                if (WonderlandConfig.ModEnforcementPlacementGuard?.Value == false) return;

                if (ModEnforcement.IsPendingPlacement(__instance.m_uid))
                {
                    ModEnforcement.EvaluatePlacement(__instance);
                }
            }
        }
    }
}
