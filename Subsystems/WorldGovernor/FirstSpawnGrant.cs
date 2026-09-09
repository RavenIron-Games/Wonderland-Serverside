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

        private const float StuckWarnSeconds = 60f;

        private static readonly HashSet<long> _lockedThisSession = new HashSet<long>();
        private static readonly Dictionary<long, float> _pendingPlayerWait = new Dictionary<long, float>();
        private static readonly HashSet<long> _warnedStuck = new HashSet<long>();
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
                    _pendingPlayerWait.Remove(playerId);
                    _warnedStuck.Remove(playerId);
                    continue;
                }

                Vector3 pos = character.Position;

                // The wait accrues before any skip below. This used to sit after the origin check, so a
                // character whose reported position never left the origin was skipped on every single
                // poll with no timeout and never granted anything - unlike the "not arrived" branch
                // further down, which always had its 60s escape.
                if (!_pendingPlayerWait.TryGetValue(playerId, out float waitTime))
                {
                    waitTime = 0f;
                }
                waitTime += PollInterval;
                _pendingPlayerWait[playerId] = waitTime;

                if (pos.sqrMagnitude < 4f)
                {
                    // Granting here would drop the kit at the world origin, so this still has to wait -
                    // but it must not do so silently and forever.
                    if (waitTime >= StuckWarnSeconds && _warnedStuck.Add(playerId))
                    {
                        WonderlandDebug.LogWarning($"[FirstSpawnGrant] '{character.Name}' (playerID {playerId}) has reported a position at the world origin for {waitTime:F0}s - the starter grant cannot fire until a real position arrives.");
                    }
                    continue;
                }

                // Verify the player has completed the Valkyrie intro flight and touched down on the ground,
                // or skipped the intro and spawned directly at the StartTemple.
                // The Valkyrie carries the player at 50m-500m altitude, 500m away.
                // When skipping the intro, the client teleports/spawns directly at the StartTemple (pos + 2m).

                bool arrivedAtSpawn = false;

                // 1. Direct temple proximity: when a player skips the intro, Game.SpawnPlayer places them
                // directly at StartTemple + Vector3.up * 2f.
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetLocationIcon("StartTemple", out Vector3 templePos))
                {
                    float horizontalDist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(templePos.x, templePos.z));
                    if (horizontalDist < 35f && Mathf.Abs(pos.y - templePos.y) < 15f)
                    {
                        arrivedAtSpawn = true;
                    }
                }

                // 2. Ground elevation check: verifies the player is standing on terrain, altar stones, or shallow water
                if (!arrivedAtSpawn && WorldGenerator.instance != null)
                {
                    float ground = WorldGenerator.instance.GetHeight(pos.x, pos.z);
                    float diff = pos.y - ground;
                    if (diff >= -4f && diff <= 8f)
                    {
                        arrivedAtSpawn = true;
                    }
                }

                // If not yet arrived/landed, wait until touchdown or up to 60 seconds fallback timeout
                if (!arrivedAtSpawn && waitTime < 60f)
                {
                    continue;
                }

                _lockedThisSession.Add(playerId);
                _pendingPlayerWait.Remove(playerId);
                _warnedStuck.Remove(playerId);
                Grant(character, playerId);
            }
        }

        private static void Grant(ConnectedCharacter character, long playerId)
        {
            Vector3 pos = character.Position;
            string playerName = character.Name;

            GrantKit(pos);
            GrantBoat(pos, playerName, character);

            ZoneSystem.instance.SetGlobalKey(KeyPrefix + playerId);
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
                float dropX = pos.x + offset.x;
                float dropZ = pos.z + offset.y;
                float groundY = WorldGenerator.instance != null
                    ? WorldGenerator.instance.GetHeight(dropX, dropZ)
                    : pos.y;
                Vector3 dropPos = new Vector3(dropX, Mathf.Max(pos.y, groundY) + 0.35f, dropZ);
                ItemDrop.DropItem(itemData, amount, dropPos, Quaternion.identity);
                ItemLedger.RecordTransfer("FirstSpawnGrant", itemData.m_shared.m_name, amount);
            }
        }

        private static void GrantBoat(Vector3 pos, string playerName, ConnectedCharacter character)
        {
            string hullPrefabName = WonderlandConfig.StarterBoatPrefab?.Value ?? "Karve";
            GameObject hullPrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hullPrefabName) : null;
            if (hullPrefab == null)
            {
                WonderlandDebug.LogWarning($"[FirstSpawnGrant] starter boat prefab '{hullPrefabName}' not found - no boat granted.");
                return;
            }

            float configuredRadius = WonderlandConfig.StarterBoatSearchRadius?.Value ?? 300f;
            Vector3 waterPos = FindNearbyWater(pos, configuredRadius);

            Vector3 seaward = waterPos - pos;
            seaward.y = 0f;
            Quaternion rot = seaward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(seaward.normalized) : Quaternion.identity;

            GameObject boat = Object.Instantiate(hullPrefab, waterPos, rot);
            ZNetView view = boat.GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null)
            {
                view.GetZDO().Set("Wonderland_BoatOwner", playerName);
            }

            float dist = Vector3.Distance(pos, waterPos);
            if (waterPos != pos)
            {
                WonderlandDebug.LogAlways($"[FirstSpawnGrant] granted starter kit to '{playerName}' (playerID {character.PlayerId}) at {pos}, and placed {hullPrefabName} in water at {waterPos} ({dist:F0}m away).");

                // Vanilla client map pin discovery (pure server routed RPC - vanilla clients receive it cleanly)
                if (WonderlandConfig.StarterBoatMapPin?.Value != false && character.Peer != null && character.Peer.m_uid != 0L)
                {
                    try
                    {
                        ZRoutedRpc.instance?.InvokeRoutedRPC(
                            character.Peer.m_uid,
                            "RPC_DiscoverLocationResponse",
                            $"{playerName}'s {hullPrefabName}",
                            (int)Minimap.PinType.Icon3,
                            waterPos,
                            false);
                    }
                    catch (System.Exception ex)
                    {
                        WonderlandDebug.LogWarning($"[FirstSpawnGrant] could not send boat map pin to '{playerName}': {ex.Message}");
                    }
                }
            }
            else
            {
                WonderlandDebug.LogWarning($"[FirstSpawnGrant] granted starter kit to '{playerName}' at {pos}, but no water was found within search limits - placing {hullPrefabName} at their position instead.");
            }
        }

        private static Vector3 FindNearbyWater(Vector3 center, float searchRadius)
        {
            if (WorldGenerator.instance == null)
            {
                return center;
            }

            float waterLevel = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : ZoneSystem.c_WaterLevel;
            const float minDepth = 1.1f;          // Karve draft is ~0.6m; 1.1m allows floating freely without beaching
            const float clearanceRadius = 2.0f;   // Check 2.0m cardinal clearance around candidate for open water
            const float ringStep = 5f;            // 5m fine-grained step to discover immediate shorelines
            const int bearings = 48;              // Every 7.5 degrees to catch nearby rivers, coves, and coastlines

            // Primary search within configured search radius (minimum 60m)
            float maxSearch = Mathf.Max(searchRadius, 60f);
            if (TryScanRings(center, 10f, maxSearch, ringStep, bearings, waterLevel, minDepth, clearanceRadius, out Vector3 found))
            {
                return found;
            }

            // Fallback expanding search: expand up to 1200m so we never drop the boat on dry land
            const float fallbackLimit = 1200f;
            if (maxSearch < fallbackLimit)
            {
                WonderlandDebug.LogInfo($"[FirstSpawnGrant] no water found within configured radius {maxSearch:F0}m - expanding search up to {fallbackLimit:F0}m...");
                if (TryScanRings(center, maxSearch + ringStep, fallbackLimit, ringStep * 1.5f, bearings, waterLevel, minDepth, clearanceRadius, out Vector3 fallbackFound))
                {
                    return fallbackFound;
                }
            }

            WonderlandDebug.LogWarning($"[FirstSpawnGrant] no suitable water found within {fallbackLimit:F0}m of the player - placing boat at their position instead.");
            return center;
        }

        private static bool TryScanRings(
            Vector3 center,
            float startRadius,
            float endRadius,
            float step,
            int bearings,
            float waterLevel,
            float minDepth,
            float clearanceRadius,
            out Vector3 bestPos)
        {
            bestPos = center;

            for (float r = startRadius; r <= endRadius; r += step)
            {
                Vector3 candidate = Vector3.zero;
                float bestDepth = 0f;

                for (int i = 0; i < bearings; i++)
                {
                    float angle = i * (360f / bearings) * Mathf.Deg2Rad;
                    float x = center.x + Mathf.Cos(angle) * r;
                    float z = center.z + Mathf.Sin(angle) * r;
                    float height = WorldGenerator.instance.GetHeight(x, z);
                    float depth = waterLevel - height;

                    if (depth >= minDepth)
                    {
                        // Check surrounding clearance at cardinal offsets to ensure this is submerged water,
                        // not straddling the dry beach or a rock wall. Three of the four is enough: a boat
                        // sitting in a cove or against a shoreline has one dry side by definition, and
                        // demanding all four is what pushed starter boats hundreds of metres out to sea
                        // past perfectly usable water near spawn.
                        float hN = WorldGenerator.instance.GetHeight(x, z + clearanceRadius);
                        float hS = WorldGenerator.instance.GetHeight(x, z - clearanceRadius);
                        float hE = WorldGenerator.instance.GetHeight(x + clearanceRadius, z);
                        float hW = WorldGenerator.instance.GetHeight(x - clearanceRadius, z);

                        int openSides = 0;
                        if ((waterLevel - hN) > 0.15f) openSides++;
                        if ((waterLevel - hS) > 0.15f) openSides++;
                        if ((waterLevel - hE) > 0.15f) openSides++;
                        if ((waterLevel - hW) > 0.15f) openSides++;

                        if (openSides >= 3)
                        {
                            // In this ring, prefer a spot with comfortable depth (~1.5m to ~2.5m)
                            if (candidate == Vector3.zero || Mathf.Abs(depth - 1.8f) < Mathf.Abs(bestDepth - 1.8f))
                            {
                                candidate = new Vector3(x, waterLevel, z);
                                bestDepth = depth;
                            }
                        }
                    }
                }

                if (candidate != Vector3.zero)
                {
                    bestPos = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
