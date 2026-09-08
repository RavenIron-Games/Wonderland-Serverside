using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Wonderland.Core.Compat
{
    /// <summary>
    /// Detects which of the two Valheim PTB builds this mod might run against - 0.221.12 (build
    /// 21981590, network version 36, ZDO sectors are Vector2i) or 0.221.13+/1.0 (build 23105022+,
    /// network version 37, ZDO sectors are Vector2s) - and bridges the one API shape difference
    /// Wonderland's own code actually touches between them: ZoneSystem.GetZone(Vector3) and
    /// ZDOMan.FindSectorObjects(sector, ...), used by Core/Data/ZdoSpatialQuery.cs for every radius
    /// query in the mod.
    ///
    /// Ground truth: libs-Tools/VALHEIM-1.0-MIGRATION-FACTS.md, reconciled against both decompiles.
    /// Everything else Wonderland calls (Container, Inventory, ItemDrop, Smelter, Fireplace, Pickable,
    /// WearNTear, Player, ZDO.Get/Set, ZNet.RPC_PeerInfo, RandEventSystem.SetRandomEvent, EnvMan.IsNight,
    /// WorldGenerator.GetHeight/GetBiome, ZDOMan.CreateNewZDO/GetAllZDOsWithPrefabIterative,
    /// Character.RPC_Damage) is confirmed unchanged between these two builds per that doc - no bridge
    /// needed there, and adding one anyway would be guessing at a problem that doesn't exist.
    ///
    /// Wonderland compiles against the 0.221.13 assembly (see the csproj), so the 0.221.13+ path below
    /// is ordinary, statically-typed, zero-overhead code. The 0.221.12 path is pure reflection with no
    /// compile-time reference to Vector2i's sibling type or the old FindSectorObjects overload - a
    /// compile-time binding to a member that doesn't exist on the loaded assembly throws at JIT of the
    /// CALLING method, taking the whole method down even with a try/catch inside it, so the two paths
    /// live in completely separate methods and only the one the detected build actually needs ever
    /// gets JIT-compiled at all. This is the same rule libs-Tools/VALHEIM-1.0-MIGRATION-FACTS.md states
    /// explicitly ("late-bind anything that moved, or gate the call behind a separate method"), and the
    /// same shape as TheEye's own Core/Compat/GameShape.cs, the workspace's worked example for this
    /// exact problem - this file follows that name and pattern deliberately.
    /// </summary>
    public static class GameShape
    {
        public enum Build
        {
            Unknown,
            V0_221_12_Vector2iSectors,
            V0_221_13Plus_Vector2sSectors
        }

        public static Build Detected { get; private set; } = Build.Unknown;

        private static MethodInfo _oldGetZone;
        private static MethodInfo _oldFindSectorObjects;
        private static FieldInfo _oldSectorX;
        private static FieldInfo _oldSectorY;

        /// <summary>
        /// Probes the loaded assembly by reflection only (inspecting MethodInfo, never invoking or
        /// statically referencing the old type) - safe to call unconditionally on either build. Call
        /// once at startup before anything in ZdoSpatialQuery runs.
        /// </summary>
        public static void Detect()
        {
            if (Detected != Build.Unknown)
            {
                return;
            }

            MethodInfo getZone = typeof(ZoneSystem).GetMethod("GetZone", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
            bool is13Plus = getZone != null && getZone.ReturnType == typeof(Vector2s);

            if (is13Plus)
            {
                Detected = Build.V0_221_13Plus_Vector2sSectors;
                WonderlandDebug.LogAlways("[GameShape] detected game build 0.221.13+ (Vector2s ZDO sectors) - using the native fast path.");
                return;
            }

            Detected = Build.V0_221_12_Vector2iSectors;
            InitOldPath(getZone);
            WonderlandDebug.LogAlways("[GameShape] detected game build 0.221.12 (Vector2i ZDO sectors) - bridging via reflection. If this looks wrong, libs-Tools/VALHEIM-1.0-MIGRATION-FACTS.md needs a fresh diff against whatever build is actually running.");
        }

        private static void InitOldPath(MethodInfo getZone)
        {
            _oldGetZone = getZone;
            if (_oldGetZone == null)
            {
                WonderlandDebug.LogWarning("[GameShape] ZoneSystem.GetZone(Vector3) not found at all - radius queries will return nothing on this build until re-verified.");
                return;
            }

            Type sectorType = _oldGetZone.ReturnType;
            _oldSectorX = sectorType.GetField("x", BindingFlags.Public | BindingFlags.Instance);
            _oldSectorY = sectorType.GetField("y", BindingFlags.Public | BindingFlags.Instance);

            _oldFindSectorObjects = typeof(ZDOMan).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "FindSectorObjects" && m.GetParameters().Length == 5 && m.GetParameters()[0].ParameterType == sectorType);

            if (_oldFindSectorObjects == null)
            {
                WonderlandDebug.LogWarning("[GameShape] ZDOMan.FindSectorObjects(sector, area, distantArea, list, list) not found with the expected shape - radius queries will return nothing on this build until re-verified.");
            }
        }

        /// <summary>
        /// The 0.221.12 bridge for ZdoSpatialQuery.FindNear's inner call. Never invoked when Detected
        /// is V0_221_13Plus, so its use of the reflected members above (resolved once, from a build
        /// where they're known to exist) never runs against a null MethodInfo in practice - callers
        /// still guard on null defensively since Detect() logs but does not throw when a probe fails.
        /// </summary>
        public static void FindSectorObjectsOld(Vector3 worldPos, int area, List<ZDO> results)
        {
            if (_oldGetZone == null || _oldFindSectorObjects == null || _oldSectorX == null || _oldSectorY == null)
            {
                return;
            }

            try
            {
                object sector = _oldGetZone.Invoke(null, new object[] { worldPos });
                _oldFindSectorObjects.Invoke(ZDOMan.instance, new object[] { sector, area, 0, results, null });
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[GameShape] 0.221.12 sector bridge failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
