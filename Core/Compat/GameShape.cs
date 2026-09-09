using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Wonderland.Core.Compat
{
    /// <summary>
    /// Detects which shape of the sector API the running game exposes and bridges the one call
    /// Wonderland makes through it - ZDOMan.FindSectorObjects, keyed by ZoneSystem.GetZone(Vector3) -
    /// which Core/Data/ZdoSpatialQuery.FindNear uses for every radius query in the mod.
    ///
    /// Two shapes exist (ground truth: libs-Tools/1.0/DECOMPILED for the release, the OLD-DECOMPILED-*
    /// folders for the older builds; note VALHEIM-1.0-MIGRATION-FACTS.md's sector row describes the
    /// playtest, not what shipped):
    ///
    ///  - Release 1.0.7+ (server build 25185644, network version 39):
    ///    FindSectorObjects(Vector2s sector, SimulationDistance distance, List&lt;ZDO&gt; near, List&lt;ZDO&gt; distant = null).
    ///    The old (int area, int distantArea) pair collapsed into the SimulationDistance struct, and
    ///    unless IsClassic is set the sweep is a disc filtered through ZoneSystem.ZonesWithinRadius
    ///    rather than the square ring older builds swept. Wonderland compiles against this build, so
    ///    this is the native, statically-typed path.
    ///  - Legacy five-arg (0.221.12 with Vector2i sectors; the 0.221.13 playtest with Vector2s sectors):
    ///    FindSectorObjects(sector, int area, int distantArea, List&lt;ZDO&gt;, List&lt;ZDO&gt;). Reached purely by
    ///    reflection, passing the boxed sector object GetZone hands back straight through, so neither
    ///    sector type is ever named at compile time.
    ///
    /// The mono rule that dictates this layout (VALHEIM-1.0-MIGRATION-FACTS.md; TheEye's own
    /// Core/Compat/GameShape.cs is the workspace's worked example): a compile-time binding to a member
    /// the loaded assembly lacks throws at JIT of the CALLING method, taking the whole method down even
    /// with a try/catch inside it. So the native path lives in its own method that is only ever called -
    /// and therefore only ever JIT-compiled - once Detect() has confirmed the release shape, and Detect()
    /// itself compares type NAMES rather than using typeof() on anything that might be missing: a
    /// typeof(SimulationDistance) or typeof(Vector2s) inside Detect() would be exactly the binding the
    /// rule forbids on an older build.
    /// </summary>
    public static class GameShape
    {
        public enum Build
        {
            Unknown,
            /// <summary>Valheim 1.0.7+: FindSectorObjects takes a SimulationDistance struct.</summary>
            Release10_SimulationDistance,
            /// <summary>0.221.12 / 0.221.13 playtest: FindSectorObjects takes (int area, int distantArea).</summary>
            Legacy_FiveArgSectors
        }

        public static Build Detected { get; private set; } = Build.Unknown;

        private static bool _probed;
        private static MethodInfo _legacyGetZone;
        private static MethodInfo _legacyFindSectorObjects;

        /// <summary>
        /// Probes the loaded assembly by reflection only (inspecting MethodInfo/ParameterInfo, never
        /// invoking or statically referencing a possibly-missing type) - safe to call unconditionally on
        /// any build. Call once at startup before anything in ZdoSpatialQuery runs.
        /// </summary>
        public static void Detect()
        {
            if (_probed)
            {
                return;
            }
            _probed = true;

            MethodInfo getZone = typeof(ZoneSystem).GetMethod("GetZone", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
            if (getZone == null)
            {
                WonderlandDebug.LogWarning("[GameShape] ZoneSystem.GetZone(Vector3) not found at all - every radius query in this mod will return nothing on this build until it is re-verified against the decompile.");
                return;
            }
            Type sectorType = getZone.ReturnType;

            MethodInfo[] candidates = typeof(ZDOMan)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "FindSectorObjects")
                .ToArray();

            if (candidates.Any(m => IsReleaseShape(m, sectorType)))
            {
                Detected = Build.Release10_SimulationDistance;
                WonderlandDebug.LogAlways($"[GameShape] detected the Valheim 1.0.7+ sector API ({sectorType.Name} sectors, SimulationDistance) - using the native path.");
                return;
            }

            _legacyFindSectorObjects = candidates.FirstOrDefault(m => IsLegacyShape(m, sectorType));
            if (_legacyFindSectorObjects != null)
            {
                _legacyGetZone = getZone;
                Detected = Build.Legacy_FiveArgSectors;
                WonderlandDebug.LogAlways($"[GameShape] detected a pre-1.0 sector API ({sectorType.Name} sectors, five-arg FindSectorObjects) - bridging via reflection. This mod is built and tested against 1.0.7; treat this path as best-effort.");
                return;
            }

            WonderlandDebug.LogWarning($"[GameShape] ZDOMan.FindSectorObjects has an unrecognised shape on this build ({candidates.Length} overload(s), sectors are {sectorType.Name}) - every radius query in this mod will return nothing until Core/Compat/GameShape.cs is re-verified against this build's decompile.");
        }

        private static bool IsReleaseShape(MethodInfo m, Type sectorType)
        {
            ParameterInfo[] p = m.GetParameters();
            return p.Length == 4
                && p[0].ParameterType == sectorType
                && p[1].ParameterType.Name == "SimulationDistance"
                && p[2].ParameterType == typeof(List<ZDO>);
        }

        private static bool IsLegacyShape(MethodInfo m, Type sectorType)
        {
            ParameterInfo[] p = m.GetParameters();
            return p.Length == 5
                && p[0].ParameterType == sectorType
                && p[1].ParameterType == typeof(int)
                && p[2].ParameterType == typeof(int)
                && p[3].ParameterType == typeof(List<ZDO>);
        }

        /// <summary>
        /// The legacy bridge for ZdoSpatialQuery.FindNear's inner call: GetZone's boxed result is handed
        /// straight to the five-arg FindSectorObjects with distantArea 0, which is the old square-ring
        /// sweep of <paramref name="area"/> sectors around the anchor. Never invoked on the release build.
        /// </summary>
        public static void FindSectorObjectsLegacy(Vector3 worldPos, int area, List<ZDO> results)
        {
            if (_legacyGetZone == null || _legacyFindSectorObjects == null || ZDOMan.instance == null)
            {
                return;
            }

            try
            {
                object sector = _legacyGetZone.Invoke(null, new object[] { worldPos });
                _legacyFindSectorObjects.Invoke(ZDOMan.instance, new object[] { sector, area, 0, results, null });
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[GameShape] legacy sector bridge failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
