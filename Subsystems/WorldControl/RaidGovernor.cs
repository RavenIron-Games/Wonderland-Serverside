using System;
using System.Collections.Generic;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldControl
{
    public static class RaidGovernor
    {
        public static bool IsRaidAllowedInBiome(string eventName, Heightmap.Biome biome)
        {
            if (WonderlandConfig.BlockHighTierRaidsInLowBiomes == null || !WonderlandConfig.BlockHighTierRaidsInLowBiomes.Value) return true;

            bool isLowBiome = (biome == Heightmap.Biome.Meadows || biome == Heightmap.Biome.BlackForest);
            if (!isLowBiome) return true;

            string lowerEvent = eventName.ToLowerInvariant();
            if (lowerEvent.Contains("seeker") || lowerEvent.Contains("charred") || lowerEvent.Contains("fulling") || lowerEvent.Contains("gjall"))
            {
                WonderlandDebug.LogInfo($"Blocked high-tier raid '{eventName}' in low-tier biome '{biome}'");
                return false;
            }

            return true;
        }
    }
}
