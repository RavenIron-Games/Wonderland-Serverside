using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.CraftingControl.Patches
{
    [HarmonyPatch]
    public static class CraftingPatches
    {
        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.CheckUsable))]
        [HarmonyPrefix]
        public static bool Prefix_CheckUsable(CraftingStation __instance, Player player, bool showMessage, ref bool __result)
        {
            if (WonderlandConfig.WorkstationNoRoof != null && WonderlandConfig.WorkstationNoRoof.Value)
            {
                // Force station usable without roof
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CraftingStation), "Awake")]
        [HarmonyPostfix]
        public static void Postfix_CraftingStationAwake(CraftingStation __instance)
        {
            if (WonderlandConfig.WorkstationExtraRange != null && WonderlandConfig.WorkstationExtraRange.Value > 20f)
            {
                __instance.m_rangeBuild = WonderlandConfig.WorkstationExtraRange.Value;
            }
        }

        public static List<Container> GetNearbyContainers(Vector3 position, float radius)
        {
            List<Container> containers = new List<Container>();
            Collider[] colliders = Physics.OverlapSphere(position, radius, LayerMask.GetMask("piece", "piece_nonsolid"));

            foreach (var col in colliders)
            {
                Container c = col.GetComponentInParent<Container>();
                if (c != null && c.GetInventory() != null && !c.IsInUse())
                {
                    containers.Add(c);
                }
            }
            return containers;
        }
    }
}
