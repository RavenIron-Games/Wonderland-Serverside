using System.Collections.Generic;
using UnityEngine;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// Nearby-object lookups against ZDOMan's own sector data - never Physics.OverlapSphere, since no
    /// collider exists server-side for anything outside the single point Game.FixedUpdate pins the
    /// world-loading reference position to (see ZoneAnchor.md). Two distinct query shapes, because
    /// they have very different cost profiles:
    ///  - FindNear: anchored to one known position (a container, a smelter, a player). Cheap - a
    ///    handful of sectors - safe to call every tick for every tracked object.
    ///  - PrefabSetScanner: unanchored ("every container on the map", "every decay-capable structure").
    ///    Time-budgeted and resumable so a full-map pass never costs a frame spike.
    /// </summary>
    public static class ZdoSpatialQuery
    {
        /// <summary>
        /// Every ZDO within roughly radiusMeters of worldPos. FindSectorObjects only narrows to whole
        /// 64m sectors (area is rounded up and area 0 still scans the containing sector), so this
        /// always post-filters to the real distance - relying on sector granularity alone both misses
        /// cross-boundary objects at small radii and over-includes at the sector's far corners.
        /// </summary>
        public static List<ZDO> FindNear(Vector3 worldPos, float radiusMeters, List<ZDO> reuseBuffer = null)
        {
            List<ZDO> result = reuseBuffer ?? new List<ZDO>();
            result.Clear();
            if (ZDOMan.instance == null || ZoneSystem.instance == null)
            {
                return result;
            }

            Vector2s sector = ZoneSystem.GetZone(worldPos);
            int area = Mathf.Max(1, Mathf.CeilToInt(radiusMeters / ZoneSystem.c_ZoneSize));
            var raw = new List<ZDO>();
            ZDOMan.instance.FindSectorObjects(sector, area, 0, raw);

            float radiusSqr = radiusMeters * radiusMeters;
            foreach (ZDO zdo in raw)
            {
                if (!zdo.IsValid())
                {
                    continue;
                }
                if ((zdo.GetPosition() - worldPos).sqrMagnitude <= radiusSqr)
                {
                    result.Add(zdo);
                }
            }
            return result;
        }

        /// <summary>
        /// Round-robins ZDOMan.GetAllZDOsWithPrefabIterative across a fixed set of prefab names, one
        /// chunk (that method's own internal ~400-sector budget) per Advance() call. Never blocks,
        /// never "completes" - it cycles the whole set forever, which is exactly what a background
        /// integrity sweep or upkeep pass wants. Prefab names, not hashes: the vanilla method only
        /// takes a name and hashes it internally, and reaching into ZDOMan's private sector array to
        /// avoid that would mean re-deriving its traversal/budget logic ourselves - more fragile than
        /// paying one redundant GetStableHashCode() per call.
        /// </summary>
        public sealed class PrefabSetScanner
        {
            private readonly List<string> _prefabNames;
            private int _prefabIndex;
            private int _sectorCursor;

            public PrefabSetScanner(IEnumerable<string> prefabNames)
            {
                _prefabNames = new List<string>(prefabNames);
            }

            public int TrackedPrefabCount => _prefabNames.Count;

            /// <summary>Appends whatever this chunk found to <paramref name="results"/> (not cleared first).</summary>
            public void Advance(List<ZDO> results)
            {
                if (_prefabNames.Count == 0 || ZDOMan.instance == null)
                {
                    return;
                }

                string name = _prefabNames[_prefabIndex];
                bool done = ZDOMan.instance.GetAllZDOsWithPrefabIterative(name, results, ref _sectorCursor);
                if (done)
                {
                    _sectorCursor = 0;
                    _prefabIndex = (_prefabIndex + 1) % _prefabNames.Count;
                }
            }
        }
    }
}
