using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// Reactive night-spawn blocking in configured biomes. SpawnSystem.UpdateSpawning - vanilla's own
    /// ambient-spawn driver - is gated on Player.m_localPlayer, so it runs on each connected player's
    /// own client, never on the dedicated server; there is nothing server-side to prevent there. What
    /// the server *does* see is every newly-created ZDO as it learns about it (ZDOMan's private
    /// CreateNewZDO(ZDOID, Vector3, int) overload, used specifically for registering a ZDO the server
    /// didn't create itself - i.e. received from a client). Destroying a freshly-registered hostile
    /// creature's ZDO the instant the server learns about it is the server-side equivalent of "this
    /// never spawned", without ever needing to intercept the spawn decision itself.
    /// </summary>
    [HarmonyPatch]
    public static class SpawnGovernor
    {
        [HarmonyPatch(typeof(ZDOMan), "CreateNewZDO", typeof(ZDOID), typeof(Vector3), typeof(int))]
        [HarmonyPostfix]
        public static void Postfix_CreateNewZDO(ZDO __result)
        {
            if (WonderlandConfig.NightSpawnBlockEnabled?.Value != true || __result == null)
            {
                return;
            }
            if (!EnvMan.IsNight())
            {
                return;
            }

            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(__result.GetPrefab()) : null;
            if (prefab == null || !IsBlockedCreature(prefab.name))
            {
                return;
            }

            if (WorldGenerator.instance == null || !IsBiomeBlocked(WorldGenerator.instance.GetBiome(__result.GetPosition())))
            {
                return;
            }

            WonderlandDebug.LogInfo($"[SpawnGovernor] destroyed newly-spawned '{prefab.name}' at {__result.GetPosition()} - blocked nighttime tier in a blocked biome.");
            ZDOMan.instance.DestroyZDO(__result);
        }

        private static bool IsBiomeBlocked(Heightmap.Biome biome)
        {
            string list = WonderlandConfig.NightSpawnBlockedBiomes?.Value ?? "";
            foreach (string entry in list.Split(','))
            {
                if (System.Enum.TryParse(entry.Trim(), true, out Heightmap.Biome parsed) && parsed == biome)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsBlockedCreature(string prefabName)
        {
            string list = WonderlandConfig.NightSpawnBlockedCreatures?.Value ?? "";
            foreach (string entry in list.Split(','))
            {
                if (string.Equals(entry.Trim(), prefabName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
