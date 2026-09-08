using System;
using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.InventoryControl.Patches
{
    [HarmonyPatch]
    public static class InventoryPatches
    {
        [HarmonyPatch(typeof(ItemDrop), "Awake")]
        [HarmonyPostfix]
        public static void Postfix_ItemDropAwake(ItemDrop __instance)
        {
            if (WonderlandConfig.StackMaxMultiplier != null && WonderlandConfig.StackMaxMultiplier.Value > 1.0f)
            {
                if (__instance.m_itemData != null && __instance.m_itemData.m_shared != null && __instance.m_itemData.m_shared.m_maxStackSize > 1)
                {
                    __instance.m_itemData.m_shared.m_maxStackSize = Mathf.CeilToInt(__instance.m_itemData.m_shared.m_maxStackSize * WonderlandConfig.StackMaxMultiplier.Value);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
        [HarmonyPostfix]
        public static void Postfix_GetMaxCarryWeight(Player __instance, ref float __result)
        {
            if (WonderlandConfig.CarryWeightBonus != null && WonderlandConfig.CarryWeightBonus.Value > 0f)
            {
                __result += WonderlandConfig.CarryWeightBonus.Value;
            }
        }
    }
}
