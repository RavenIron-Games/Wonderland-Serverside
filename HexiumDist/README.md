<div align="center">

# 🌐 Wonderland

![Valheim Mod](https://img.shields.io/badge/Valheim-Serverside_Automation-orange.svg)
[![Multiplayer Compatible](https://img.shields.io/badge/Multiplayer-Server--Synced-blue.svg)]()
[![Framework](https://img.shields.io/badge/Requires-BepInEx-red.svg)]()
[![Crossplay](https://img.shields.io/badge/Crossplay-PlayFab%2FXbox_Ready-purple.svg)]()
[![Valheim 1.0](https://img.shields.io/badge/Valheim-1.0.15_Server-green.svg)]()

*No client install, ever. The server does the work. Built and live-tested on Valheim 1.0.*

</div>

Wonderland is a strictly server-side automation and world-governance mod: every feature below runs
entirely on the dedicated server, operating on the world's raw ZDO data instead of live game
objects — which is what makes it work on a real headless server at all, with nobody connecting
needing to install a thing. Steam, Xbox, PlayFab, and full crossplay parties all get the identical
experience.

---

<a id="progression-ledger"></a>
## 👑 Automatic World Progression Ledger & Anti-Twink Security

> [!IMPORTANT]
> **"The world levels up when the boss falls, and the head is hung on the stones. That is our ledger."**  
> Wonderland dynamically ties server gear progression ceilings, equipment surveillance, and anti-cheat kicking directly to Valheim's persistent boss defeat registry (`defeated_<boss>`). No more players connecting with endgame gear (twinking) to bypass your server's survival milestones.  
> 🛡️ **Admin Freedom**: Authenticated admins on `adminlist.txt` are **100% exempt** from all checks (`EquipmentGuardAdminBypass = true`) — an admin can wield, wear, spawn, or store whatever they want, whenever they want!

### How Progression Works
1. **The World Level-Up Record**: In Valheim, when a boss falls and its trophy is hung at the sacrificial stones, the dedicated server permanently registers an authoritative world global key (`defeated_gdking`, `defeated_bonemass`, etc.). Wonderland reads these keys in real time to establish the active allowed tier.
2. **Baseline: Starting at Black Forest (Skipping Eikthyr)**: Because Wonderland's Starter Grant equips arriving Vikings with Bronze tools and a Karve on day one, the baseline world tier begins at `BlackForest`. Eikthyr is bypassed for tier gating.
3. **Defeating The Elder Unlocks the Iron Age**: Until The Elder falls, Iron armor, banded shields, and swamp weapons are prohibited — Vikings equipping Iron are kicked or flagged. Defeating The Elder immediately unlocks the Swamp tier.
4. **Real-Time Live Unlock & Zero False Positives**: The instant a boss dies anywhere in the world, the server steps up the world tier, proclaims the milestone across the server and Discord, and flushes all player violation caches so players crafting new gear are never falsely warned or kicked.
5. **Dual Enforcers**:
   - **`EquipmentGuard`**: Continuously monitors equipped visual weapons, shields, armor, capes, and magic staves & robes on player character ZDOs, plus impossible upgrade levels (gear upgraded past its base cap at Valheim 1.0's upgrader stations is recognized and exempt). Wielding prohibited gear logs to `wonderland_security.log`, and optionally auto-kicks the violator (`EquipmentGuardKick = true`).
   - **`ItemSanityGuard`**: Sweeps player-built chests to detect and optionally purge smuggled endgame gear or developer cheat items stashed ahead of progression.
6. **Admin Immunity**: Authenticated server administrators on `adminlist.txt` completely bypass all equipment, quality, and progression tier checks (`EquipmentGuardAdminBypass = true`). Admins can do whatever with whatever!
7. **Raw Drops Are Free, Processed & Crafted Is Gated**: Wonderland does not guess tiers from item names. At world start it reads the game's own recipes, smelter and cooking conversions, and crafting station build costs, and an item's tier is simply the highest boss-gated ingredient or station anywhere in its crafting chain: The Elder unlocks the crypts and therefore iron, Bonemass leads you to silver, Moder's tear builds the artisan table and everything downstream of it (black metal, linen, barley flour), the Mistlands stations gate Mistlands gear, and flametal gates the Ashlands. Anything that drops straight into your bag with no recipe behind it is unrestricted: the scrap, pelts, needles, barley, and mushrooms you carried home from a biome you dared to visit are never confiscated. The derived table is written every boot to `BepInEx/config/Wonderland.ProgressionTiers.txt` (one line per item with the tier and the reason), and any item can be pinned to a different tier, or freed entirely, with `ProgressionItemExemptions` (`Prefab:Tier` or just `Prefab`) live with zero server restarts.

### Progression Unlock Matrix

| World State / Boss Defeated | Global Key | Unlocked Max Tier | Allowed Gear & Materials | Gated / Blocked Ahead of Progression |
| :--- | :--- | :--- | :--- | :--- |
| **Baseline** (Day 1 / Pre-Elder) | *(None)* | `BlackForest` | Every raw drop from any biome, Bronze weapons/armor/tools, Troll hide, Root armor, Chitin, everything from the cauldron | Iron, Silver, Black metal, Linen, Eitr, Flametal and all gear made from them |
| **The Elder** slain | `defeated_gdking` | `Swamp` | Iron armor & weapons, Banded shield, Huntsman bow, Iron pickaxe, Iron nails | Silver, Black metal, Eitr, Flametal |
| **Bonemass** slain | `defeated_bonemass` | `Mountain` | Silver weapons, Wolf armor, Drake helm, Frostner, Fang spear, Lox cape (silver-trimmed) | Black metal, Eitr, Flametal |
| **Moder** slain | `defeated_dragon` | `Plains` | Black metal weapons & shields, Padded armor, Porcupine, Linen, Bread and every windmill/oven food | Black forge & galdr table gear, Eitr, Flametal |
| **Yagluth** slain | `defeated_goblinking` | `Mistlands` | Carapace gear, all Eitr magic staves & robes, Krom, Mistwalker, Feather cape, refined Eitr | Flametal, Ashlands gear |
| **The Queen** slain | `defeated_queen` | `Ashlands` | Flametal weapons & armor, Ashlands armor sets, Ashlands staves | Bloodgold gear, Frost Foundry casts, Timberwood, Nornathread |
| **Fader** (Ashlands boss) slain | `defeated_fader` | `DeepNorth` | Bloodgold weapons, armor, shields and staves, everything cast at the Frost Foundry - fully unrestricted end-game tier | None |

Raw drops - scrap, ore you cannot yet smelt, pelts, needles, barley, flax, mushrooms, sap, black marble, yggdrasil wood, trophies - are never gated, whatever biome they came from. The exact placement of every item, with the reason, is in `BepInEx/config/Wonderland.ProgressionTiers.txt` after each boot.

---

<details>
<summary>📜 <b>Contents</b></summary>

- [👑 Automatic Progression Ledger & Anti-Twink Security](#progression-ledger)
- [🌱 Features](#features)
  - [🧲 Vacuum & Auto-Harvest](#vacuum-auto-harvest)
  - [🌊 All Items Float](#all-items-float)
  - [⚖️ Carry Capacity](#carry-capacity)
  - [🏃 Stamina Regen Rate](#stamina-regen-rate)
  - [🧪 Status Effect Roster](#status-effect-roster)
  - [🔥 Production Supply](#production-supply)
  - [🗂️ Background Sort](#background-sort)
  - [📐 Container Rows](#container-rows)
  - [📦 Item Cache & Overflow Guard](#item-cache-overflow-guard)
  - [🎮 Player Controls](#player-controls)
  - [⚔️ Raids & Night Spawns](#raids-night-spawns)
  - [👥 Player Cap](#player-cap)
  - [🏚️ Structure Upkeep](#structure-upkeep)
  - [🎁 Starter Grant](#starter-grant)
  - [🛡️ Security & Anti-Cheat](#security-anti-cheat)
  - [📣 Discord Notify](#discord-notify)
  - [💓 Heartbeat](#heartbeat)
  - [🤖 BarrkBOT Export](#barrkbot-export)
  - [🧬 Valheim 1.0 Native](#valheim-10-native)
- [⚙️ Configuration](#configuration)
  - [Server-Synced (Admin Controlled)](#server-synced-admin-controlled)
  - [Local to Your Game](#local-to-your-game)
- [📦 Dependencies](#dependencies)
- [📥 Installation](#installation)

</details>

---

<a id="features"></a>
## 🌱 Features

<a id="vacuum-auto-harvest"></a>
### 🧲 Vacuum & Auto-Harvest
Containers and carts quietly pull in matching ground items within a configurable radius — **match-required**, so a chest only tops up an item type it already holds and never has a new one sprout inside it. Mining spoils, harvest drops, anything on the ground near a linked container just walks itself home — within a couple of seconds of landing near you. What you are reaching for stays yours: a chest claims a stack for a moment before it takes it and steps aside the instant a player asks for it, so a pickup and a vacuum never both win. When unclaimed items settle, a subtle physics wakeup vector is applied so vanilla client physics immediately wakes the item's Rigidbody, ensuring it drops naturally under gravity rather than floating in mid-air. Swimming fish, a feast on the table and an egg warming by the fire are never touched. This includes **ship cargo** — a docked or beached Karve or VikingShip vacuums nearby matching items exactly like a chest does. A visual liquid splash and splash sound (vanilla's own fermenter effect `vfx_fermenter_add` and `sfx_fermenter_add`) play at the container on a successful pull via vanilla's native routed `SpawnObject` RPC, so it is clearly visible and audible to nearby players with nothing installed client-side; turn it off with `VacuumEffectEnabled` or customize the prefabs via `VacuumEffectPrefab` / `VacuumSoundPrefab`. Paired with this: **pick one and the rest follow** — press E on a single berry bush, mushroom or carrot and every ripe one of the same type within the radius is harvested with it, each drop landing exactly where its plant grew so a matching chest in range pulls the lot straight in — one keypress can put a whole carrot patch in the chest beside it. Ores, tar and dungeon loot are never swept. Exclusion lists (containers and items) keep this out of anything you want left alone.

<a id="all-items-float"></a>
### 🌊 All Items Float
All dropped items — ores, raw metals, scrap, tools, weapons, armor, trophies, and serpent scales — float on water instead of sinking to the ocean floor. Genuinely 100% server-side: vanilla clients see them bobbing and resting on the water surface, readily collected while swimming or sailing past, with zero client mods installed. Vanilla only attaches buoyancy components to wood, fish, and tombstones, while clients simulate physics on objects they own; Wonderland enhances server item prefabs, maintains surface elevation and server ownership on waterborne items, and immediately grants ownership when a player presses E or walks into auto-pickup range so pickup is instant. Items resting on coastal banks or river shores are verified against dry ground elevation so land items are never falsely lifted into the air. Configurable toggle (`AllItemsFloatEnabled`, default on), sweep interval, and surface elevation offset in `2 - Vacuum & Auto-Harvest`.

<a id="carry-capacity"></a>
### ⚖️ Carry Capacity
Scale player max carry weight completely server-side via Valheim 1.0's native World Modifier system (`Game.m_carryWeightRate`). Stock vanilla clients receive the rate via the vanilla `GlobalKeys` network channel, display the increased limit directly in their inventory GUI (e.g. `0 / 600`), and enforce encumbrance and auto-pickup against that higher ceiling with no client mods. Configurable via `CarryWeightMultiplier` in `15 - World Modifiers & Capacity` (default `2.0` = 600 lbs base / 900 with Megingjord; `1.0` is vanilla). Live config changes broadcast to all connected players immediately without a server restart.

<a id="stamina-regen-rate"></a>
### 🏃 Stamina Regen Rate
The same native World Modifier mechanism as Carry Capacity above, targeting Valheim's own `Game.m_staminaRegenRate` instead. `StaminaRegenRateMultiplier` (default `3.0`) scales passive stamina regeneration for every connected player — completely server-side, arbitrary multiplier, zero client mods, live-updating the moment the config changes. Configurable in `15 - World Modifiers & Capacity`. Note: this stacks *multiplicatively*, not additively, with any Status Effect Roster entry below that also boosts stamina regen — see that setting's own in-file description for the exact math before combining both.

<a id="status-effect-roster"></a>
### 🧪 Status Effect Roster
Keeps a curated set of **existing vanilla status effects** — potions, trinket effects, guardian powers — permanently active on every connected player, entirely server-side. This isn't a custom buff system: Wonderland can't invent a new status effect (nothing that isn't already compiled into every vanilla client can ever be granted this way), but it *can* remotely trigger any status effect a stock client already has, using the same vanilla networking channel the game itself uses for potions and guardian powers. Distinct effects genuinely stack — the server-triggered grant bypasses vanilla's usual "one potion effect per category" rule — so several roster entries combine into one compound buff.

Each roster slot is its own on/off switch in `16 - Status Effect Roster`:

| Slot | Effect | Default |
| :--- | :--- | :--- |
| `GP_Moder` | +10% run speed, +300 carry weight, Frost Resistant, lets a boat sail against the wind | On, while steering only (see note below) |
| `Potion_hasty` | +15% run speed | On |
| `TrinketIronStamina` | +15% run speed | On |
| `Potion_swimmer` | -50% swim stamina cost only (run/jump untouched) | On |
| `TrinketChitinSwim` | -80% swim stamina cost only, +50% swim speed | On |
| `Warm` | Stamina + eitr regen ×2, naturally permanent | Off |
| `Potion_stamina_lingering` | Stamina regen +25% | Off |
| `Potion_tasty` | Stamina regen ×2, short-lived | Off |
| `Rested` | Health/stamina/eitr regen boost + faster skill gain, permanently | Off |
| `GP_Eikthyr` | -60% run/jump/swim stamina cost, all at once | Off |
| `GP_Bonemass` | Free blocking + resistance to physical damage | Off |
| `GP_TheElder` | +health regen, +damage chopping/mining | Off |
| `GP_Yagluth` | +damage all types, farming skill boost, lightning resistant | Off |
| `GP_Queen` | Free sneaking, eitr regen ×2, poison resistant | Off |

All three run-speed slots on by default stack to **+40% run speed while actively steering a ship** (+30% on land, since `GP_Moder`'s +10% only applies at the helm). This matches what that guardian power is really for in vanilla — sailing against the wind — rather than being an always-on speed buff. Detection is read directly off the ship's own steering-user field (`ZDOVars.s_user`, the same one the game itself sets the instant you take the wheel and clears the instant you let go) — confirmed live tracking correctly across two different ships. `BuffRoster_GP_Moder_RequireBoat` (on by default) is what scopes it to steering specifically — standing on deck without hands on the wheel does not count — turn that setting off if you'd rather have it always-on everywhere instead. Because there's no vanilla channel to force an effect off early, disabling a slot means "stop renewing it" — it fades out on its own natural duration rather than being instantly revoked.

<a id="production-supply"></a>
### 🔥 Production Supply
Fireplaces, hearths, torches, smelters and kilns keep themselves fed from the chests around them — **fuel and ore tracked separately**, so a smelter never idles on a full coal bin. It works whether or not anyone is online, never drains a chest below your **reserve floor**, only ever loads a charcoal kiln with the wood you allow, and any station can be switched off with an emote (see Player Controls). 0.8.0 fixes the one thing that used to go wrong: Wonderland no longer takes a station away from the player using it, so hand-feeding a smelter that is also being auto-fed just works — no more *"the smelter ate my silver"* — and the stations you leave behind through a portal keep working while you're away.

<a id="background-sort"></a>
### 🗂️ Background Sort
A slow, low-frequency pass quietly merges partial stacks of the same item across a base's containers. Consolidation only — a stack that's already whole is never touched, and nothing gets relocated while you're actively looking at it.

<a id="container-rows"></a>
### 📐 Container Rows
Every player-built container gets more rows — doubled by default, so a wood chest goes from 2 rows to 4 and a reinforced chest from 4 to 8 — on completely vanilla clients, with nothing installed on their side. This includes **ship cargo holds**: Karve, VikingShip and VikingShip_Ashlands grow exactly like a chest (Raft has no cargo hold in vanilla, so there's nothing there to grow). The multiplier is configurable, and total rows are capped at 32. The mechanism is vanilla's own: a 1.0 client accepts item rows beyond a chest's prefab grid when it loads the chest from the server, resizes the grid to fit them, and the chest panel scrolls. All the server does is keep one stack parked in the last row so the client keeps drawing it — a pure position move that can never add, remove or resize a stack — and since vanilla already places materials bottom-first, that anchor mostly takes care of itself. Ship cargo holds retain their expanded capacity safely without mid-voyage sailing desyncs or water impact damage: row anchoring, vacuum pulls, and background sorts are automatically paused while a ship is steered or underway, and resume seamlessly when moored. The scanner only ever visits containers that can actually grow, so a newly-filled chest gets picked up quickly and consistently rather than waiting behind every world-spawned loot chest and dungeon prop on the map. The vacuum, production supply and sort all see and use the grown rows. Width and stack sizes are **not** part of this: a vanilla client refuses extra columns and clamps every stack to its own maximum on load, so those stay vanilla (see below). Tombstones, treasure and dungeon chests, cargo crates and every other world-spawned container keep their vanilla size; add any player-built prefab you want left alone to the exclusion list.

**Ships and carts included.** A hull's cargo lives on the ship's own network object, and whoever is nearest a floating hull is the one simulating it — so rather than write over that player's game, Wonderland borrows the hull the way a second player opens someone else's chest (vanilla's own request, which the player's game refuses while the cargo is open), parks the extra rows, and hands it straight back. The hull pauses for a tenth of a second; the rows are there the next time the cargo is opened. A ship under sail is left alone until it stops; never more than one request a minute per ship.

<a id="item-cache-overflow-guard"></a>
### 📦 Item Cache & Overflow Guard
Wonderland does **not** boost stack sizes: a vanilla client — the only kind that ever connects to a server-only mod — clamps every stack to its own vanilla maximum the moment it loads a chest, so a server-side boost is invisible in play and only puts the excess at risk. (Extra *rows* are different — see Container Rows above.) The guard is the safety net that keeps anything a client would clamp or refuse from ever being lost:
- **Never-Lost Overflow Guard**: Any items a chest cannot actually hold — a column past vanilla width, a row past the chest's grown height, a stack above vanilla max — are safely routed into nearby sibling chests, or into the persistent server-side `ItemCache` (`Wonderland.Cache.<WorldName>.dat`) if all nearby chests are full.
- **Automatic Drain**: As soon as new chests are placed or space opens up, cached items automatically flow right back into storage.

> Lost something to the cache? Stand where you want it back and use the `/comehere` emote (see Player Controls) — Wonderland drops the cached stacks at your feet.

<a id="player-controls"></a>
### 🎮 Player Controls
Players can operate parts of Wonderland from a completely vanilla client, on any platform, with **emotes** — from the emote wheel or typed in chat as the vanilla command. A stock client never sends a custom slash command to a server (it runs locally and prints "not a recognized command"), and plain chat is only delivered to *other* players, so someone alone on the server produces no chat traffic at all. Emotes are different: every one is written into the player's character data, which always reaches the server. Wonderland answers with a normal on-screen message.

| Do this | Standing near | Effect |
| :--- | :--- | :--- |
| `/nonono` | a kiln, smelter or fire | Auto-supply **off** for that station: Wonderland stops loading it, whatever is inside burns out, feeding it by hand still works. Survives restarts. |
| `/thumbsup` | the same station | Auto-supply back **on**. |
| `/comehere` | anywhere | Drops your cached items (see Item Cache) at your feet — the ones within 30 m, or all of them if none are nearby. |

The nearest station within `ControlRange` (5 m by default) is the target. Every emote and the range are configurable in section `13 - Player Controls`.

<a id="raids-night-spawns"></a>
### ⚔️ Raids & Night Spawns
Block configured high-tier raid events from ever triggering in configured biomes (defaults: Meadows, BlackForest) — genuinely server-authoritative, not a guess. A companion system destroys hostile night-spawn creatures matching a configured biome/tier list the instant the server registers them, so a Meadows base stays a Meadows base no matter how long it's been standing. Both a creature's name **and** its biome must be on their respective lists for a spawn to be blocked. Defaults cover Meadows/BlackForest (Draugr, Draugr Elite, Wraith, Abomination, Deathsquito, Blob, Blob Elite, Stone Golem), the full Mistlands Seeker family (Seeker, Seeker Brood, Seeker Brute, Seeker Queen), the full Ashlands Charred family (Charred Archer, Charred Archer Fader, Charred Mage, Charred Melee, Charred Melee Dyrnwyn, Charred Melee Fader, Charred Twitcher, Charred Twitcher Summoned), and the full Fenring family from Hildir's camps (Fenring, Fenring Cultist, and the Hildir variants) in their own biomes — every prefab name confirmed against a live game data dump, not guessed.

<a id="player-cap"></a>
### 👥 Player Cap
Raises or lowers vanilla's hardcoded 10-player connection limit. Connection admission is a server decision through and through, so this is one of the few levers here with zero ambiguity about where authority lives.

> ⚠️ **Crossplay note:** `-crossplay` routes every connection through PlayFab, whose own lobby registration is separately hardcoded to 10 players. Raising the cap past 10 helps Steam-direct joins only — PlayFab/Xbox joins past the 10th are still rejected by PlayFab itself, independent of this or any mod. A startup warning fires automatically if this applies to your server.

**Crossplay lobby guard.** A crossplay server only becomes joinable once PlayFab confirms its join code is unique, and vanilla's check has a blind spot: the join code is the same after every restart, the previous run's lobby is usually still on PlayFab's books, and the moment PlayFab forgets who owned it the check crashes on a null owner and the server sits registered-but-invisible — no join code, no server list, no IP join — until the next restart. Wonderland catches that reply and takes vanilla's own regenerate-the-join-code path instead, so the server comes up findable every time. Nothing else in the registration flow is touched; `CrossplayLobbyGuardEnabled` turns it off, and it is inert on a Steam-only server.

<a id="structure-upkeep"></a>
### 🏚️ Structure Upkeep
A standing correction pass resets player-built pieces back to full health, rather than trying to intercept the (client-owned) decay tick directly — a patch that could never reliably fire on a real dedicated server. Two passes: a fast one around every connected player (decay can only happen inside a client's active area, so that is the only ground that can have lost health since the last tick) and a slow background sweep of the whole map as a backstop. Starter boats granted by the server (`s_creator == 0L`) are also protected: when moored or docked, starter vessels receive periodic upkeep so wave chop and weather damage don't permanently wear them down. Each repair routes vanilla's native `RPC_Repair` directly to the owning client alongside `RPC_HealthChanged`: this authoritatively commits full health into the client's in-memory ZDO, ensuring the heal permanently holds upon taking subsequent hits instead of reverting to previous damaged health. World-generated ruins are left alone by default.

**Boats repair on their own schedule — at sea too.** Ships are out of the structure pass and on a loop of their own (`18 - Boat Upkeep`): every `BoatUpkeepInterval` a damaged Raft, Karve, Longship or Drakkar is put back to full health wherever it is — moored, under sail, or under a serpent — with no rudder or speed condition. A boat someone is aboard or beside is repaired through that player's own game (vanilla's repair message, no hammer, no effects); a boat nobody is near is written by the server. Set the interval short for boats that are hard to sink, long for boats that merely do not rot, or switch it off to keep vanilla wear on hulls while structures stay pristine.

<a id="starter-grant"></a>
### 🎁 Starter Grant
New characters get a one-time starter kit and a labeled boat, automatically, the first time they're seen in the world — configurable kit contents, hull type, boat placement search radius, and an automatic vanilla map pin discovery. The boat is sited in open water with proper clearance, oriented seaward, rather than placed on dry land. The record is a world global key per character (`wonderland_starter_<playerID>`), saved inside the world file itself, so it only ever happens once per world — across reconnects, restarts, and backup restores alike.

<a id="security-anti-cheat"></a>
### 🛡️ Security & Anti-Cheat

Wonderland enforces an authoritative, multi-layered server-side security suite designed to protect honest survival progression while remaining split honestly between what a dedicated server can enforce versus client-side blind spots:

- **Enforceable: Vanilla Client Mod Enforcement** — protects pure vanilla servers by ensuring connecting clients are genuine, unmodded vanilla Valheim installs. Employs dual-vector enforcement: (1) **Active Probing** queries connecting clients with known framework challenges (ServerSync, Jotunn, ValheimPlus, AzuAntiCheat); vanilla clients silently ignore them while modded clients respond into the honeypot and are kicked. (2) **Placement Cadence Guard** monitors incoming entity creation to detect automated client-side planting and bulk placement mods (e.g. `PlantEasily`); bursts exceeding the vanilla physical placement cooldown (0.4s) immediately purge the cheated batch and kick the client. Disconnected clients receive *"Failed to connect: Incompatible version"* or the notice: *"Vanilla enforcement enabled, connect fairly with vanilla only client"*. Authenticated server admins on `adminlist.txt` are completely exempted from mod checks based purely on the server's authoritative adminlist check.
- **Enforceable: Server-Side Forced Map Visibility (`ForcePlayerMapPosition`)** — forces all connected players to always be publicly visible on the map and minimap for all players, completely server-side. Vanilla clients display all player pins with character names at all times, overriding the client-side "Visible to other players" checkbox. Authenticated admins can be exempted (`ForcePlayerMapPositionAdminBypass`) for stealth spectating.
- **Enforceable: Container Integrity & Progression Sweep (`ItemSanityGuard`)** — a periodic **item-integrity sweep** checks every tracked container against real item definitions, vanilla stack ceilings, cheat item blacklists, and server progression tier ceilings (`MaxAllowedTier`, defaulting to `"Auto"` to dynamically follow world boss kills), flagging (or optionally removing with `ItemIntegritySweepCorrect`) prohibited items. Containers placed by authenticated server admins are completely exempt. Containers only: a player's own bag is never networked to the server, so there is nothing there to check.
- **Enforceable / Detect-only: Equipment & Magic Surveillance (`EquipmentGuard`)** — scans visual equipment hashes (`VisEquipment`) on player character ZDOs in real time. Flags developer cheat items (`SwordCheat`, `SledgeCheat`, `ClubCheat`, `ArmorIronChestCheater`, `HelmetCheater`), impossible quality levels (`quality > m_shared.m_maxQuality`, exempting gear whose recipe runs through Valheim 1.0's upgrader stations, which legitimately climb past the base cap), and gear exceeding the server's progression tier ceiling, magic staves and robes included. Supports optional auto-kick (`EquipmentGuardKick`). Authenticated server admins on `adminlist.txt` are **100% exempt** (`EquipmentGuardAdminBypass = true`) — admins can wield, wear, spawn, or test whatever they want, whenever they want without interference. Anti-spam tracking ensures equipped gear logs once upon equip and resets automatically when a world boss falls.
- **Enforceable: Automatic World Progression Ledger (`MaxAllowedTier = "Auto"`)** — dynamically ties server gear progression ceilings and anti-cheat kicking directly to Valheim's persistent world boss defeat registry (`defeated_<boss>`). See the [front-and-center Progression Ledger](#progression-ledger) above for the full unlock matrix from Black Forest through Ashlands/Deep North.
- **Detect-only: Movement & Vitals** — max HP above a configured ceiling, implausible current stamina, implausible movement speed, and a persistent gap between reported position and expected terrain height (likely flight/noclip) are all logged as signals for an admin to review, never auto-corrected. Authenticated server admins are completely exempt.
- **Dedicated Security Audit Log (`SecurityLogEnabled`, `SecurityLogFileName`)** — writes all security events to a dedicated `BepInEx/logs/wonderland_security.log` stamped with UTC timestamps, player name, Steam64 / account ID, stable PlayerID, world coordinates, and infraction details, completely isolated from general BepInEx noise.
- **Structural blind spots, named honestly** — max stamina, skill levels, save-file edits, and ESP-style rendering cheats never reach the server at all, on any Valheim build; no amount of server-side logic changes that. (Note: carry capacity was previously in this list, but was unlocked in 0.6.0 via Valheim 1.0's native World Modifier system). Combat hits are in the same bucket: a damage RPC goes to the victim's own client and the server only relays it, which is why there is no damage-plausibility check here.

<a id="discord-notify"></a>
### 📣 Discord Notify
Your community's Discord knows what the server is doing: online and offline, who joined and left, who died, the first time an **account the world has never seen** joins, the first fall of each of the five classic bosses — and, if you want it, a heartbeat with uptime and who's on. Every roster says what each player is on — `Alice (PC), Bob (Xbox), Cara (Switch 2)` — and so does the join, leave and welcome headline, because in a crossplay world that's half the story; `{platform}` is there for any other template that wants it. Each message is clean Discord markdown — a bold headline with a small grey line beneath it, the live roster where it fits — and every one of them is a template you can rewrite. Optional extras, all off by default: your own avatar, a role to ping on boss kills and newcomers, a heartbeat rhythm of its own. Nothing is ever pinged by accident — not even by a player who names their character `@everyone`. Paste one webhook URL and you're done; it lives on your server and is never synced to clients.

<a id="heartbeat"></a>
### 💓 Heartbeat
A single summary line — world name, uptime, and who's currently online and on what — logged periodically (every 15 minutes by default) so an admin tailing the server log can confirm Wonderland is alive at a glance, without turning on full verbose logging. The optional Discord heartbeat above shares this interval unless given its own (`DiscordHeartbeatIntervalMinutes`). Configurable in `1 - General` (`HeartbeatEnabled`, `HeartbeatIntervalMinutes`).

<a id="barrkbot-export"></a>
### 🤖 BarrkBOT Export
Run BarrkBOT, the community Discord bot that answers members' questions from what the server's mods write down? Wonderland feeds it: who's online, each player's platform, sessions, time on the server, deaths and when it first saw them, world progress, the day count, the rates in force and lifetime tallies of what the automation has fed, vacuumed, cached and granted — all **measured by the server itself**, never taken from a client's word. A clean shutdown tells the bot the server is offline instead of leaving it guessing, and nothing private goes out: account ids stay with the admin. Harmless without BarrkBOT — it's two small files beside your config, and it can be switched off.

<a id="valheim-10-native"></a>
### 🧬 Valheim 1.0 Native
Built against the Valheim 1.0.15 dedicated-server assemblies — the same binary a live Linux dedicated server runs — and every release is booted on that server. Every game API the mod touches was verified against the 1.0 decompile — including the sector-query API that 1.0 reshaped, which is detected by parameter shape at startup (the older 0.221.x five-argument form is still bridged by reflection, best-effort). The startup log says which path it picked.

---

<a id="configuration"></a>
## ⚙️ Configuration

Settings live in `BepInEx/config/wubarrk.wonderland.cfg`, split into numbered sections, each entry documented with its own description, unit, and range right in the file. **Edits are picked up live**: the file is polled every 5 seconds and applied automatically — no server restart needed for a config change to take effect.

<a id="server-synced-admin-controlled"></a>
### Server-Synced (Admin Controlled)
| Section | What it covers |
| :--- | :--- |
| `1 - General` | Whether the server locks synced config against client overrides (default on); verbose logging, heartbeat, the tick profiler (slow-frame lines with a per-subsystem breakdown, 5-minute summaries) and the per-frame sweep budget. |
| `2 - Vacuum & Auto-Harvest` | Enable, interval, batch size, radius, exclusion lists, pickup-effect toggle, and the All Items Float buoyancy toggle (`AllItemsFloatEnabled`) for ground items in water. |
| `3 - Production Supply` | Enable, interval, batch size, range, reserve floor, and the kiln wood-type filter for fuel/process-material auto-supply. |
| `4 - Sort` | Enable, interval, and batch size for background stack consolidation. |
| `5 - Container Rows` | Enable, row multiplier, sweep interval, batch size, and exclusion list for server-side container row growth on vanilla clients (chests and ship cargo alike). |
| `6 - Raids` | Enable, blocked biomes, and blocked raid-event name list. |
| `7 - Night Spawns` | Enable, blocked biomes (default includes Mistlands, AshLands, and Plains), and blocked creature prefab list (default includes the full Seeker, Charred, and Fenring families). |
| `8 - Player Cap` | Maximum concurrent connected players (default 10, vanilla's own limit); crossplay lobby guard toggle. |
| `9 - Structure Upkeep` | Enable, interval, per-player radius, background sectors-per-sweep, and player-built-only for the no-decay correction pass. |
| `18 - Boat Upkeep` | Enable and interval for the separate boat repair loop (works at sea; uses the structure pass's per-player radius). |
| `10 - Starter Grant` | Enable, kit contents, boat hull prefab, and boat placement search radius. |
| `12 - Security` | Vanilla client mod enforcement (auto-kick with notice, authoritative admin bypass), server-side forced map visibility (`ForcePlayerMapPosition`), automatic boss defeat progression ledger (`MaxAllowedTier = "Auto"`), item exemption & tier override list (`ProgressionItemExemptions`), equipment & magic surveillance (`EquipmentGuard`), vitals guard, position watch, container integrity sweep, and dedicated audit log file. |
| `13 - Player Controls` | Enable, target range, and which emotes switch a station's auto-supply off and on or recover cached items. |
| `15 - World Modifiers & Capacity` | Multiplier for player max carry weight (`CarryWeightMultiplier`, default `2.0` = 600 lbs base) and passive stamina regen (`StaminaRegenRateMultiplier`, default `3.0`). Both synced to vanilla clients in real time via Valheim 1.0's native world rate channel. |
| `16 - Status Effect Roster` | Master switch, one on/off toggle per curated vanilla status effect asset (run speed, swim stamina, stamina regen, boss powers), and the boat-steering requirement for Moder. |

<a id="local-to-your-game"></a>
### Local to Your Game
| Setting | Section | What it does |
| :--- | :--- | :--- |
| `VerboseLogging` | `1 - General` | Verbose diagnostic logging. Security findings always log regardless of this setting. |
| `HeartbeatEnabled` / `HeartbeatIntervalMinutes` | `1 - General` | The periodic "still alive" server-log summary (see Heartbeat above). |
| `SweepBudgetMs` | `1 - General` | How much of any one server frame each world-wide sweep (vacuum round-robin, container rows, sort, production supply) may use before it yields to the next frame (default 2 ms). Coverage is unchanged — each sweep still walks its batch per interval — the work is just spread across frames instead of landing in one, so a large world never stalls the server for a chest type. Local because it is about this machine's frame time. |
| `BarrkBotExportEnabled` / `BarrkBotWriteSeconds` | `17 - BarrkBOT Export` | Whether the BarrkBOT export is written, and how often. Local because it describes this server's own files. |
| *(all of section 14)* | `14 - Discord Notify` | The webhook, which events to announce, the display name, the optional avatar / mention / heartbeat rhythm, and every message template. Local because a webhook URL is a per-server secret. |

**Upgrading from an older release?** Settings carry over automatically on first launch wherever the concept still exists. Anything tied to a removed feature is logged once as a summary and dropped. What changed between releases lives in the changelog, not here.

---

<a id="dependencies"></a>
## 📦 Dependencies

> ⚠️ **Requires:** BepInEx (the Valheim 1.0 pack, 5.4.2350 or newer). Nothing on the client, ever.

| Dependency | Why |
| :--- | :--- |
| **BepInExPack Valheim** (denikson) | The mod loader (also provides HarmonyX). |
| **JsonDotNET** (ValheimModding) | Declared so mod managers install it; the game already ships its own copy, which is enough. |

<a id="installation"></a>
## 📥 Installation

Wonderland is a **server-side-only** mod — install it once, on the server, and every connected player benefits with nothing to download.
Just install Wonderland on the server — the dependencies above are pulled in for you.

**Manual install:**
1. Install **BepInExPack Valheim** on the server.
2. Drop `Wonderland.dll` into the server's `BepInEx/plugins` folder.
3. Restart the server. That's it — no client install, no client config, nothing for players to do.

<div align="center">

*A Valheim mod by [Raven Iron](https://ravenirongames.com/).*

</div>
