using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldControl.Patches
{
    [HarmonyPatch]
    public static class RaidPatches
    {
        [HarmonyPatch(typeof(RandEventSystem), "SetRandomEvent")]
        [HarmonyPrefix]
        public static bool Prefix_SetRandomEvent(RandEventSystem __instance, RandomEvent ev, Vector3 pos)
        {
            if (ev != null && WorldGenerator.instance != null)
            {
                Heightmap.Biome biome = WorldGenerator.instance.GetBiome(pos);
                if (!RaidGovernor.IsRaidAllowedInBiome(ev.m_name, biome))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
