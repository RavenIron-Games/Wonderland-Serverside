# Wonderland — Valheim 1.0.7 migration instructions

> **Applied 2026-09-09 - Wonderland 0.2.0.** Everything in section 2 is done, in the shape recommended:
> `GameShape.Detect()` probes the `FindSectorObjects` *parameter shape* (release = 4 params with a
> `SimulationDistance`; legacy = 5 params with two `int`s, bridged by reflection; type names compared as
> strings so `Detect()` itself never JIT-binds a possibly-missing type), and `FindNear_Native` passes
> `new SimulationDistance(area, 0, classic: true)`. Rebuilt against `1.0/server` and BepInEx 5.4.2350 and
> loaded on the real 1.0.7 Linux dedicated server with no errors; the log reads
> `[GameShape] detected the Valheim 1.0.7+ sector API (Vector2s sectors, SimulationDistance)`.
>
> The same pass also found four defects this signature audit could not see, all fixed in 0.2.0 (details in
> `HexiumDist/CHANGELOG.md`): `SpawnGovernor`'s hook ran before the received ZDO was deserialized;
> `Player.GetAllPlayers()` is always empty on a dedicated server (six features moved to character ZDOs via
> `Core/Data/ConnectedCharacters.cs`); the starter grant's once-only flag lived on a per-session ZDO (now a
> world global key); and max HP is not enforceable server-side (Vitality subsystem removed, vitals guard
> detect-only). The player-cap transpiler was re-read against the 1.0.7 `RPC_PeerInfo` body: the
> `GetNrOfPlayers() >= 10` check is unchanged and is the method's only `GetNrOfPlayers` call.

**Status: ONE BREAK, in `Core/Compat/GameShape.cs` + `Core/Data/ZdoSpatialQuery.cs`.**
Everything else is 1.0-ready. The csproj already references `..\libs-Tools\1.0\server\`, which is
correct for a server-side-only mod.
Audited 2026-09-09 against `libs-Tools/1.0/DECOMPILED/assembly_valheim_SERVER.decompiled.cs`
(dedicated server 1.0.7, app 896660, build 25185644, network 39).

All 15 Harmony targets resolve on the 1.0.7 **server** assembly. The data layer, the ZDO I/O and the
whole subsystem set are unaffected. The problem is that `GameShape` models the world as a two-way
choice that is now a **three-way** choice, and it guesses wrong on the release build.

---

## 1. There are THREE sector-API shapes, not two

`GameShape`'s doc comment and `Build` enum assume "0.221.12 `Vector2i`" vs "0.221.13+/1.0 `Vector2s`",
and treat those as the same thing after 0.221.13. **They are not.** Measured directly from the three
decompiles in `libs-Tools/`:

| Build | `ZoneSystem.GetZone` | `ZDOMan.FindSectorObjects` | `m_activeArea` |
|---|---|---|---|
| 0.221.12 — `OLD-DECOMPILED ASSEMBLY VALHEIM-build21981559` | `-> Vector2i` | `(Vector2i, int area, int distantArea, List, List = null)` | present |
| 0.221.13 playtest — `OLD-DECOMPILED-PLAYTEST-build23105022` | `-> Vector2s` | `(Vector2s, int area, int distantArea, List, List = null)` | present |
| **1.0.7 release — `1.0/DECOMPILED`** | `-> Vector2s` | **`(Vector2s, SimulationDistance, List, List = null)`** | **removed** |

> `libs-Tools/VALHEIM-1.0-MIGRATION-FACTS.md` documents only the first transition and calls it
> bridgeable by "pass-through overloads". It was written against the playtest and **never updated for
> the release-stage change**. Do not trust that row; the decompile is ground truth.

### Why this specifically defeats `GameShape` as written

`Detect()` branches on `getZone.ReturnType == typeof(Vector2s)`. On 1.0.7 that is **true**, so it
selects `V0_221_13Plus_Vector2sSectors` and takes "the native fast path" — which is
`ZdoSpatialQuery.FindNear_Native`:

```csharp
Vector2s sector = ZoneSystem.GetZone(worldPos);
ZDOMan.instance.FindSectorObjects(sector, area, 0, raw);   // (Vector2s, int, int, List) - gone in 1.0.7
```

Two consequences, and the second is the dangerous one:

1. **Against the 1.0 refs this will not compile.** `area` is an `int` and cannot convert to
   `SimulationDistance`; `0` cannot convert to `List<ZDO>`.
2. **If you kept the playtest refs and ran on 1.0.7**, it compiles and then throws
   `MissingMethodException` at JIT of `FindNear_Native` — which is exactly the failure mode the file's
   own doc comment is designed to prevent. The reflection fallback would not save you either: it
   filters on `GetParameters().Length == 5`, and the 1.0.7 method has **4** parameters, so
   `_oldFindSectorObjects` is `null`, `FindSectorObjectsOld` returns early, and **every radius query in
   the mod silently returns an empty list**. ItemFlow, Storage and the upkeep sweep would all quietly
   do nothing, with only a single startup warning to show for it.

The architecture here is right — separate methods, no cross-shape static typing, probe before you
call. It just needs the third shape added.

---

## 2. The fix

### 2.1 `Core/Compat/GameShape.cs`

Add a third `Build` member and detect on the **parameter shape**, not just the sector type:

```csharp
public enum Build
{
    Unknown,
    V0_221_12_Vector2iSectors,          // (Vector2i, int, int, List, List)
    V0_221_13_Vector2sSectors_IntArea,  // (Vector2s, int, int, List, List)  -- playtest only
    V1_0_Vector2sSectors_SimDistance    // (Vector2s, SimulationDistance, List, List)
}
```

```csharp
public static void Detect()
{
    if (Detected != Build.Unknown) return;

    MethodInfo getZone = typeof(ZoneSystem).GetMethod(
        "GetZone", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);

    // Branch on the FindSectorObjects parameter shape, not just GetZone's return type: the playtest
    // and the 1.0 release BOTH return Vector2s and differ only in this method's second parameter.
    MethodInfo fso = typeof(ZDOMan)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(m => m.Name == "FindSectorObjects");

    ParameterInfo[] ps = fso?.GetParameters();

    if (ps != null && ps.Length == 4 && ps[1].ParameterType.Name == "SimulationDistance")
    {
        Detected = Build.V1_0_Vector2sSectors_SimDistance;
        WonderlandDebug.LogAlways("[GameShape] detected Valheim 1.0 (Vector2s sectors, SimulationDistance) - using the native fast path.");
        return;
    }

    if (ps != null && ps.Length == 5 && getZone != null && getZone.ReturnType.Name == "Vector2s")
    {
        Detected = Build.V0_221_13_Vector2sSectors_IntArea;
        InitPlaytestPath(getZone, fso);
        WonderlandDebug.LogAlways("[GameShape] detected 0.221.13 playtest (Vector2s sectors, int area) - bridging via reflection.");
        return;
    }

    Detected = Build.V0_221_12_Vector2iSectors;
    InitOldPath(getZone);
    WonderlandDebug.LogAlways("[GameShape] detected 0.221.12 (Vector2i sectors) - bridging via reflection.");
}
```

Keep `FindSectorObjectsOld` for the 0.221.12 path exactly as it is — it already invokes through
`MethodInfo` with `object[]`, so it is shape-agnostic. Add the sibling for the playtest path (same
body, sector type differs), or drop the playtest branch entirely if you no longer intend to run
against it — a deliberate two-shape file (0.221.12 + 1.0) is cleaner than a speculative three.

Update the class doc comment: it currently asserts the two-build dichotomy and cites
`VALHEIM-1.0-MIGRATION-FACTS.md` as ground truth for it. Point it at `libs-Tools/1.0/DECOMPILED/`
instead and record that the facts doc is stale on this exact point.

### 2.2 `Core/Data/ZdoSpatialQuery.cs`

`FindNear_Native` becomes the 1.0 shape:

```csharp
// 1.0 collapsed (int area, int distantArea) into a SimulationDistance. `classic: true` reproduces
// the pre-1.0 square-ring sweep; without it FindSectorObjects filters each ring through
// ZoneSystem.ZonesWithinRadius and the swept set becomes a disc. FindNear post-filters to the real
// radius anyway, but a disc sweep would drop the sector corners this method relies on seeing.
private static void FindNear_Native(Vector3 worldPos, int area, List<ZDO> raw)
{
    Vector2s sector = ZoneSystem.GetZone(worldPos);
    ZDOMan.instance.FindSectorObjects(sector, new SimulationDistance(area, 0, classic: true), raw);
}
```

And the dispatch at line ~44:

```csharp
if (GameShape.Detected == GameShape.Build.V1_0_Vector2sSectors_SimDistance)
{
    FindNear_Native(worldPos, area, raw);
}
else
{
    GameShape.FindSectorObjectsOld(worldPos, area, raw);
}
```

**`classic: true` is not optional here.** With `IsClassic == false`, vanilla narrows each ring through
`ZonesWithinRadius`, so `FindNear` would stop seeing objects in the far corners of the containing
sector — precisely the "cross-boundary objects at small radii" case the method's own doc comment says
it exists to handle. `FindNear` already post-filters by true distance, so over-fetching is harmless and
under-fetching is a silent correctness bug.

Also: the doc comment on `FindNear` says *"area is rounded up and area 0 still scans the containing
sector"*. On 1.0.7, `FindSectorObjects` loops `for (int i = 1; i <= NearSimulationDistance; i++)`
after seeding the centre sector, so `area = 0` still yields the containing sector. The existing
`Mathf.Max(1, ...)` keeps you above that anyway. Statement still true — leave it.

---

## 3. Rebuild and verify

```bash
dotnet build Wonderland.csproj -c Release

# should print nothing
grep -rn "FindSectorObjects(sector, area" --include=*.cs .
```

Runtime, on a 1.0.7 dedicated server — the silent-empty-result failure mode above means a clean log is
**not** sufficient evidence. Prove a radius query actually returns rows:

- place two containers within a few metres of each other, put items in one, and confirm ItemFlow
  moves them (that path is `FindNear` end to end);
- check the startup log says `detected Valheim 1.0`, not `detected 0.221.12`.

---

## 4. Verified safe on the 1.0.7 SERVER assembly

**All 15 Harmony targets resolve**:
- `Character.RPC_Damage(long, HitData)` — unchanged, and the patch already pins
  `typeof(long), typeof(HitData)`, so it is immune to overload drift. Leave it.
- `ZDOMan.CreateNewZDO(ZDOID, Vector3, int)` — 1.0.7 declares
  `private ZDO CreateNewZDO(ZDOID uid, Vector3 position, int prefabHashIn = 0)`. The patch's explicit
  `typeof(ZDOID), typeof(Vector3), typeof(int)` matches it exactly and correctly disambiguates from the
  public 2-arg `CreateNewZDO(Vector3, int)`. Leave it. (You flagged this as least-verified — it is fine.)
- `RandEventSystem.SetRandomEvent(RandomEvent, Vector3)` — unchanged, still private.
- `ZNet.Awake` / `Shutdown` / `Disconnect(ZNetPeer)` / `OnNewConnection(ZNetPeer)` /
  `RPC_PeerInfo(ZRpc, ZPackage)` — all unchanged. **Your player-cap transpiler on `RPC_PeerInfo` still
  has a valid target**, but a transpiler matches IL, not a signature — re-read the 1.0.7 body before
  trusting it, since 446 methods changed body-only in this release.
- `ZNetScene.Awake`, `ZRpc.HandlePackage(ZPackage)`, `FejdStartup.ShowConnectError`,
  `ConfigEntryBase.Get/SetSerializedValue` — unchanged.

**Data layer**:
- `ZDOMan.GetAllZDOsWithPrefabIterative(string, List<ZDO>, ref int)` — unchanged. `PrefabSetScanner` is fine.
- `ZoneSystem.c_ZoneSize` (`public const float = 64f`) — unchanged.
- `ZDO.GetByteArray` / `ZDO.Set` / `ZDOVars.s_items` — unchanged.
- `EnvMan.IsNight()` — unchanged.
- **`Inventory.Load`**: 1.0 added a second overload, `Load(ZPackage, bool)`. This matters only for
  Harmony patches that omit argument types. `ZdoInventoryIO.cs:42` makes a **direct C# call** with one
  argument, which binds unambiguously to `Load(ZPackage)`. **No change needed** — but do not add a
  `[HarmonyPatch(typeof(Inventory), "Load")]` anywhere without pinning `new[]{ typeof(ZPackage) }`.

**Not touched by this mod at all** (so the corresponding 1.0 breaks are irrelevant here):
`VisEquipment` hashes, `ItemStand.m_visualHash`, `ZDOExtraData` per-type readers,
`World.GetWorldSavePath` / `FileHelpers.FileSource` flag renumbering, `Version.c_networkVersion`,
`ZNet.PlayerInfo.m_host`, `Minimap.m_explored`, `ZDOMan.m_objectsByOutsideSector`.

**`StringExtensionMethods.GetStableHashCode(string)`** is unchanged on the release build — the 2-arg
playtest signature was reverted before ship. `PrefabSetScanner`'s "one redundant `GetStableHashCode()`
per call" reasoning still holds, and Wonderland needs no `Valheim10Compatibility` preloader.

---

## 5. Stale note to clear up

`VALHEIM-1.0-MIGRATION-FACTS.md` lists Wonderland as having three dead Harmony targets —
`Plant.HaveSpace`, `Character.OnFallLand`, `CraftingStation.Awake`. **None of those exist in the
current tree** (the 2026-09-08 server-side-only rebuild removed them). Verified against every
`HarmonyPatch` attribute in the repo. That row is stale; no action needed.
