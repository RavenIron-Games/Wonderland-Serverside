using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// Reactive night-spawn blocking in configured biomes. SpawnSystem.UpdateSpawning - vanilla's own
    /// ambient-spawn driver - is gated on Player.m_localPlayer, so it runs on each connected player's
    /// own client, never on the dedicated server; there is nothing server-side to prevent there. What
    /// the server *does* see is every ZDO a client creates, the moment it arrives in ZDOMan.RPC_ZDOData.
    /// Destroying a freshly-registered hostile creature's ZDO right there is the server-side equivalent
    /// of "this never spawned", without ever intercepting the spawn decision itself.
    ///
    /// Two hooks, not one, because of the order RPC_ZDOData does things (read straight from the 1.0.7
    /// server decompile): for an unknown ZDOID it calls the private CreateNewZDO(ZDOID, Vector3, int)
    /// overload with prefabHash 0, and only afterwards sets the owner, the position, and finally calls
    /// zdo.Deserialize(pkg) - which is where the prefab hash actually arrives. A postfix on CreateNewZDO
    /// alone therefore always sees GetPrefab() == 0 and can never act (the first version of this file had
    /// exactly that bug). So CreateNewZDO's postfix only *remembers* the new ZDOID, and ZDO.Deserialize's
    /// postfix - the first moment the prefab is known - does the actual check, for that ZDOID only.
    /// prefabHash 0 is what distinguishes the network-receive path from the server's own public
    /// CreateNewZDO(Vector3, int) wrapper, which always passes a real hash.
    ///
    /// The destroy itself mirrors vanilla's own dead-ZDO branch in the same method: SetOwner to this
    /// session first, then DestroyZDO. DestroyZDO is a no-op unless the caller owns the ZDO, and by the
    /// time Deserialize runs RPC_ZDOData has already handed ownership to the client that spawned it.
    /// </summary>
    [HarmonyPatch]
    public static class SpawnGovernor
    {
        /// <summary>ZDOIDs created via the network-receive path, awaiting the Deserialize that names their prefab.</summary>
        private static readonly HashSet<ZDOID> _pendingNew = new HashSet<ZDOID>();

        /// <summary>
        /// RPC_ZDOData always Deserializes immediately after CreateNewZDO, so this set is drained within
        /// the same call and never grows in practice. The cap is a belt-and-braces bound so an unexpected
        /// code path can never turn it into a leak.
        /// </summary>
        private const int PendingCap = 4096;

        [HarmonyPatch(typeof(ZDOMan), "CreateNewZDO", typeof(ZDOID), typeof(Vector3), typeof(int))]
        [HarmonyPostfix]
        public static void Postfix_CreateNewZDO(ZDO __result, [HarmonyArgument(2)] int prefabHash)
        {
            if (__result == null || prefabHash != 0 || WonderlandConfig.NightSpawnBlockEnabled?.Value != true)
            {
                return;
            }
            if (_pendingNew.Count >= PendingCap)
            {
                _pendingNew.Clear();
            }
            _pendingNew.Add(__result.m_uid);
        }

        [HarmonyPatch(typeof(ZDO), "Deserialize", typeof(ZPackage))]
        [HarmonyPostfix]
        public static void Postfix_Deserialize(ZDO __instance)
        {
            // Fast path for the overwhelmingly common case - an update to a ZDO the server already knew.
            if (_pendingNew.Count == 0 || !_pendingNew.Remove(__instance.m_uid))
            {
                return;
            }
            Evaluate(__instance);
        }

        private static void Evaluate(ZDO zdo)
        {
            if (WonderlandConfig.NightSpawnBlockEnabled?.Value != true || ZNetScene.instance == null || ZDOMan.instance == null)
            {
                return;
            }
            if (!EnvMan.IsNight())
            {
                return;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            if (prefab == null || !IsBlockedCreature(prefab.name))
            {
                return;
            }

            if (WorldGenerator.instance == null || !IsBiomeBlocked(WorldGenerator.instance.GetBiome(zdo.GetPosition())))
            {
                return;
            }

            WonderlandDebug.LogInfo($"[SpawnGovernor] destroyed newly-spawned '{prefab.name}' at {zdo.GetPosition()} - blocked nighttime tier in a blocked biome.");
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(zdo);
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
