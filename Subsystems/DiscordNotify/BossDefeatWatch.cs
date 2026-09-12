using System.Collections.Generic;
using HarmonyLib;
using Wonderland.Core;

namespace Wonderland.Subsystems.DiscordNotify
{
    /// <summary>
    /// Announces the five classic boss defeats from the same native world-global-key mechanism class as
    /// GlobalKeys.CarryWeightRate: vanilla's own boss-death code marks a kill by calling
    /// ZoneSystem.GlobalKeyAdd("defeated_eikthyr", ...) (etc.) - a plain private method with exactly one
    /// overload, patched here by name.
    ///
    /// GlobalKeyAdd fires for far more than boss kills (world-rate keys, per-player tracking keys, this
    /// mod's own DiscordNotify "seen" marker, ...), so every call is filtered against a small known map
    /// first before doing anything else.
    ///
    /// Critical correctness point, decompile-confirmed in ZoneSystem.SetStartingGlobalKeys: every server
    /// boot re-adds EVERY already-persisted global key by calling GlobalKeyAdd for each one in
    /// ZNet.World.m_startingGlobalKeys - including bosses killed in a past session. A naive postfix would
    /// re-announce every historical boss kill on every single restart. SnapshotExistingBossKeys (called
    /// once from OnWorldReady, after ZoneSystem.Start has already run per the established
    /// WorldRatesEngine.OnWorldReady precedent) pre-seeds the "already announced" set with whatever is
    /// already true at boot, so only a GENUINELY new defeat during this session fires.
    /// </summary>
    [HarmonyPatch(typeof(ZoneSystem), "GlobalKeyAdd")]
    public static class BossDefeatWatch
    {
        private static readonly Dictionary<string, string> BossDisplayNames = new Dictionary<string, string>
        {
            { "defeated_eikthyr", "Eikthyr" },
            { "defeated_gdking", "The Elder" },
            { "defeated_bonemass", "Bonemass" },
            { "defeated_dragon", "Moder" },
            { "defeated_goblinking", "Yagluth" },
        };

        private static readonly HashSet<string> Announced = new HashSet<string>();

        public static void SnapshotExistingBossKeys()
        {
            Announced.Clear();
            if (ZoneSystem.instance == null)
            {
                return;
            }

            foreach (string key in BossDisplayNames.Keys)
            {
                if (ZoneSystem.instance.GetGlobalKey(key))
                {
                    Announced.Add(key);
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(string keyStr)
        {
            if (string.IsNullOrEmpty(keyStr))
            {
                return;
            }

            string bare = keyStr.ToLowerInvariant().Split(' ')[0];
            if (!BossDisplayNames.TryGetValue(bare, out string bossName))
            {
                return;
            }

            if (!Announced.Add(bare))
            {
                return; // already true at boot, or already announced this session
            }

            DiscordNotifySubsystem.AnnounceBossDefeat(bossName);
        }
    }
}
