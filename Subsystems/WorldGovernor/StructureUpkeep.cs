using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// No-decay / auto-repair for building pieces: a ZDO-layer correction pass that resets WearNTear
    /// health back to its prefab max, rather than trying to intercept the decay tick itself
    /// (WearNTear.UpdateWear only ever runs on whichever client owns the piece, same client-owned-
    /// simulation constraint as everything else this plan works around). Harmless no-op for
    /// non-decaying pieces (stone/core wood already report health == max) and correct for the wood
    /// pieces that do take weather/support wear.
    ///
    /// Two passes, because a full-map scan on its own is far too slow to keep up with decay:
    ///  - a per-player pass over the only ground decay can happen on (UpdateWear early-outs on
    ///    ZNetScene.OutsideActiveArea, a 1.5-zone Chebyshev box around the owning client), which is
    ///    what actually makes the feature keep up; and
    ///  - a resumable background sweep of the whole sector array, to clear damage that predates the
    ///    feature or happened in a zone nobody has visited since.
    /// </summary>
    public static class StructureUpkeep
    {
        /// <summary>Prefab name hash -> that prefab's BASE WearNTear.m_health (no world-level bonus).</summary>
        private static readonly Dictionary<int, float> _baseHealth = new Dictionary<int, float>();
        private static ZdoSpatialQuery.PrefabSetSweeper _sweep;
        private static float _timer;
        private static readonly List<ZDO> _buffer = new List<ZDO>();
        private static readonly List<ZDO> _scratch = new List<ZDO>();

        /// <summary>
        /// Ceiling on RPC_HealthChanged broadcasts per sweep. The first sweep over a long-running
        /// world can turn up thousands of damaged pieces at once, and each one would otherwise become
        /// a routed packet to every connected peer in the same frame. A piece clipped here is still
        /// repaired in the ZDO - it just keeps its stale worn look until that zone next loads on the
        /// client, where WearNTear.Awake re-reads health from the ZDO anyway.
        /// </summary>
        private const int MaxHealthBroadcastsPerSweep = 256;

        private static int _broadcastBudget;
        private static int _broadcastsClipped;

        public static void Initialize()
        {
            _baseHealth.Clear();
            _sweep = null;
            if (ZNetScene.instance == null)
            {
                return;
            }

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                WearNTear wear = prefab.GetComponent<WearNTear>();
                if (wear == null || wear.m_health <= 0f)
                {
                    continue;
                }
                _baseHealth[prefab.name.GetStableHashCode()] = wear.m_health;
            }

            _sweep = new ZdoSpatialQuery.PrefabSetSweeper(_baseHealth.Keys);
            WonderlandDebug.LogInfo($"[StructureUpkeep] tracking {_baseHealth.Count} WearNTear-bearing prefab types.");
        }

        public static void OnUpdate(float dt)
        {
            if (_sweep == null || WonderlandConfig.StructureUpkeepEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.StructureUpkeepInterval?.Value ?? 60f))
            {
                return;
            }
            _timer = 0f;

            _broadcastBudget = MaxHealthBroadcastsPerSweep;
            _broadcastsClipped = 0;
            float multiplier = WorldLevelMultiplier();
            bool playerBuiltOnly = WonderlandConfig.StructureUpkeepPlayerBuiltOnly?.Value != false;

            int nearPlayers = RepairAroundPlayers(multiplier, playerBuiltOnly);
            int background = RepairFromBackgroundSweep(multiplier, playerBuiltOnly);

            if (nearPlayers + background > 0)
            {
                string clipped = _broadcastsClipped > 0
                    ? $"; {_broadcastsClipped} kept stale visuals until their zone reloads (broadcast cap)"
                    : "";
                WonderlandDebug.LogInfo(
                    $"[StructureUpkeep] repaired {nearPlayers + background} piece(s): " +
                    $"{nearPlayers} near players, {background} from the background sweep{clipped}.");
            }
        }

        /// <summary>
        /// The pass that does the real work. Decay is only ever applied by the client that owns the
        /// piece, and only inside ZNetScene's active area - a 1.5-zone Chebyshev box measured from the
        /// player's ZONE origin, so at most 96m + a 32m half-zone offset from the player themselves.
        /// Every piece that can have lost health since the last tick is therefore within one short
        /// radius query of somebody, which is a few sectors rather than a quarter-million.
        /// </summary>
        private static int RepairAroundPlayers(float multiplier, bool playerBuiltOnly)
        {
            float radius = WonderlandConfig.StructureUpkeepPlayerRadius?.Value ?? 128f;
            int repaired = 0;
            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                ZdoSpatialQuery.FindNear(character.Position, radius, _scratch);
                repaired += RepairAll(_scratch, multiplier, playerBuiltOnly);
            }
            return repaired;
        }

        private static int RepairFromBackgroundSweep(float multiplier, bool playerBuiltOnly)
        {
            _buffer.Clear();
            int budget = Mathf.Max(1, WonderlandConfig.StructureUpkeepSectorsPerSweep?.Value ?? 512);
            _sweep.Advance(budget, _buffer);
            return RepairAll(_buffer, multiplier, playerBuiltOnly);
        }

        private static int RepairAll(List<ZDO> zdos, float multiplier, bool playerBuiltOnly)
        {
            int repaired = 0;
            for (int i = 0; i < zdos.Count; i++)
            {
                if (Repair(zdos[i], multiplier, playerBuiltOnly))
                {
                    repaired++;
                }
            }
            return repaired;
        }

        /// <summary>
        /// WearNTear.Awake folds the world-level bonus into its own m_health; a prefab never runs
        /// Awake, so a prefab's m_health is always the base value. Repairing to that base on a
        /// world-level server would leave every piece permanently below the client's real max -
        /// visibly damaged, and re-written on every single sweep for ever. Read live rather than
        /// cached at Initialize: world level is not necessarily settled when ZNetScene.Awake fires.
        /// </summary>
        private static float WorldLevelMultiplier()
        {
            if (Game.m_worldLevel <= 0 || Game.instance == null)
            {
                return 1f;
            }
            return 1f + Game.m_worldLevel * Game.instance.m_worldLevelPieceHPMultiplier;
        }

        private static bool Repair(ZDO zdo, float multiplier, bool playerBuiltOnly)
        {
            if (zdo == null || !zdo.IsValid())
            {
                return false;
            }
            if (!_baseHealth.TryGetValue(zdo.GetPrefab(), out float baseHealth))
            {
                return false;
            }

            // Piece.IsPlacedByPlayer() is exactly this test. World-gen ruins are spawned deliberately
            // pre-damaged (WearNTear.Awake randomises them to 10-60% health while the location being
            // placed has m_randomInitialDamage set), so repairing them would quietly turn every
            // abandoned village and dungeon on the map pristine. They are also the overwhelming
            // majority of WearNTear ZDOs in a world, so skipping them is most of the sweep's cost.
            if (playerBuiltOnly && zdo.GetLong(ZDOVars.s_creator, 0L) == 0L)
            {
                return false;
            }

            float max = baseHealth * multiplier;
            float current = zdo.GetFloat(ZDOVars.s_health, max);
            if (current >= max - 0.01f)
            {
                return false;
            }

            // No SetOwner here, deliberately. ZDO.Set needs no ownership - it writes through
            // ZDOExtraData, bumps the data revision and marks the sector dirty, and ZDOMan replicates
            // it like any other change. Taking ownership instead actively breaks the game: a server
            // owning a piece it has no instance of means ZNetView.InvokeRPC (which routes to
            // ZDO.GetOwner()) sends the player's own hammer RPC_Repair to a machine with nothing
            // registered to receive it, so manual repair silently stops working until the client
            // claims the ZDO back through ReleaseNearbyZDOS.
            zdo.Set(ZDOVars.s_health, max);

            // The owning client caches health in WearNTear.m_healthPercentage and only ever refreshes
            // it in Awake or RPC_HealthChanged. Without this the piece keeps its worn material and
            // damaged hover text, and - because UpdateWear gates rain damage on
            // GetHealthPercentage() > 0.5f - goes on behaving as if it were still at half health.
            // Routing the vanilla RPC by ZDOID reaches whichever client has the piece instantiated
            // without the server needing an instance of its own; ZRoutedRpc.HandleRoutedRPC drops it
            // harmlessly wherever no instance exists, including here on the server.
            if (_broadcastBudget > 0)
            {
                _broadcastBudget--;
                ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, zdo.m_uid, "RPC_HealthChanged", max);
            }
            else
            {
                _broadcastsClipped++;
            }
            return true;
        }
    }
}
