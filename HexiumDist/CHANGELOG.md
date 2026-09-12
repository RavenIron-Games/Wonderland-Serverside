# Changelog

## 0.7.2

### Added
- **Status Effect Roster.** A curated, config-driven set of EXISTING vanilla status effects (potions, trinket
  effects, guardian powers) can be kept active on every connected player, entirely server-side: `SEMan.AddStatusEffect`'s
  non-owner path is a routed RPC the client executes with its own already-loaded copy of the named asset, the same
  wire pattern already used for HUD toasts - no client mod, ever. Distinct effects stack additively and a
  server-triggered grant bypasses vanilla's "one potion effect per category" rule, so several roster slots combine
  freely. There is no removal RPC, so disabling a slot means "stop renewing it," not an instant revoke. New config
  section `16 - Status Effect Roster`, 14 slots total:
  - On by default: `GP_Moder`, `Potion_hasty`, `TrinketIronStamina` (+40% run speed combined while actively steering
    a ship, +30% on land - the honest ceiling from every positive vanilla speed asset that exists), `Potion_swimmer`,
    `TrinketChitinSwim` (swim-stamina cost cut way down, swim-only).
  - `GP_Moder` specifically only applies while actively steering a ship (`BuffRoster_GP_Moder_RequireBoat`, on by
    default) - matches what that guardian power is really for in vanilla (sailing against the wind) rather than an
    always-on speed buff. Detected via the ship's own steering-user ZDO field (`ZDOVars.s_user`), the same one the
    game itself sets the instant you take the wheel and clears the instant you let go - **live-confirmed working**,
    including re-granting correctly after letting go and re-taking the wheel on the same or a different ship.
  - Off by default (opt-in): `Warm`, `Potion_stamina_lingering`, `Potion_tasty` (stamina/eitr regen boosts - stack
    *multiplicatively* with `StaminaRegenRateMultiplier` below if both are used, see that setting's description),
    `Rested` (health/stamina/eitr regen + faster skill gain, permanently), and four more boss guardian powers -
    `GP_Eikthyr` (-60% run/jump/swim stamina), `GP_Bonemass` (free blocking + physical resistance), `GP_TheElder`
    (+health regen + more chop/pickaxe damage), `GP_Yagluth` (+damage, lightning resistant), `GP_Queen` (free
    sneaking + eitr regen x2).
- **Native stamina regen rate.** `StaminaRegenRateMultiplier` (default 3.0x) drives Valheim's own
  `GlobalKeys.StaminaRegenRate` world modifier - the same proven, zero-client-install mechanism already shipping as
  `CarryWeightMultiplier`. Compounds multiplicatively (not additively) with any Status Effect Roster entry that also
  touches stamina regen - see the setting's own description for the concrete math.
- **Config hot-reload.** The `.cfg` file is polled every 5 seconds; an on-disk edit is picked up and applied without
  a server restart, the same live-tested pattern already proven on this testbed by the sibling GetOffMyLawn mod.
- **Discord: deaths, first-time joins, boss defeats, and an optional heartbeat.** Four new independent
  announcements, all detected from server-visible state alone: a death (the character ZDO's own `s_dead` flag
  flipping false->true), a character's first time ever connecting to this world (its own tracking key, independent
  of `StarterGrantEnabled`), any of the five classic bosses being defeated for the first time (Eikthyr, The Elder,
  Bonemass, Moder, Yagluth - read from the same native world global keys the game itself sets, never re-announced on
  a later restart), and an optional periodic heartbeat post showing uptime and who's currently online
  (`DiscordNotifyHeartbeat`, off by default). Deaths/joins/boss-defeats are on by default and always log locally too,
  even with no Discord webhook configured, so nothing is silently lost if Discord isn't set up.
- **Heartbeat log line.** `HeartbeatEnabled` (on by default, section `1 - General`) logs one summary line every
  `HeartbeatIntervalMinutes` (default 15) with uptime and who's online, so an admin tailing the log can confirm the
  mod is alive without turning on full verbose logging. Shares its interval with the optional Discord heartbeat above.

### Changed
- **`CarryWeightMultiplier` default raised from 1.0x to 2.0x** (existing installs keep whatever value is already in
  their `.cfg` - this only changes the default for a fresh install).
- **Night Spawns now cover the full Seeker, Charred, and Fenring families by default.** `NightSpawnBlockedCreatures`
  gained `Seeker`, `SeekerBrood`, `SeekerBrute`, `SeekerQueen`, `Charred_Archer`, `Charred_Archer_Fader`,
  `Charred_Mage`, `Charred_Melee`, `Charred_Melee_Dyrnwyn`, `Charred_Melee_Fader`, `Charred_Twitcher`,
  `Charred_Twitcher_Summoned`, `Fenring`, `Fenring_Cultist`, `Fenring_Cultist_Hildir`, `Fenring_Cultist_Hildir_nochest`
  (every real prefab name confirmed against a live ZNetScene prefab dump, not guessed), and `NightSpawnBlockedBiomes`
  gained `Mistlands`, `AshLands`, and `Plains` to match - both lists must agree for a spawn to be blocked, so the
  biome list had to grow too or the new creature names would have done nothing by default.
- **Every config setting written in plain language**, leading with what it actually does in practice before any
  technical detail.

### Fixed
- **Status Effect Roster: a short-lived effect (`Rested`, 1-second vanilla duration) could flicker off between
  re-applications.** The roster's re-ping scheduler had a hardcoded 2-second floor that was slower than `Rested`'s
  own natural duration; it's now a per-tick real-time cooldown with no such floor, so even very short-lived vanilla
  effects stay reliably active.
- **Status Effect Roster could silently resolve zero assets on some boots.** `ObjectDB.instance` is sometimes still
  null at the exact moment this mod otherwise treats the world as "ready" (a genuine Unity initialization-order race,
  not a fixed sequence) - the roster now retries until `ObjectDB.instance` actually exists instead of giving up after
  one attempt. The existing All Items Float feature had this same latent race; it was never visibly affected only
  because it has a second, independent way of finding item prefabs.
- **`GP_Moder` never re-granted after the first time.** Leaving the ship's wheel mid-cooldown left the roster's
  re-ping timer *frozen* rather than reset, so a player who got on and off the helm in anything shorter than a full
  120-second window could go the rest of the session without a single re-grant after the very first one. Re-taking
  the wheel - the same ship or a different one - now grants immediately regardless of how much of the previous
  cooldown was left. **Live-confirmed working end to end.**

## 0.6.5

### Fixed
- **All Items Float: live Fish no longer get frozen at the surface.**
  `Fish` (the swimming creature prefab) carries its own `ItemDrop` component — used for hand-pickup fish and
  to remember quality — on the very same GameObject as its swim AI. The buoyancy sweep was classifying every
  `ItemDrop`-bearing prefab as dropped loot, so it also matched live fish: claiming ZDO ownership, zeroing
  their Rigidbody velocity, and pinning `pos.y` to a fixed water-surface height every sweep tick. That fought
  `Fish.Update()`'s own swim/wave/dive logic every frame, leaving fish stuck bobbing rigidly at the surface
  instead of swimming naturally at depth.

  `OnWorldReady` now skips any prefab with a `Fish` component when building the tracked-prefab set, so the
  sweep, the ownership guard, and the forced-`Floating` pass never touch live fish — vanilla's own native
  `Floating` component (Fish already has one) continues to handle them untouched.

## 0.6.4

### Fixed
- **Security: Portal transit no longer triggers speed flag.**
  `PositionWatch` samples player position every 3 seconds. A portal hop moves a player across the
  entire world map in that window — the test server observed 109.7 m/s, nearly 3× the 40 m/s ceiling
  — producing a false-positive `SECURITY:PositionWatch` audit entry on every portal use.

  `IsLegitimateTransit()` is now evaluated before any flag fires. It suppresses the alert for three
  classes of legitimate high-speed movement:

  1. **Portal transit** (`IsPortalTransit`): iterates `ZDOMan.instance.GetPortals()` (the live
     dictionary, no heap allocation) checking if either position sample is within 40 m of a portal
     ZDO. Falls back to the portal's linked `ZDOExtraData.ConnectionType.Portal` target ZDO with a
     60 m extended radius to tolerate players who walked a bit before the sample window opened.
  2. **Dungeon / interior entry-exit**: `Character.InInterior()` (verified from the 1.0.7 decompile:
     `position.y > 3000f`) changing between samples, or either sample above 2 000 m, or a vertical
     delta exceeding 1 000 m — all reliable signatures of zone-transition teleportation.
  3. **Admin teleport**: peer socket hostname resolves as an admin via `ZNet.instance.IsAdmin()`.

  Additionally: respawn is now tracked via `ZDOVars.s_dead` state. The tick immediately following a
  death→respawn transition is silently skipped, since spawning at a bed or world spawn produces the
  same magnitude of position jump as a portal.

  Fly/noclip height-check is also skipped while a player is inside a dungeon interior
  (`pos.y < 2 000 m` guard), preventing `WorldGenerator.GetHeight` comparisons against terrain that
  sits thousands of metres below the instance.

## 0.6.3

### Fixed
- **Item Buoyancy: Robust Headless Water & Ground Detection.**
  - **Headless Terrain & Water Floor Resolution:** Previously, `IsWaterborneItem` relied on `Floating.GetLiquidLevel`
    (which queries `WaterVolume` physics colliders) and `ZoneSystem.GetGroundHeight` (which raycasts against `terrain` colliders).
    On headless dedicated servers, neither collider type exists for items, causing liquid detection to return `-10000f`
    and items to sink to the seabed.
  - **Three-Tier Ground Height:** Now resolves ground elevation via `Heightmap.GetHeight(worldPos, out h)` (including player
    terrain modifications), falling back to procedural `WorldGenerator.instance.GetHeight(pos.x, pos.z)`. Any location outdoors
    where `groundHeight < ZoneSystem.m_waterLevel` (30m) is recognized as open water.
  - **Vertical Waterborne Window:** Fixed the check that previously disqualified items once lifted to `targetY`. The upper
    bound now correctly encompasses items floating at the surface (`pos.y <= targetY + 0.25f`), keeping them continuously
    identified as waterborne, while safely excluding items on ship decks (`y >= 30.8m`) and docks (`y >= 31.5m`).
  - **Handoff Protection Window:** Added a 3-second pickup allowance on `RPC_RequestOwn` so the server never wrestles
    ownership away from a player while their vanilla client executes `Pickup()` into inventory.
  - **Sweep Cadence:** Default `FloatSweepInterval` reduced from 1.0s to 0.3s for near-instant buoyant bobbing. Sunken items
    are actively rescued and hoisted to the surface.

### Fixed
- **World Modifier & Capacity Sync: Patched GlobalKeys Dispatch.**
  Previously, `WorldRatesEngine` set the `carryweightrate` global key during `ZNetScene.Awake` via `ZoneSystem.SetGlobalKey`.
  On dedicated servers, `ZoneSystem.Start` had not yet registered the `"SetGlobalKey"` RPC with `ZRoutedRpc`, and subsequent
  `ZNet.WorldSetup` initialization cleared all 41 global keys via `SetStartingGlobalKeys`, wiping the modifier before
  any player joined.
  - **Direct Key Injection:** Now invokes `ZoneSystem.GlobalKeyAdd(key, true)` directly on the server instance.
  - **Lifecycle Harmony Patches (`WorldRatesPatches`):** Added patches on `ZoneSystem.SetStartingGlobalKeys` (postfix),
    `ZoneSystem.Start` (postfix), and `ZoneSystem.SendGlobalKeys` (prefix). Any time starting keys are reset or global
    keys are dispatched to connecting peers (`OnNewPeer`), the configured `carryweightrate` is guaranteed to be present
    in `m_globalKeys` before the network packet is serialized and sent to the vanilla client.

## 0.6.1

### Fixed
- **Container Auto-Vacuum Feedback: Switched to Fermenter VFX/SFX & Routed RPC.**
  Previously, `VacuumEngine.PlayVacuumEffect` called `Object.Instantiate` directly on the dedicated server
  for `vfx_auto_pickup`. Because dedicated servers run headless (`-nographics`) and `vfx_auto_pickup` has
  `m_persistent: false` and a 1-second timeout with unused player components, no visual effect was ever
  replicated or shown on vanilla clients.
  - **Replaced with the Fermenter Splash:** Now spawns vanilla's own fermenter liquid splash (`vfx_fermenter_add`)
    paired with the mead splash sound (`sfx_fermenter_add`), creating immediate, highly visible and satisfying
    feedback as containers gulp down items.
  - **Routed Network Broadcast:** Utilizes Valheim's native `ZRoutedRpc` `"SpawnObject"` mechanism, registered
    on every 1.0 vanilla client. The server broadcasts the effect specifically to connected peers within
    visible/audible range (80m) of the container, causing clients to execute `Object.Instantiate` locally with
    zero server memory/particle overhead, no network lag, and 100% vanilla client compatibility.
  - **Configurable Prefabs:** Added `VacuumEffectPrefab` (default `vfx_fermenter_add`) and `VacuumSoundPrefab`
    (default `sfx_fermenter_add`) in `2 - Vacuum & Auto-Harvest` for server administrators to fully customize
    or silence the effect.

## 0.6.0

### Added
- **All Items Float: strictly server-side buoyancy for all dropped items.** Solves Valheim's oldest
  hazard — metal, ores, scrap, gear, and serpent scales sinking to the inaccessible ocean floor — with
  zero client-side installation. Deep-dive audited against the 1.0.7 decompile: vanilla only attaches the
  `Floating` MonoBehaviour to wood, fish and tombstones, while clients simulate physics locally for any
  item they own. Wonderland solves this by: (1) enhancing all server-side `ItemDrop` prefabs in `ObjectDB`
  with `Floating`, (2) scanning submerged item ZDOs near players and lifting them to the water surface,
  (3) maintaining server ownership (`zdo.SetOwner(serverSession)`) with revision escalation so client
  physics cannot sink them, (4) guarding `ZDOMan.ReleaseNearbyZDOS` from passively re-assigning waterborne
  items to nearby players, and (5) intercepting `ZRoutedRpc.HandleRoutedRPC` for `RPC_RequestOwn` so
  players pressing E or entering auto-pickup range immediately receive ownership and execute `Pickup()`.
  Configurable via `AllItemsFloatEnabled` (toggle on/off, default on), `FloatSurfaceOffset`, and
  `FloatSweepInterval` in `2 - Vacuum & Auto-Harvest`.
- **Adjustable Max Carry Capacity: native 1.0 world modifier scaling.** Earlier releases documented
  carry weight as "confirmed impossible server-side on any Valheim build" based on pre-1.0 unnetworked
  player fields. Deep-dive audit of the 1.0.7 decompile revealed Valheim 1.0's native World Rate system:
  `Player.GetMaxCarryWeight()` computes `(base + statusEffects) * Game.m_carryWeightRate`, which reads
  `GlobalKeys.CarryWeightRate` (`"carryweightrate <int_percentage>"`). The dedicated server broadcasts
  this key to vanilla clients via `ZRoutedRpc` `GlobalKeys`. Stock vanilla clients receive the key, display
  the increased number directly in their inventory GUI (e.g. `0 / 600`), and enforce encumbrance and
  auto-pickup against the higher ceiling with zero client mods. Configurable via `CarryWeightMultiplier`
  in new section `15 - World Modifiers & Capacity` (default `1.0` vanilla; `1.5` = 450 lbs base; `2.0` =
  600 lbs base). Dynamic config changes broadcast immediately to all connected players in real time.

## 0.5.2

### Fixed
- **Structure Upkeep not repairing anything a player could see.** Five compounding defects, each
  verified against the 1.0.7 server decompile. (1) The scan asked vanilla's one-prefab-at-a-time
  `GetAllZDOsWithPrefabIterative` for each of the **797** WearNTear-bearing prefab types in turn, and
  each call re-walks all 262,144 sector slots (`new ZDOMan(512)`), so a single full cycle ran to hours -
  slower than rain decay. (2) Even when a piece was reached, the owning client never learned about it:
  `WearNTear` caches health in `m_healthPercentage` and only refreshes it in `Awake` or
  `RPC_HealthChanged`, so the piece kept its worn material and damaged hover text, and - since
  `UpdateWear` gates rain damage on `GetHealthPercentage() > 0.5f` - went on behaving as if still at
  half health until the zone reloaded. (3) It called `SetOwner` first, which `ZDO.Set` never needed,
  and which actively broke the hammer: `ZNetView.InvokeRPC` routes to `ZDO.GetOwner()`, so the
  player's own `RPC_Repair` went to a server with no instance to receive it. (4) No creator check, so
  every deliberately pre-damaged world-gen ruin was being "repaired" too. (5) Repaired to the prefab's
  base `m_health`, which `WearNTear.Awake` scales by world level on the client, so on a world-level
  server every repair landed permanently short and was re-written every sweep.
  Now: a per-player pass over each connected player's active area (`FindNear`, ~128m - decay can only
  ever happen inside a client's active area, so this is the ground that matters and it is a few
  sectors instead of a quarter-million), plus a background full-map sweep that matches a HashSet of
  all 797 prefab hashes in **one** walk of the sector array (new `ZdoSpatialQuery.PrefabSetSweeper`).
  Each repair writes the ZDO without touching ownership, then routes vanilla's own `RPC_HealthChanged`
  by ZDOID so the client's cached percentage, visuals and hover text update at once (capped at 256
  broadcasts per sweep so the first pass over an old world can't flood the RPC queue; anything past the
  cap is still repaired and catches up on zone reload). Player-built pieces only by default (creator
  != 0, exactly `Piece.IsPlacedByPlayer()`). Repairs now log a per-sweep count so the feature is
  observable. Config: `StructureUpkeepBatchSize` is replaced by `StructureUpkeepSectorsPerSweep`
  (its unit changed - populated sectors, not scanner calls); new `StructureUpkeepPlayerRadius` and
  `StructureUpkeepPlayerBuiltOnly`.

## 0.5.1

### Fixed
- **Container Rows expanding unpredictably.** The row-growth sweep round-robinned through every
  container-bearing prefab in the game - every `TreasureChest_*` variant, dungeon pot, tar pit, cargo
  crate, 64 types on a stock install - to find the ~18 that are actually player-buildable and eligible
  to grow. Most of the sweep's budget went to prefabs that always failed the eligibility check, which
  is what made a specific chest's turn to be visited (and therefore anchored into its grown size)
  arrive so unpredictably. The scanner is now built from just the eligible set, rebuilt every 60s so an
  edit to `ContainerRowsExcludedContainers` still takes effect without a restart. Verified live: the
  boot log now reads `x2 rows on 18 of 64 container type(s)` instead of scanning all 64.
- **Ship cargo silently excluded from every container feature.** Every container lookup in the mod
  resolved a prefab's `Container` component from its root GameObject only. Karve, VikingShip and
  VikingShip_Ashlands build their cargo hold the way vanilla builds it - a *child* object carrying its
  own `Container` with `m_rootObjectOverride` pointed at the ship's own ZNetView, so the item blob
  saves under the ship's ZDO - which a root-only lookup never finds. Ships were therefore invisible to
  Container Rows, the vacuum, the overflow guard, background sort, and the item-integrity sweep alike.
  `ContainerRegistry` now resolves a container template from the root or, failing that, any child, and
  every engine goes through that one resolver. Verified live against a real dedicated-server boot:
  Karve (2x2->2x4), VikingShip (6x3->6x6) and VikingShip_Ashlands (8x4->8x8) now appear in the eligible
  set and grow/vacuum exactly like a land chest. Raft correctly gets none of this - it has no cargo
  hold in vanilla, confirmed by elimination on the same live run.

### Added
- **A visual cue when a container vacuums ground items.** Spawns vanilla's own `vfx_auto_pickup`
  prefab - the sparkle vanilla plays for its built-in auto-pickup-nearby-items feature - at the
  container on a successful pull. It's a real networked, self-destructing object already shipped on
  the dedicated server, so a completely vanilla client renders it with nothing installed client-side.
  New `VacuumEffectEnabled` toggle in `2 - Vacuum & Auto-Harvest` (default on).

## 0.5.0

### Added
- **Discord Notify: webhook announcements for server status and player logins.** Posts to a Discord
  webhook when the world finishes loading and on shutdown, and when a player connects or disconnects -
  configurable message templates with `{world}`/`{player}` placeholders and a display username. New
  config section `14 - Discord Notify`, entirely **local to this server** (never synced to clients,
  unlike almost every other setting) since a webhook URL is a per-server secret.
  - Join/leave detection hooks `ZNet.RPC_PeerInfo` (verified against the decompile: the player-name
    field is only ever set after every rejection path - bad version, blacklist, full server, wrong
    password, duplicate connection - has already returned early, so a hook that checks it only fires
    for a connection that actually completed the handshake) and `ZNet.Disconnect` (the same vanilla
    teardown point the bundled ServerSync library already hooks). A peer is only announced as "left" if
    its "joined" was actually announced first, so a rejected or duplicate connection attempt can never
    produce a spurious leave message.
  - Webhook posts are fire-and-forget over one shared `HttpClient` (a new instance per post risks
    socket exhaustion under Mono) and best-effort: a failed or rate-limited post is logged and dropped,
    never retried. The shutdown/offline message is the one exception - it blocks up to 3 seconds, since
    the process can exit immediately after `OnDestroy` returns and would otherwise kill a fire-and-forget
    post before it ever ran.
  - Needed a plain `System.Net.Http` reference added to the project (confirmed present in the actual
    dedicated server's Managed folder, not pinned into libs-Tools since it's a stock .NET Framework
    assembly rather than a game DLL).

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
- Max stamina adjustment — confirmed impossible server-side on any Valheim build (neither value is ever written to a ZDO); not carried forward as a non-functional placeholder (note: max carry weight was solved in 0.6.0 via Valheim 1.0's world rate modifier system)

### Known limitations
- Max stamina: see above, not a bug, not planned (max carry weight is now fully supported as of 0.6.0)
- Crossplay servers have PlayFab's own separate, non-configurable 10-player lobby cap
