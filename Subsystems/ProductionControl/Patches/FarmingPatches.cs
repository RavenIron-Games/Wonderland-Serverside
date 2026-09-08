using System;
using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.ProductionControl.Patches
{
    [HarmonyPatch]
    public static class FarmingPatches
    {
        [HarmonyPatch(typeof(Plant), "HaveSpace")]
        [HarmonyPrefix]
        public static bool Prefix_HaveSpace(Plant __instance, ref bool __result)
        {
            if (WonderlandConfig.DisableCropSpaceCheck != null && WonderlandConfig.DisableCropSpaceCheck.Value)
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Plant), "TimeSincePlanted")]
        [HarmonyPostfix]
        public static void Postfix_TimeSincePlanted(Plant __instance, ref double __result)
        {
            if (WonderlandConfig.CropGrowthSpeedMultiplier != null && WonderlandConfig.CropGrowthSpeedMultiplier.Value > 1.0f)
            {
                __result *= WonderlandConfig.CropGrowthSpeedMultiplier.Value;
            }
        }

        [HarmonyPatch(typeof(Plant), "GetHoverText")]
        [HarmonyPostfix]
        public static void Postfix_GetHoverText(Plant __instance, ref string __result)
        {
            if (WonderlandConfig.CropBiomeRestrictionsBypass != null && WonderlandConfig.CropBiomeRestrictionsBypass.Value)
            {
                if (__result.Contains("wrong biome"))
                {
                    __result = __result.Replace(" (wrong biome)", "");
                }
            }
        }
    }
}
