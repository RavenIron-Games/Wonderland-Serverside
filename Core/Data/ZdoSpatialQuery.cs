using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core.Compat;

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

            int area = Mathf.Max(1, Mathf.CeilToInt(radiusMeters / ZoneSystem.c_ZoneSize));
            var raw = new List<ZDO>();

            // Split into separate methods, not an if/else inline, deliberately: FindNear_Native's body is
            // statically typed against Vector2s and SimulationDistance (this mod's compile-time reference
            // build, Valheim 1.0.7). A method containing a call the loaded assembly doesn't have throws at
            // JIT of THAT METHOD, taking it down even with a try/catch around the call site - so on an
            // older server this whole method must never be JIT-compiled, which means never called, which
            // is what the dispatch below guarantees. See Core/Compat/GameShape.cs.
            switch (GameShape.Detected)
            {
                case GameShape.Build.Release10_SimulationDistance:
                    FindNear_Native(worldPos, area, raw);
                    break;
                case GameShape.Build.Legacy_FiveArgSectors:
                    GameShape.FindSectorObjectsLegacy(worldPos, area, raw);
                    break;
                default:
                    // Unrecognised sector API - GameShape.Detect already logged it once, loudly. An empty
                    // result here is the honest answer; guessing at a call shape is how a hot method dies.
                    return result;
            }

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

        private static void FindNear_Native(Vector3 worldPos, int area, List<ZDO> raw)
        {
            Vector2s sector = ZoneSystem.GetZone(worldPos);
            // classic: true reproduces the pre-1.0 square-ring sweep (every sector within `area` rings).
            // Without it, 1.0.7 filters each ring through ZoneSystem.ZonesWithinRadius into a disc that
            // drops the corner sectors, and the metre-radius post-filter below would then be applied to a
            // set that already missed objects sitting diagonally across a sector boundary. far = 0 is the
            // old distantArea 0 - Wonderland never wants distant-only ZDOs in a radius query.
            ZDOMan.instance.FindSectorObjects(sector, new SimulationDistance(area, 0, classic: true), raw);
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

        /// <summary>
        /// One resumable walk of ZDOMan's whole sector array that matches every ZDO against a SET of
        /// prefab hashes in a single pass. PrefabSetScanner above leans on vanilla's
        /// GetAllZDOsWithPrefabIterative, which only takes ONE prefab name and re-walks all
        /// m_width*m_width (512*512 = 262144) sector slots for each - fine for the dozen-odd container
        /// prefabs the item-flow engines track, ruinous for the ~800 WearNTear-bearing prefabs
        /// StructureUpkeep tracks (a single full cycle ran to hours). A HashSet lookup during one walk
        /// collapses that to one pass regardless of set size. This does read the publicised
        /// ZDOMan.m_objectsBySector directly; the loop is a straight copy of vanilla's own traversal
        /// with the per-sector budget counted the same way (non-empty sectors only), minus vanilla's
        /// quirk of re-scanning the sector it broke on.
        /// </summary>
        public sealed class PrefabSetSweeper
        {
            private readonly HashSet<int> _prefabHashes;
            private int _sectorCursor;

            public PrefabSetSweeper(IEnumerable<int> prefabHashes)
            {
                _prefabHashes = new HashSet<int>(prefabHashes);
            }

            public int TrackedPrefabCount => _prefabHashes.Count;

            /// <summary>
            /// Walks up to <paramref name="sectorBudget"/> NON-EMPTY sectors from where the last call
            /// stopped, appending matches to <paramref name="results"/> (not cleared first). Returns
            /// true when the cursor ran off the end of the array - a full map pass completed - and
            /// has been reset to 0 for the next call.
            /// </summary>
            public bool Advance(int sectorBudget, List<ZDO> results)
            {
                if (_prefabHashes.Count == 0 || ZDOMan.instance == null)
                {
                    return false;
                }
                List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
                if (sectors == null)
                {
                    return false;
                }

                int visited = 0;
                while (_sectorCursor < sectors.Length)
                {
                    List<ZDO> sector = sectors[_sectorCursor];
                    _sectorCursor++;
                    if (sector == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < sector.Count; i++)
                    {
                        ZDO zdo = sector[i];
                        if (zdo != null && zdo.IsValid() && _prefabHashes.Contains(zdo.GetPrefab()))
                        {
                            results.Add(zdo);
                        }
                    }
                    if (++visited >= sectorBudget)
                    {
                        return false;
                    }
                }
                _sectorCursor = 0;
                return true;
            }
        }
    }
}
