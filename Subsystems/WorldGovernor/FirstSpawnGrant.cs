using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// One-time starter kit + labeled boat, granted the first time a character is seen connected to this
    /// world. Both are granted the same way: new, independent ZDOs created at the player's current
    /// position (ground ItemDrops for the kit, a fresh hull ZDO for the boat) rather than anything written
    /// into the player's own inventory - that path does not exist, a character's bag is never networked.
    ///
    /// Players are found through ConnectedCharacters (peer + character ZDO), never Player.GetAllPlayers(),
    /// which is always empty on a dedicated server. Identity is s_playerID - the stable per-character id
    /// PLAYER-IDENTITY-FACTS.md says to persist - not the peer's connection id and not the character
    /// ZDOID, which is regenerated on every login.
    ///
    /// The once-only guarantee is a world global key ("wonderland_starter_&lt;playerID&gt;"). The first
    /// version of this file wrote a flag onto the character ZDO instead; that ZDO is per-session (a new
    /// one is created on every connect and discarded on disconnect), so the flag vanished with it and the
    /// grant would have repeated on every login. Global keys are saved in the world file itself, so the
    /// record travels with the world and its backups. The key is written after the grant succeeds, with a
    /// short in-memory per-session lock guarding a rapid reconnect racing itself, so a crash mid-grant
    /// fails toward "try again next connect", never "granted twice". On the server, SetGlobalKey routes to
    /// the server's own handler synchronously, so the key is readable the moment it is set.
    /// </summary>
    public static class FirstSpawnGrant
    {
        private const string KeyPrefix = "wonderland_starter_";
        private const float PollInterval = 2f;

        private static readonly HashSet<long> _lockedThisSession = new HashSet<long>();
        private static float _timer;

        public static void OnUpdate(float dt)
        {
            if (WonderlandConfig.StarterGrantEnabled?.Value != true || ZoneSystem.instance == null)
            {
                return;
            }

            _timer += dt;
            if (_timer < PollInterval)
            {
                return;
            }
            _timer = 0f;

            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                long playerId = character.PlayerId;
                if (playerId == 0L)
                {
                    continue; // Player.SetPlayerID hasn't written the identity into the ZDO yet - next pass.
                }
                if (_lockedThisSession.Contains(playerId))
                {
                    continue;
                }
                if (ZoneSystem.instance.GetGlobalKey(KeyPrefix + playerId))
                {
                    _lockedThisSession.Add(playerId);
                    continue;
                }

                _lockedThisSession.Add(playerId);
                Grant(character, playerId);
            }
        }

        private static void Grant(ConnectedCharacter character, long playerId)
        {
            Vector3 pos = character.Position;
            string playerName = character.Name;

            GrantKit(pos);
            GrantBoat(pos, playerName);

            ZoneSystem.instance.SetGlobalKey(KeyPrefix + playerId);
            WonderlandDebug.LogAlways($"[FirstSpawnGrant] granted starter kit + boat to '{playerName}' (playerID {playerId}) at {pos}.");
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

                // A prefab's template ItemData only gets m_dropPrefab from ItemDrop.Awake, which never
                // runs for the prefab itself - and ItemDrop.DropItem instantiates from exactly that field.
                ItemDrop.ItemData itemData = dropTemplate.m_itemData.Clone();
                itemData.m_dropPrefab = prefab;
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

            WonderlandDebug.LogWarning($"[FirstSpawnGrant] no water found within {searchRadius}m of the player - placing boat at their position instead.");
            return center;
        }
    }
}
