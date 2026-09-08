using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// Blocks configured raid events from triggering in configured biomes. One of the few pieces of
    /// the old mod that was already architecturally sound: RandEventSystem.UpdateRandomEvent/
    /// SetRandomEvent is gated on ZNet.instance.IsServer(), genuinely server-authoritative, unlike
    /// almost everything else in the old codebase.
    /// </summary>
    [HarmonyPatch]
    public static class RaidGovernor
    {
        [HarmonyPatch(typeof(RandEventSystem), "SetRandomEvent")]
        [HarmonyPrefix]
        public static bool Prefix_SetRandomEvent(RandomEvent ev, Vector3 pos)
        {
            if (WonderlandConfig.RaidBlockEnabled?.Value != true || ev == null || WorldGenerator.instance == null)
            {
                return true;
            }

            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(pos);
            if (!IsBiomeBlocked(biome))
            {
                return true;
            }

            if (!IsEventBlocked(ev.m_name))
            {
                return true;
            }

            WonderlandDebug.LogInfo($"[RaidGovernor] blocked raid event '{ev.m_name}' in biome '{biome}'.");
            return false;
        }

        private static bool IsBiomeBlocked(Heightmap.Biome biome)
        {
            string list = WonderlandConfig.RaidBlockedBiomes?.Value ?? "";
            foreach (string entry in list.Split(','))
            {
                if (System.Enum.TryParse(entry.Trim(), true, out Heightmap.Biome parsed) && parsed == biome)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsEventBlocked(string eventName)
        {
            string list = WonderlandConfig.RaidBlockedEvents?.Value ?? "";
            string lowerEvent = eventName.ToLowerInvariant();
            foreach (string entry in list.Split(','))
            {
                string trimmed = entry.Trim();
                if (trimmed.Length > 0 && lowerEvent.Contains(trimmed.ToLowerInvariant()))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
