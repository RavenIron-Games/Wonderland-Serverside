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

        public static void Discover()
        {
            _prefabNames = new List<string>();
            if (ZNetScene.instance == null)
            {
                return;
            }
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab != null && prefab.GetComponent<Container>() != null)
                {
                    _prefabNames.Add(prefab.name);
                }
            }
            WonderlandDebug.LogInfo($"[ContainerRegistry] discovered {_prefabNames.Count} container prefab types.");
        }

        public static bool IsContainer(GameObject prefab) => prefab != null && prefab.GetComponent<Container>() != null;
    }
}
