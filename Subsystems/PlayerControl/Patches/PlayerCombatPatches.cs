using System;
using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.PlayerControl.Patches
{
    [HarmonyPatch]
    public static class PlayerCombatPatches
    {
        [HarmonyPatch(typeof(Player), "UseStamina")]
        [HarmonyPrefix]
        public static void Prefix_UseStamina(Player __instance, ref float v)
        {
            if (__instance.IsRunning() && WonderlandConfig.RunningStaminaMultiplier != null)
            {
                v *= WonderlandConfig.RunningStaminaMultiplier.Value;
            }
            else if (__instance.IsSwimming() && WonderlandConfig.SwimmingStaminaMultiplier != null)
            {
                v *= WonderlandConfig.SwimmingStaminaMultiplier.Value;
            }
        }

        [HarmonyPatch(typeof(Character), "OnFallLand")]
        [HarmonyPrefix]
        public static void Prefix_OnFallLand(Character __instance, ref float hitDistance)
        {
            if (__instance.IsPlayer() && WonderlandConfig.FallDamageMultiplier != null)
            {
                hitDistance *= WonderlandConfig.FallDamageMultiplier.Value;
            }
        }
    }
}
