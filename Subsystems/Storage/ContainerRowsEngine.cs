using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// The sweep that keeps every eligible chest's anchor row occupied (see ContainerRows for why a
    /// parked stack is what makes a vanilla client draw the extra rows). Round-robins the same
    /// container ZDO set the other engines use. A chest is written only when it has items, nobody has
    /// it open (ZDOVars.s_inUse, set by the opening client before any content edit) and its anchor row
    /// is empty - so after the first pass a chest is normally never written again until a player
    /// empties its bottom row. The write claims ownership for the moment of the commit like every
    /// other Wonderland container write; ZDOMan.ReleaseZDOS hands it back to the nearest player within
    /// about two seconds.
    /// </summary>
    public static class ContainerRowsEngine
    {
        private static ZdoSpatialQuery.PrefabSetScanner? _scanner;
        private static float _timer;
        private static readonly List<ZDO> _buffer = new List<ZDO>();
        private static int _anchoredTotal;

        private static bool _announced;
        private static float _unresolvedFor;

        public static void Initialize()
        {
            ContainerRows.ResetCache();
            _scanner = new ZdoSpatialQuery.PrefabSetScanner(ContainerRegistry.PrefabNames);
            _anchoredTotal = 0;
            _announced = false;
            _unresolvedFor = 0f;

            if (!ContainerRows.IsEnabled)
            {
                WonderlandDebug.LogAlways("[ContainerRows] disabled (ContainerRowsEnabled=false or ContainerRowMultiplier=1) - containers keep vanilla rows.");
            }
        }

        /// <summary>
        /// Logs the eligible set once the build tables can be read. Returns false until then, so no
        /// sweep runs against an unresolved eligibility set.
        /// </summary>
        private static bool Announce(float dt)
        {
            if (_announced)
            {
                return true;
            }
            if (!ContainerRows.TryResolveBuildable())
            {
                _unresolvedFor += dt;
                if (_unresolvedFor > 60f)
                {
                    WonderlandDebug.LogWarning("[ContainerRows] ObjectDB has no build tables after 60s - cannot tell player-built containers from world loot, so no container is being grown.");
                    _unresolvedFor = float.NegativeInfinity;
                }
                return false;
            }

            var eligible = new List<string>();
            foreach (string name in ContainerRegistry.PrefabNames)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                Container template = prefab != null ? prefab.GetComponent<Container>() : null;
                if (template != null && ContainerRows.IsEligible(prefab, template))
                {
                    (int w, int h) = ContainerRows.GetGridSize(prefab, template);
                    eligible.Add($"{name} {template.m_width}x{template.m_height}->{w}x{h}");
                }
            }
            WonderlandDebug.LogAlways($"[ContainerRows] x{ContainerRows.Multiplier:0.##} rows on {eligible.Count} of {ContainerRegistry.PrefabNames.Count} container type(s) (player-buildable only): {string.Join(", ", eligible)}");
            _announced = true;
            return true;
        }

        public static void OnUpdate(float dt)
        {
            if (_scanner == null || !ContainerRows.IsEnabled || !Announce(dt))
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.ContainerRowsInterval?.Value ?? 5f))
            {
                return;
            }
            _timer = 0f;

            _buffer.Clear();
            int budget = Mathf.Max(1, WonderlandConfig.ContainerRowsBatchSize?.Value ?? 25);
            for (int i = 0; i < budget; i++)
            {
                _scanner.Advance(_buffer);
            }

            foreach (ZDO zdo in _buffer)
            {
                Visit(zdo);
            }
        }

        private static void Visit(ZDO zdo)
        {
            if (!zdo.IsValid() || ZdoInventoryIO.IsBusy(zdo))
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Container template = prefab != null ? prefab.GetComponent<Container>() : null;
            if (template == null || !ContainerRows.IsEligible(prefab, template))
            {
                return;
            }

            (int width, int height) = ContainerRows.GetGridSize(prefab, template);
            (_, int vanillaHeight) = GridGrowth.GetVanillaSize(prefab.name, template);
            if (height <= vanillaHeight)
            {
                return;
            }

            Inventory? inventory = ZdoInventoryIO.Load(zdo, width, height);
            if (inventory == null || inventory.NrOfItems() == 0)
            {
                return;
            }

            if (!ContainerRows.EnsureAnchor(inventory, vanillaHeight, height, out ItemDrop.ItemData? moved) || moved == null)
            {
                return;
            }

            ZdoInventoryIO.Save(zdo, inventory);
            _anchoredTotal++;
            WonderlandDebug.LogInfo($"[ContainerRows] '{prefab.name}' at {zdo.GetPosition():F0}: parked {moved.m_shared.m_name} x{moved.m_stack} in row {height - 1} ({vanillaHeight} -> {height} rows). Anchors this session: {_anchoredTotal}");
        }
    }
}
