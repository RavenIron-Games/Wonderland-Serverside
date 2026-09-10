# Changelog

## 0.4.0

### Added
- **Player Controls: operate Wonderland from a vanilla client with emotes.** Stand next to a kiln,
  smelter or fire and `/nonono` switches its auto-supply off (Wonderland stops loading it, whatever
  is inside burns out, hand-feeding still works, the switch survives restarts); `/thumbsup` switches
  it back on; `/comehere` drops your cached items at your feet. The server replies with an on-screen
  message. Emotes are the control because they are the only player signal a stock client always
  delivers: a custom slash command never leaves the client (`Chat.InputText` runs it as a local
  console command and prints "not a recognized command"), and plain chat is sent only to *other*
  players (`ZRoutedRpc.InvokeRoutedRPC` never routes a self-targeted message), so a player alone on
  the server produces no chat traffic at all. An emote is written into the character ZDO, which
  always reaches the server, from the emote wheel on every platform or typed in chat. New config
  section `13 - Player Controls`; every emote and the range are configurable.
- **`KilnWoodTypes`** in `3 - Production Supply`: which wood the auto-supply may load into a
  charcoal kiln (any smelter-family station whose only product is Coal). Default `Wood`.

### Changed
- **Kilns are fed plain wood only by default.** Previously the auto-supply loaded every wood a kiln
  accepts, so fine wood, core wood and blackwood in a linked chest were turned into coal. List the
  types you want in `KilnWoodTypes`, or leave it empty for the old behaviour. Hand-feeding is vanilla
  and unaffected.
- The switched-off station list is a mod-side file (`Wonderland.SupplyOff.<WorldName>.dat`) rather
  than a write to the station's ZDO: the owning client rewrites that ZDO every second while the
  station runs, and a server write there can lose the race. Nothing about this feature moves items.

### Removed
- The `/cache` and `/cache claim` chat hook. It patched `Chat.RPC_ChatMessage`, which never runs on
  a dedicated server, and the chat text it waited for is never sent by a solo player anyway. Cache
  recovery is the `/comehere` emote.

## 0.3.0

### Added
- **Container Rows: bigger chests on vanilla clients, server-side only.** Every player-built container
  has its rows multiplied by `ContainerRowMultiplier` - doubled by default, so a 5x2 wood chest
  becomes 5x4 and an 8x4 reinforced chest 8x8 - and players see it with nothing installed on their
  side. 0.2.6 concluded that a vanilla client "discards anything stored beyond its own grid"; that
  was wrong for rows, and only rows. The 1.0 client load path (`Inventory.AddItem` with
  `skipValidPositionCheck`, reached from `Container.Load`) refuses an item in a column past the grid
  but accepts one in a row past it, and `Container.UpdateRows()` then resizes the chest to its lowest
  occupied row; the chest panel is a scrolling grid. So the server keeps one stack parked in the last
  row - a pure position move that can never add, remove or resize a stack, so a write that loses a
  race against a client changes nothing - and vanilla does the rest. Materials are placed
  bottom-first by vanilla anyway, so the anchor mostly maintains itself. The vacuum, production supply
  and sort all work with the grown grid. New config section `5 - Container Rows`. Stack sizes stay
  vanilla: the same load path clamps every stack to the client's own maximum, and no RPC, ZDO field or
  global key a vanilla client honours can change that. Rows grow; width does not.

### Changed
- **Overflow guard now follows the real client rules**: a column past vanilla width, a row past the
  height Wonderland targets for that container, or a stack above vanilla max. Lowering
  `ContainerRowMultiplier` later pulls items out of the abandoned rows back into the chest or a sibling.
  Sibling containers are sized by their own prefab's rules rather than the source chest's.
- **The README no longer carries version information.** It is the how-to-use and feature reference;
  this changelog is the record of what changed when.

## 0.2.7

### Fixed
- **Starter boats drifting further out to sea with every player.** Each boat claimed its spot and the
  next player's search had to start beyond it, so the walk grew without bound - measured at 278m, 299m,
  321m and 343m for the first four players on one test world, roughly 21m added per player. The required
  clearance between hulls is now 6m rather than 12m, close to a Karve's own length, which packs them
  tightly enough that the distance stops running away. Boats still never overlap.

### Changed
- **Defaults now match what a real server actually wants**, rather than the conservative values the
  features were first written with:
  - The starter kit is `Wood:50,Stone:10,Flint:5,AxeFlint:1,KnifeFlint:1,SpearFlint:1,PickaxeAntler:1` -
    enough wood to build with and the flint tools needed to use it, instead of raw materials alone.
  - The boat water search ceiling is 200m rather than 300m. The search always begins at the shoreline
    nearest the player and works outward, so this is a limit, not a target.
  - **Verbose logging is on by default.** Every item transfer the mod makes is now recorded. This is a
    deliberate trade of log volume for diagnosability: a suspected duplication can only be diagnosed
    from a log that was already recording the transfer when it happened, and the alternative - turn it
    on and reproduce - means the interesting event has already been missed. Rejections and security
    findings log regardless, as before. Set it to `false` if log size matters more on your server.

## 0.2.6

### Removed
- **Stack-size multiplier and container grid growth are gone.** Both were verified on a live server and
  did nothing observable: a vanilla client - the only kind that ever connects to a server-only mod -
  clamps every stack to its own vanilla maximum and ignores grid slots beyond its own bounds the moment
  it loads a chest. The server was doing real work that no player could ever see. Rather than leave two
  settings that quietly amount to nothing, the features and their seven config keys have been removed
  outright. Any values left in an existing config file are simply ignored.

  The **overflow guard and Item Cache stay**, and are now the whole point of that area. A container grown
  by an earlier version still exists in saved worlds and is still dangerous - a vanilla client silently
  discards the excess when it opens one - so the guard continues to rehome anything over vanilla bounds
  into sibling chests or the cache before that can happen.

## 0.2.5

Found by watching a live 0.2.4 test server rather than by reading code.

### Fixed
- **New players flagged as cheating by the intro flight.** The Valkyrie carries a brand-new character
  hundreds of metres up before it ever touches down, and the fly/noclip check flagged that as suspicious -
  three warnings against one player before they had landed once, aimed at exactly the people least likely
  to be cheating. Nothing is flagged now until a character has been seen on the ground at least once in
  the session.
- **Starter grant could still fire before the player had really landed.** Being near the spawn altar was
  treated as arrival on its own, so a character still descending was granted roughly 9m above the altar.
  Altar proximity now only buys extra vertical allowance (the stamped platform genuinely sits above base
  terrain, which `WorldGenerator.GetHeight` knows nothing about) - it is no longer a substitute for having
  touched down. A short settle delay was added on top, so the drop lands at the player's feet rather than
  raining down behind them.

### Changed
- **Logging that matters no longer hides behind verbose mode.** Ledger *rejections* - an item that could
  not go where the mod intended - now always log, as does the stack-capacity summary, and a one-line
  storage summary at startup states whether stack sizing and grid growth actually engaged and by how much.
  Individual transfers stay on verbose. Diagnosing a suspected duplication previously required turning
  verbose on and reproducing it, which meant the interesting event had already been missed.

## 0.2.4

Both fixes below were diagnosed from a live 0.2.3 server log rather than from reading the code.

### Fixed
- **Starter kit and boat delivered mid-air to anyone who watches the intro.** The grant had a 60-second
  fallback that fired regardless of where the character actually was. Players who skip the intro spawn at
  the altar and were fine; players who sit through the Valkyrie flight were still airborne when it expired,
  so the kit was dropped from the sky and the boat was placed in whatever water could be found from up
  there - one grant landed at (539, 190, 275) with its Karve dumped 266m away. The grant no longer fires on
  a timeout at all: it waits for a genuine arrival, which is safe because the intro ends at the same spawn
  the skip route uses. The ground-height test is now also paired with a movement check, since elevation
  alone reads as "landed" whenever the Valkyrie passes over high terrain.
- **Starter boats piling up on one spot.** Every player spawning at the same altar asked for the nearest
  water and got the same answer, so hulls stacked - four Karves ended up within a metre of each other.
  Candidate spots are now rejected if another ship already sits within 12m, so the search walks outward to
  clear water. Checked against ship ZDOs rather than live objects, since a dedicated server has no instance
  for a hull nobody is standing next to.

## 0.2.3

Audited every change in 0.2.1/0.2.2 against the 1.0.7 dedicated-server decompile before release. Neither
of those versions ever shipped, and the audit found that several of their fixes did not work the way they
were described - the entries below supersede them.

### Fixed
- **Chest vacuum item loss and endless re-adding.** Two separate defects. First, the partial-move path wrote a
  reduced stack back to the ground item but never destroyed it, and nothing else removes a ZDO from the sector
  index - so that item was returned by every future scan forever, and the chest kept growing from it. Second,
  the same path double-counted: vanilla's `Inventory.RemoveItem` decrements the stack in place, so subtracting
  the moved amount a second time silently destroyed the difference on every partial vacuum.
  Vacuuming is now all-or-nothing: the container's real capacity is checked, the stack is added, the arrival is
  **verified by counted quantity**, the container ZDO is committed, and only then is the ground copy destroyed.
  If the container cannot take the whole stack the item is left on the ground untouched and the reason logged.
- **Auto-harvest spawning items from the same bush repeatedly.** The server cannot make a bush "picked" stick:
  writing `s_picked` never reaches an already-loaded `Pickable` (applying a ZDO fires no callback, so the live
  component keeps its old `m_picked`, which is the only thing gating interaction and the berry visual), the
  `RPC_SetPicked` broadcast only lands on peers that happen to have that bush instantiated at that instant, and
  vanilla's own `ReleaseNearbyZDOS` hands ownership back to the nearby player within ~2 seconds. The sweep
  therefore now keeps its own harvest ledger and refuses to re-harvest a bush until its real respawn time has
  genuinely elapsed, instead of trusting a flag the server cannot hold.
- **Starter kit never granted to some players.** The "position not initialised yet" check returned before the
  wait timer accrued and had no timeout of its own, so any character reporting a position at the world origin
  was skipped on every poll, forever, with nothing logged. The timer now accrues first, and a stuck character
  is reported by name in the log instead of silently never being granted.
- **Starter boat placed far out to sea.** The water search required all four cardinal probes around a candidate
  to be open water, which rejects exactly the shorelines and coves near a typical spawn and pushed the boat
  hundreds of metres out. Three of four is now enough - a boat against a shoreline has a dry side by definition.

### Known limitations
- The in-game chat commands added in 0.2.2 (`/cache`, `/cache claim`) do not currently work on a dedicated
  server. Chat is relayed as per-recipient targeted packets, and a dedicated server only dispatches a routed
  RPC locally when it is the target or the message is a broadcast, so the `Chat.RPC_ChatMessage` hook never
  fires for player-typed text. The reply path is correct; the receive path needs to hook the routing layer.
  Cached items still drain back into nearby chests automatically, which does not depend on chat.

## 0.2.2

### Added
- **Persistent Server-Side Item Cache (`ItemCache`)**: Any container overflow from `GridGrowth` or `StackCapacity`
  that exceeds vanilla slot or stack bounds and cannot fit into sibling chests is safely stored in `ItemCache`
  rather than being left in the container where vanilla clients would silently discard it on open.
  - Automatically drains cached items back into nearby chests as soon as slots become available (e.g. placing new chests or emptying existing ones).
  - Persisted to disk across restarts using native `ZPackage` binary format (`Wonderland.Cache.<WorldName>.dat`).
  - Native in-game chat commands for all connected vanilla players (Steam, Xbox, Crossplay): `/cache` displays summary, and `/cache claim` drops cached items at the player's feet.

### Fixed
- **Chest vacuum infinite item duplication**: Fixed an issue where the vacuum engine left ground items in the world
  while continuously adding their values to nearby chests on every sweep. `ZDOMan.instance.DestroyZDO` is a silent
  no-op unless the server owns the ZDO; the server now claims ownership before destruction and stack reduction,
  and guards against duplicate processing in the same sweep batch.
- **Auto-harvest bush duplication and visual berry desync**: Fixed an issue where swept bushes spawned item drops but
  remained visually and logically unpicked on connected clients. The server now claims ownership and broadcasts vanilla
  `RPC_SetPicked(true)` across the network, updating the visual berry meshes and interaction flags on connected clients,
  and cleanly destroys ZDOs for non-respawning pickables (branches, stones, etc.).
- **Starter kit delivery reliability**: Fixed an issue where only some players received the starter kit on first spawn.
  The server now detects ground contact (waiting out the high-altitude Valkyrie intro flight) before spawning the kit,
  and snaps dropped items to terrain/altar elevation so they never fall through the world or drop mid-air.
- **Starter boat distance**: Sited the starter boat at the closest shoreline to the spawn altar by running the water
  search from the player's landed ground coordinates with finer 5m ring steps, 1.1m depth, and 2.0m clearance check.

## 0.2.1

### Fixed
- **Starter boat placed in water rather than at spawn.** Fixed an issue where starter boats were placed on dry land
  directly at the player's spawn position because the old 24-point 60m search failed to reach water from inland sacrificial stones.
  The water finder now performs an expanding concentric-ring scan using `WorldGenerator.instance.GetHeight` (pure noise math,
  no colliders/zones needed) verifying water depth (≥1.5m) and open-water clearance, orients the boat seaward, and automatically
  adds a vanilla map pin discovery on the player's map (`StarterBoatMapPin`).
- Raised default `StarterBoatSearchRadius` from 60m to 300m (with slider ceiling expanded to 1,500m) and added expanding
  search fallback up to 1,200m so the boat is never beached on land even on large starter islands.

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
