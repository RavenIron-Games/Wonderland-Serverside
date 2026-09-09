# Changelog

## 0.2.0

Rebuilt for **Valheim 1.0** (1.0.7, dedicated-server build 25185644, network version 39) and, for the
first time, loaded on a real 1.0.7 Linux dedicated server under BepInEx 5.4.2350 with no errors. The same
pass audited every game API the mod touches against the 1.0.7 server decompile and found four things that
were wrong before 1.0 ever entered the picture; all four are fixed below rather than carried forward.

### Fixed
- **Sector API on 1.0.** The release collapsed `FindSectorObjects`' two `int` distance arguments into a
  `SimulationDistance` struct and removed `ZoneSystem.m_activeArea`. 0.1.0 (compiled against the 0.221.13
  playtest) would not have found a single nearby object on 1.0: the native call no longer existed and the
  reflection fallback probed for the old five-argument shape, so every radius query — vacuum, production
  supply, auto-harvest, the overflow guard's sibling lookup — would have silently come back empty. The build
  is now detected by *parameter shape* rather than by sector type, and the 1.0 path sweeps in classic
  (square-ring) mode so sector corners are never dropped.
- **Night-spawn blocking never fired.** Its hook on `ZDOMan.CreateNewZDO` ran before the received ZDO had
  been deserialized, so the prefab was still unknown — and `DestroyZDO` is a no-op unless the server owns the
  ZDO, which it no longer does by then. It now evaluates on the first `ZDO.Deserialize` of a network-created
  ZDO and claims ownership before destroying, exactly the way vanilla's own dead-ZDO path does.
- **Six features looked for players in a list that is always empty on a dedicated server.**
  `Player.GetAllPlayers()` is the *local instance* list; a dedicated server pins its own reference position
  a thousand kilometres away and never instantiates a `Player`. Auto-harvest triggers, the starter grant, the
  position watch and the vitals guard now read each connected peer's character ZDO instead — the only
  server-side view of a player there is.
- **Starter grant would have repeated on every login.** Its once-only flag lived on the character ZDO,
  which is per-session (a new one on every connect). The record is now a world global key per stable
  character id (`wonderland_starter_<playerID>`), saved inside the world file and its backups.
- Starter-kit and auto-harvest bonus drops set the item's drop prefab explicitly before spawning; a prefab
  template's item data does not carry it, and the vanilla spawn call instantiates from exactly that field.

### Removed
- **Max-HP floor (the `11 - Vitality` section) and the max-HP clamp.** Verified impossible from the server:
  the owning client rewrites max HP from its food every second, and while the player is moving its own
  revision counter outruns the server's, so the owner discards the server's write as stale. Nothing on the
  server even consumes the value — damage resolves on the victim's client. A max HP above the configured
  ceiling is still *detected* and written to the security log, once per distinct value per player.
- The player-inventory half of the item-integrity sweep. A player's bag is never networked to the server,
  so there was never anything there to check; containers are still swept.
- **Damage plausibility** (`DamagePlausibilityEnabled` / `DamagePlausibilityCeiling`). Its hook on
  `Character.RPC_Damage` can never run on a dedicated server: a damage RPC is routed to the victim's owning
  client and the server only relays it, and the server has no live `Character` instance to receive it in
  any case. It was off by default; removed rather than shipped as a dead switch.

### Changed
- **Storage capacity now defaults to off** (`StackSizeEnabled`, `GridGrowthEnabled`). With vanilla clients the
  extra capacity is never visible — the client clamps stacks and grid on load — and the overflow guard rewinds
  it every sweep by design, so "on" only added churn. The guard stays active either way. The feature is still
  there for anyone experimenting; the README now says plainly what it can and cannot do.
- Compiled against the 1.0.7 dedicated-server assemblies and BepInEx 5.4.2350. The 0.221.12 / 0.221.13
  five-argument sector API is still bridged by reflection, best-effort and untested.
- Config: `11 - Vitality` is gone; `VitalityCheckInterval` migrates automatically to
  `12 - Security` / `VitalsGuardInterval`. Descriptions for the vitals guard, integrity sweep and starter
  grant now say what those features actually do.

## 0.1.0

Complete rebuild as a strictly server-side-only mod — no client install ever required, so PlayFab,
Xbox, and crossplay players get full functionality. This is a clean-slate repurpose of the mod name;
none of the old client/server-mixed code survived.

**Why:** the previous version's automation (vacuum, auto-fuel, auto-repair) all keyed off
`Player.m_localPlayer`, which is always null on a true dedicated server — none of it actually ran
there. This version operates purely on ZDO data instead of live game objects, which is what makes it
work on a real headless server at all.

### Added
- Smart drop-to-chest (match-required) and auto-harvest (triggered by an actual player harvest, not a passive sweep)
- Production auto-supply for fuel and ore/process material, with a reserve floor
- Background stack consolidation
- Stack-size multiplier and real grid-slot growth for containers/boats, with an overflow guard against item loss on load
- Raid and nighttime-ambient-spawn blocking, per biome and per creature/event list
- Configurable player cap (raises or lowers vanilla's hardcoded 10)
- Building no-decay / auto-repair
- One-time starter kit + labeled boat on first character creation
- Max-HP ceiling/floor enforcement, an item-fabrication integrity sweep, and detect-only checks for implausible speed, flight/noclip, and outsized damage
- Auto-detection between Valheim 0.221.12 and 0.221.13+ builds, bridging the one API shape that differs between them (ZDO sector coordinates)
- Config migration from the pre-0.1.0 key names, where the underlying setting still exists

### Removed
- Everything client-simulated and therefore unenforceable server-side: combat/movement/stamina tuning, the HUD overlay, portal PIN-locking/single-portal dialing, craft-from-containers (the live crafting check only ever reads the player's own inventory, with no server-side path around it)
- Farming automation (plant-anything, mass planting, crop growth speed) — out of scope for this rebuild
- Max stamina / max carry weight adjustment — confirmed impossible server-side on any Valheim build (neither value is ever written to a ZDO); not carried forward as a non-functional placeholder

### Known limitations
- Max stamina/carry weight: see above, not a bug, not planned
- Crossplay servers have PlayFab's own separate, non-configurable 10-player lobby cap
