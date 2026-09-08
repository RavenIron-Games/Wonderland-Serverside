# Changelog

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
