using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// No-decay / auto-repair for building pieces: a periodic ZDO-layer correction pass that resets
    /// WearNTear health back to its prefab max, rather than trying to intercept the decay tick itself
    /// (WearNTear.UpdateWear only ever runs on whichever client owns the piece, same client-owned-
    /// simulation constraint as everything else this plan works around). Harmless no-op for
    /// non-decaying pieces (stone/core wood already report health == max) and correct for the wood
    /// pieces that do take weather/support wear.
    /// </summary>
    public static class StructureUpkeep
    {
        private static ZdoSpatialQuery.PrefabSetScanner _scanner;
        private static float _timer;
        private static readonly List<ZDO> _buffer = new List<ZDO>();

        public static void Initialize()
        {
            var pieceNames = new List<string>();
            if (ZNetScene.instance != null)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab != null && prefab.GetComponent<WearNTear>() != null)
                    {
                        pieceNames.Add(prefab.name);
                    }
                }
            }
            _scanner = new ZdoSpatialQuery.PrefabSetScanner(pieceNames);
            WonderlandDebug.LogInfo($"[StructureUpkeep] tracking {pieceNames.Count} WearNTear-bearing prefab types.");
        }

        public static void OnUpdate(float dt)
        {
            if (_scanner == null || WonderlandConfig.StructureUpkeepEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.StructureUpkeepInterval?.Value ?? 60f))
            {
                return;
            }
            _timer = 0f;

            _buffer.Clear();
            int budget = Mathf.Max(1, WonderlandConfig.StructureUpkeepBatchSize?.Value ?? 50);
            for (int i = 0; i < budget; i++)
            {
                _scanner.Advance(_buffer);
            }

            foreach (ZDO zdo in _buffer)
            {
                Repair(zdo);
            }
        }

        private static void Repair(ZDO zdo)
        {
            if (!zdo.IsValid())
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            WearNTear template = prefab != null ? prefab.GetComponent<WearNTear>() : null;
            if (template == null)
            {
                return;
            }

            float current = zdo.GetFloat(ZDOVars.s_health, template.m_health);
            if (current >= template.m_health - 0.01f)
            {
                return;
            }

            zdo.SetOwner(ZNet.GetUID());
            zdo.Set(ZDOVars.s_health, template.m_health);
        }
    }
}
