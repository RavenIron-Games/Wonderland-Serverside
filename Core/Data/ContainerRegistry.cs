using System.Collections.Generic;
using UnityEngine;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// The set of container-bearing prefab names, discovered once from ZNetScene at startup and
    /// shared by every subsystem that needs "is this ZDO a container" or "find nearby containers"
    /// (VacuumEngine, ProductionSupplyEngine, SortEngine, the Security sweep) - one discovery pass
    /// instead of three, and one place to look if a container type is ever missing from a sweep.
    /// </summary>
    public static class ContainerRegistry
    {
        private static List<string> _prefabNames;
        private static HashSet<int> _prefabHashes;

        public static List<string> PrefabNames
        {
            get
            {
                if (_prefabNames == null)
                {
                    Discover();
                }
                return _prefabNames;
            }
        }

        /// <summary>True when a ZDO's prefab hash is a container type - a set lookup, for the callers that
        /// walk every ZDO in a sector ring (ResolveTemplate on each would be two native GetComponent calls
        /// per wall, floor and rock in range).</summary>
        public static bool IsContainerPrefab(int prefabHash)
        {
            if (_prefabHashes == null)
            {
                Discover();
            }
            return _prefabHashes != null && _prefabHashes.Contains(prefabHash);
        }

        public static void Discover()
        {
            _prefabNames = new List<string>();
            _prefabHashes = new HashSet<int>();
            if (ZNetScene.instance == null)
            {
                return;
            }
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab != null && ResolveTemplate(prefab) != null)
                {
                    _prefabNames.Add(prefab.name);
                    _prefabHashes.Add(prefab.name.GetStableHashCode());
                }
            }
            WonderlandDebug.LogInfo($"[ContainerRegistry] discovered {_prefabNames.Count} container prefab types.");
        }

        public static bool IsContainer(GameObject prefab) => ResolveTemplate(prefab) != null;

        /// <summary>
        /// The Container template for a prefab - on the prefab's own GameObject for an ordinary chest,
        /// or on a child for anything built the way vanilla builds ship cargo (Karve/Raft/VikingShip):
        /// a child object carrying its own Container with m_rootObjectOverride pointed at the parent's
        /// ZNetView, so m_width/m_height live on the child but the item blob still saves under the
        /// ship's own ZDO (ZDOVars.s_items) - confirmed against the WubarrksEye prefab dump (Karve's
        /// root GameObject has no Container component at all) and libs-Tools'
        /// VANILLA-PIECE-INTEROP-FACTS.md, which documents m_rootObjectOverride as exactly this pattern.
        /// Every Wonderland engine should resolve a container template through this method rather than
        /// a bare GetComponent&lt;Container&gt;(), or it silently treats every ship as cargo-less.
        /// </summary>
        public static Container ResolveTemplate(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }
            Container template = prefab.GetComponent<Container>();
            return template != null ? template : prefab.GetComponentInChildren<Container>(true);
        }
    }
}
