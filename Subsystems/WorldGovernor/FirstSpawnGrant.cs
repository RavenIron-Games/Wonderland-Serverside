using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// One-time starter kit + labeled boat, granted the first time a character is ever seen connected
    /// with no Wonderland_StarterGranted flag on its own ZDO. Both are granted the same way: new,
    /// independent ZDOs created near the player's own spawn position (ground ItemDrops for the kit,
    /// a fresh hull ZDO for the boat) rather than anything written into the player's own inventory -
    /// see the plan's Context section for why that path does not exist. A player's own character ZDO
    /// is never touched with SetOwner here: ZDO.Set does not require ownership to apply locally and
    /// still replicate (confirmed against the decompile - it only bumps DataRevision), and forcibly
    /// reclaiming ownership of a connected player's own character would risk breaking their client's
    /// ability to keep simulating it. Duplicate-safe via the flag field plus a short-lived in-memory
    /// per-session lock guarding a rapid reconnect racing itself; written after the grant succeeds so
    /// a crash mid-grant fails toward "try again next connect", never "granted twice".
    /// </summary>
    public static class FirstSpawnGrant
    {
        private const string GrantedField = "Wonderland_StarterGranted";
        private static readonly HashSet<ZDOID> _lockedThisSession = new HashSet<ZDOID>();

        public static void OnUpdate()
        {
            if (WonderlandConfig.StarterGrantEnabled?.Value != true)
            {
                return;
            }

            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null || player.m_nview == null)
                {
                    continue;
                }
                ZDO zdo = player.m_nview.GetZDO();
                if (zdo == null || !zdo.IsValid())
                {
                    continue;
                }
                if (zdo.GetBool(GrantedField) || _lockedThisSession.Contains(zdo.m_uid))
                {
                    continue;
                }

                _lockedThisSession.Add(zdo.m_uid);
                Grant(player, zdo);
            }
        }

        private static void Grant(Player player, ZDO zdo)
        {
            Vector3 pos = player.transform.position;
            string playerName = player.GetPlayerName();

            GrantKit(pos);
            GrantBoat(pos, playerName);

            zdo.Set(GrantedField, true);
            WonderlandDebug.LogInfo($"[FirstSpawnGrant] granted starter kit + boat to '{playerName}' at {pos}.");
        }

        private static void GrantKit(Vector3 pos)
        {
            string spec = WonderlandConfig.StarterKitItems?.Value ?? "";
            foreach (string entry in spec.Split(','))
            {
                string[] parts = entry.Trim().Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out int amount) || amount <= 0)
                {
                    continue;
                }
                string prefabName = parts[0].Trim();
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
                ItemDrop dropTemplate = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (dropTemplate == null || dropTemplate.m_itemData?.m_shared == null)
                {
                    WonderlandDebug.LogWarning($"[FirstSpawnGrant] starter kit entry '{entry}' does not name a real item prefab - skipped.");
                    continue;
                }

                ItemDrop.ItemData itemData = dropTemplate.m_itemData.Clone();
                Vector2 offset = Random.insideUnitCircle * 0.6f;
                ItemDrop.DropItem(itemData, amount, pos + new Vector3(offset.x, 0.3f, offset.y), Quaternion.identity);
                ItemLedger.RecordTransfer("FirstSpawnGrant", itemData.m_shared.m_name, amount);
            }
        }

        private static void GrantBoat(Vector3 pos, string playerName)
        {
            string hullPrefabName = WonderlandConfig.StarterBoatPrefab?.Value ?? "Karve";
            GameObject hullPrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hullPrefabName) : null;
            if (hullPrefab == null)
            {
                WonderlandDebug.LogWarning($"[FirstSpawnGrant] starter boat prefab '{hullPrefabName}' not found - no boat granted.");
                return;
            }

            Vector3 waterPos = FindNearbyWater(pos, WonderlandConfig.StarterBoatSearchRadius?.Value ?? 60f);
            GameObject boat = Object.Instantiate(hullPrefab, waterPos, Quaternion.identity);
            ZNetView view = boat.GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null)
            {
                view.GetZDO().Set("Wonderland_BoatOwner", playerName);
            }
        }

        private static Vector3 FindNearbyWater(Vector3 center, float searchRadius)
        {
            if (WorldGenerator.instance == null)
            {
                return center;
            }

            const int attempts = 24;
            for (int i = 0; i < attempts; i++)
            {
                float angle = i * (360f / attempts) * Mathf.Deg2Rad;
                float dist = searchRadius * ((i % 4) + 1) / 4f;
                float x = center.x + Mathf.Cos(angle) * dist;
                float z = center.z + Mathf.Sin(angle) * dist;
                float height = WorldGenerator.instance.GetHeight(x, z);
                if (height < ZoneSystem.c_WaterLevel - 1f)
                {
                    return new Vector3(x, ZoneSystem.c_WaterLevel, z);
                }
            }

            WonderlandDebug.LogWarning($"[FirstSpawnGrant] no water found within {searchRadius}m of spawn - placing boat at spawn position instead.");
            return center;
        }
    }
}
