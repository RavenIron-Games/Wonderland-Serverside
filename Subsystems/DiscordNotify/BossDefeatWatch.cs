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
    /// GlobalKeyAdd fires for far more than boss kills (world-rate keys, per-player tracking keys, ...),
    /// so every call is filtered against a small known map first before doing anything else.
    ///
    /// Critical correctness point, decompile-confirmed: every server boot re-adds EVERY persisted global
    /// key through GlobalKeyAdd, AFTER ZNetScene.Awake (the OnWorldReady hook). The real boot order is
    /// ZNet.Start -&gt; ServerLoadWorld -&gt; LoadWorld -&gt; ZoneSystem.Load (GlobalKeyAdd per key saved
    /// in the .db, which is where defeated_* live - ZoneSystem.Save strips the server-option enum keys)
    /// -&gt; WorldSetup -&gt; ZoneSystem.SetStartingGlobalKeys (GlobalKeyAdd per .fwl server-option key)
    /// -&gt; OnWorldSaveLoaded. A snapshot taken from OnWorldReady is
    /// therefore always empty and the first re-added boss key gets announced on every restart (the
    /// "boss defeated: Eikthyr" on every boot seen in the live log). So the snapshot is taken from
    /// DiscordNotifySubsystem.OnWorldLoaded (ZNet.ServerLoadWorld postfix, after every load path), and
    /// the postfix refuses to announce anything until that has happened - key re-adds during load are
    /// ignored even if the ordering ever shifts again.
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
        private static bool _worldLoaded;

        /// <summary>Pre-seeds the "already announced" set with every boss key already true once the
        /// world has finished loading, and only then arms the postfix.</summary>
        /// <summary>World progress right now, from the same keys: (defeated, remaining) display names.</summary>
        public static (List<string> defeated, List<string> remaining) Snapshot()
        {
            var defeated = new List<string>();
            var remaining = new List<string>();
            foreach (KeyValuePair<string, string> kv in BossDisplayNames)
            {
                bool done = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(kv.Key);
                (done ? defeated : remaining).Add(kv.Value);
            }
            return (defeated, remaining);
        }

        public static void SnapshotExistingBossKeys()
        {
            Announced.Clear();
            if (ZoneSystem.instance != null)
            {
                foreach (string key in BossDisplayNames.Keys)
                {
                    if (ZoneSystem.instance.GetGlobalKey(key))
                    {
                        Announced.Add(key);
                    }
                }
            }

            _worldLoaded = true;
        }

        [HarmonyPostfix]
        public static void Postfix(string keyStr)
        {
            if (!_worldLoaded || string.IsNullOrEmpty(keyStr))
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
            BarrkBot.BarrkBotStats.OnBossDefeated(bossName);
        }
    }
}
