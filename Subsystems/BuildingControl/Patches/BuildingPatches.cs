using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.BuildingControl.Patches
{
    [HarmonyPatch]
    public static class BuildingPatches
    {
        private static float _lastAutoRepairTime = 0f;

        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        [HarmonyPrefix]
        public static void Prefix_UpdatePlacement(Player __instance)
        {
            if (WonderlandConfig.BuildRangeMultiplier != null && WonderlandConfig.BuildRangeMultiplier.Value > 1.0f)
            {
                __instance.m_maxPlaceDistance = 5f * WonderlandConfig.BuildRangeMultiplier.Value;
            }
        }

        [HarmonyPatch(typeof(WearNTear), "UpdateWear")]
        [HarmonyPrefix]
        public static bool Prefix_UpdateWear(WearNTear __instance)
        {
            if (WonderlandConfig.DisableBuildingDecay != null && WonderlandConfig.DisableBuildingDecay.Value)
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(WearNTear), "GetSupport")]
        [HarmonyPostfix]
        public static void Postfix_GetSupport(WearNTear __instance, ref float __result)
        {
            if (WonderlandConfig.BuildingSupportMultiplier != null && WonderlandConfig.BuildingSupportMultiplier.Value > 1.0f)
            {
                __result = SafeMath.Safe(__result * WonderlandConfig.BuildingSupportMultiplier.Value, __result);
            }
        }

        public static void ProcessAutoRepair()
        {
            if (WonderlandConfig.AutoRepairRadius == null || WonderlandConfig.AutoRepairRadius.Value <= 0f) return;
            if (Time.time - _lastAutoRepairTime < 1.0f) return;
            _lastAutoRepairTime = Time.time;

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null) return;

            ItemDrop.ItemData rightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem")?.GetValue(localPlayer) as ItemDrop.ItemData;
            if (rightItem == null || rightItem.m_shared.m_name != "$item_hammer") return;

            Vector3 pos = localPlayer.transform.position;
            float radius = WonderlandConfig.AutoRepairRadius.Value;

            int mask = LayerMask.GetMask("piece", "piece_nonsolid");
            Collider[] colliders = Physics.OverlapSphere(pos, radius, mask);

            foreach (var col in colliders)
            {
                WearNTear wnt = col.GetComponentInParent<WearNTear>();
                if (wnt != null && wnt.Repair())
                {
                    WonderlandDebug.LogInfo($"Auto-repaired building piece: {wnt.gameObject.name}");
                }
            }
        }
    }
}
