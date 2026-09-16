<div align="center">

# 🌐 Wonderland

![Valheim Mod](https://img.shields.io/badge/Valheim-Serverside_Automation-orange.svg)
[![Multiplayer Compatible](https://img.shields.io/badge/Multiplayer-Server--Synced-blue.svg)]()
[![Framework](https://img.shields.io/badge/Requires-BepInEx-red.svg)]()
[![Crossplay](https://img.shields.io/badge/Crossplay-PlayFab%2FXbox_Ready-purple.svg)]()
[![Valheim 1.0](https://img.shields.io/badge/Valheim-1.0.12_Server-green.svg)]()

*No client install, ever. The server does the work. Built and live-tested on Valheim 1.0.*

</div>

Wonderland is a strictly server-side automation and world-governance mod: every feature below runs
entirely on the dedicated server, operating on the world's raw ZDO data instead of live game
objects — which is what makes it work on a real headless server at all, with nobody connecting
needing to install a thing. Steam, Xbox, PlayFab, and full crossplay parties all get the identical
experience.

---

<details>
<summary>📜 <b>Contents</b></summary>

- [🌱 Features](#-features)
  - [🧲 Vacuum & Auto-Harvest](#-vacuum--auto-harvest)
  - [🌊 All Items Float](#-all-items-float)
  - [⚖️ Carry Capacity](#️-carry-capacity)
  - [🏃 Stamina Regen Rate](#-stamina-regen-rate)
  - [🧪 Status Effect Roster](#-status-effect-roster)
  - [🔥 Production Supply](#-production-supply)
  - [🗂️ Background Sort](#️-background-sort)
  - [📐 Container Rows](#-container-rows)
  - [📦 Item Cache & Overflow Guard](#-item-cache--overflow-guard)
  - [🎮 Player Controls](#-player-controls)
  - [⚔️ Raids & Night Spawns](#️-raids--night-spawns)
  - [👥 Player Cap](#-player-cap)
  - [🏚️ Structure Upkeep](#️-structure-upkeep)
  - [🎁 Starter Grant](#-starter-grant)
  - [🛡️ Security & Anti-Cheat](#️-security--anti-cheat)
  - [📣 Discord Notify](#-discord-notify)
  - [💓 Heartbeat](#-heartbeat)
  - [🤖 BarrkBOT Export](#-barrkbot-export)
  - [🧬 Valheim 1.0 Native](#-valheim-10-native)
- [⚙️ Configuration](#️-configuration)
- [📦 Dependencies](#-dependencies)
- [📥 Installation](#-installation)

</details>

---

## 🌱 Features

### 🧲 Vacuum & Auto-Harvest
Containers and carts quietly pull in matching ground items within a configurable radius — **match-required**, so a chest only tops up an item type it already holds and never has a new one sprout inside it. Mining spoils, harvest drops, anything on the ground near a linked container just walks itself home — within a couple of seconds of landing near you. This includes **ship cargo** — a docked or beached Karve or VikingShip vacuums nearby matching items exactly like a chest does. A visual liquid splash and splash sound (vanilla's own fermenter effect `vfx_fermenter_add` and `sfx_fermenter_add`) play at the container on a successful pull via vanilla's native routed `SpawnObject` RPC, so it is clearly visible and audible to nearby players with nothing installed client-side; turn it off with `VacuumEffectEnabled` or customize the prefabs via `VacuumEffectPrefab` / `VacuumSoundPrefab`. Paired with this: **pick one and the rest follow** — press E on a single berry bush, mushroom or carrot and every ripe one of the same type within the radius is harvested with it, each drop landing exactly where its plant grew so a matching chest in range pulls the lot straight in — one keypress can put a whole carrot patch in the chest beside it. Ores, tar and dungeon loot are never swept. Exclusion lists (containers and items) keep this out of anything you want left alone.

### 🌊 All Items Float
All dropped items — ores, raw metals, scrap, tools, weapons, armor, trophies, and serpent scales — float on water instead of sinking to the ocean floor. Genuinely 100% server-side: vanilla clients see them bobbing and resting on the water surface, readily collected while swimming or sailing past, with zero client mods installed. Vanilla only attaches buoyancy components to wood, fish, and tombstones, while clients simulate physics on objects they own; Wonderland enhances server item prefabs, maintains surface elevation and server ownership on waterborne items, and immediately grants ownership when a player presses E or walks into auto-pickup range so pickup is instant. Configurable toggle (`AllItemsFloatEnabled`, default on), sweep interval, and surface elevation offset in `2 - Vacuum & Auto-Harvest`.

### ⚖️ Carry Capacity
Scale player max carry weight completely server-side via Valheim 1.0's native World Modifier system (`Game.m_carryWeightRate`). Stock vanilla clients receive the rate via the vanilla `GlobalKeys` network channel, display the increased limit directly in their inventory GUI (e.g. `0 / 600`), and enforce encumbrance and auto-pickup against that higher ceiling with no client mods. Configurable via `CarryWeightMultiplier` in `15 - World Modifiers & Capacity` (default `2.0` = 600 lbs base / 900 with Megingjord; `1.0` is vanilla). Live config changes broadcast to all connected players immediately without a server restart.

### 🏃 Stamina Regen Rate
The same native World Modifier mechanism as Carry Capacity above, targeting Valheim's own `Game.m_staminaRegenRate` instead. `StaminaRegenRateMultiplier` (default `3.0`) scales passive stamina regeneration for every connected player — completely server-side, arbitrary multiplier, zero client mods, live-updating the moment the config changes. Configurable in `15 - World Modifiers & Capacity`. Note: this stacks *multiplicatively*, not additively, with any Status Effect Roster entry below that also boosts stamina regen — see that setting's own in-file description for the exact math before combining both.

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

### 🔥 Production Supply
Fireplaces, hearths, torches, smelters and kilns keep themselves fed from the chests around them — **fuel and ore tracked separately**, so a smelter never idles on a full coal bin. It works whether or not anyone is online, never drains a chest below your **reserve floor**, only ever loads a charcoal kiln with the wood you allow, and any station can be switched off with an emote (see Player Controls). 0.8.0 fixes the one thing that used to go wrong: Wonderland no longer takes a station away from the player using it, so hand-feeding a smelter that is also being auto-fed just works — no more *"the smelter ate my silver"* — and the stations you leave behind through a portal keep working while you're away.

### 🗂️ Background Sort
A slow, low-frequency pass quietly merges partial stacks of the same item across a base's containers. Consolidation only — a stack that's already whole is never touched, and nothing gets relocated while you're actively looking at it.

### 📐 Container Rows
Every player-built container gets more rows — doubled by default, so a wood chest goes from 2 rows to 4 and a reinforced chest from 4 to 8 — on completely vanilla clients, with nothing installed on their side. This includes **ship cargo holds**: Karve, VikingShip and VikingShip_Ashlands grow exactly like a chest (Raft has no cargo hold in vanilla, so there's nothing there to grow). The multiplier is configurable, and total rows are capped at 32. The mechanism is vanilla's own: a 1.0 client accepts item rows beyond a chest's prefab grid when it loads the chest from the server, resizes the grid to fit them, and the chest panel scrolls. All the server does is keep one stack parked in the last row so the client keeps drawing it — a pure position move that can never add, remove or resize a stack — and since vanilla already places materials bottom-first, that anchor mostly takes care of itself. The scanner only ever visits containers that can actually grow, so a newly-filled chest gets picked up quickly and consistently rather than waiting behind every world-spawned loot chest and dungeon prop on the map. The vacuum, production supply and sort all see and use the grown rows. Width and stack sizes are **not** part of this: a vanilla client refuses extra columns and clamps every stack to its own maximum on load, so those stay vanilla (see below). Tombstones, treasure and dungeon chests, cargo crates and every other world-spawned container keep their vanilla size; add any player-built prefab you want left alone to the exclusion list.

### 📦 Item Cache & Overflow Guard
Wonderland does **not** boost stack sizes: a vanilla client — the only kind that ever connects to a server-only mod — clamps every stack to its own vanilla maximum the moment it loads a chest, so a server-side boost is invisible in play and only puts the excess at risk. (Extra *rows* are different — see Container Rows above.) The guard is the safety net that keeps anything a client would clamp or refuse from ever being lost:
- **Never-Lost Overflow Guard**: Any items a chest cannot actually hold — a column past vanilla width, a row past the chest's grown height, a stack above vanilla max — are safely routed into nearby sibling chests, or into the persistent server-side `ItemCache` (`Wonderland.Cache.<WorldName>.dat`) if all nearby chests are full.
- **Automatic Drain**: As soon as new chests are placed or space opens up, cached items automatically flow right back into storage.

> Lost something to the cache? Stand where you want it back and use the `/comehere` emote (see Player Controls) — Wonderland drops the cached stacks at your feet.

### 🎮 Player Controls
Players can operate parts of Wonderland from a completely vanilla client, on any platform, with **emotes** — from the emote wheel or typed in chat as the vanilla command. A stock client never sends a custom slash command to a server (it runs locally and prints "not a recognized command"), and plain chat is only delivered to *other* players, so someone alone on the server produces no chat traffic at all. Emotes are different: every one is written into the player's character data, which always reaches the server. Wonderland answers with a normal on-screen message.

| Do this | Standing near | Effect |
| :--- | :--- | :--- |
| `/nonono` | a kiln, smelter or fire | Auto-supply **off** for that station: Wonderland stops loading it, whatever is inside burns out, feeding it by hand still works. Survives restarts. |
| `/thumbsup` | the same station | Auto-supply back **on**. |
| `/comehere` | anywhere | Drops your cached items (see Item Cache) at your feet — the ones within 30 m, or all of them if none are nearby. |

The nearest station within `ControlRange` (5 m by default) is the target. Every emote and the range are configurable in section `13 - Player Controls`.

### ⚔️ Raids & Night Spawns
Block configured high-tier raid events from ever triggering in configured biomes (defaults: Meadows, BlackForest) — genuinely server-authoritative, not a guess. A companion system destroys hostile night-spawn creatures matching a configured biome/tier list the instant the server registers them, so a Meadows base stays a Meadows base no matter how long it's been standing. Both a creature's name **and** its biome must be on their respective lists for a spawn to be blocked. Defaults cover Meadows/BlackForest (Draugr, Draugr Elite, Wraith, Abomination, Deathsquito, Blob, Blob Elite, Stone Golem), the full Mistlands Seeker family (Seeker, Seeker Brood, Seeker Brute, Seeker Queen), the full Ashlands Charred family (Charred Archer, Charred Archer Fader, Charred Mage, Charred Melee, Charred Melee Dyrnwyn, Charred Melee Fader, Charred Twitcher, Charred Twitcher Summoned), and the full Fenring family from Hildir's camps (Fenring, Fenring Cultist, and the Hildir variants) in their own biomes — every prefab name confirmed against a live game data dump, not guessed.

### 👥 Player Cap
Raises or lowers vanilla's hardcoded 10-player connection limit. Connection admission is a server decision through and through, so this is one of the few levers here with zero ambiguity about where authority lives.

> ⚠️ **Crossplay note:** `-crossplay` routes every connection through PlayFab, whose own lobby registration is separately hardcoded to 10 players. Raising the cap past 10 helps Steam-direct joins only — PlayFab/Xbox joins past the 10th are still rejected by PlayFab itself, independent of this or any mod. A startup warning fires automatically if this applies to your server.

### 🏚️ Structure Upkeep
A standing correction pass resets player-built pieces back to full health, rather than trying to intercept the (client-owned) decay tick directly — a patch that could never reliably fire on a real dedicated server. Two passes: a fast one around every connected player (decay can only happen inside a client's active area, so that is the only ground that can have lost health since the last tick) and a slow background sweep of the whole map as a backstop. Each repair also routes vanilla's own health-changed RPC to the client so the piece visibly updates at once instead of waiting for the zone to reload. World-generated ruins are left alone by default.

### 🎁 Starter Grant
New characters get a one-time starter kit and a labeled boat, automatically, the first time they're seen in the world — configurable kit contents, hull type, boat placement search radius, and an automatic vanilla map pin discovery. The boat is sited in open water with proper clearance, oriented seaward, rather than placed on dry land. The record is a world global key per character (`wonderland_starter_<playerID>`), saved inside the world file itself, so it only ever happens once per world — across reconnects, restarts, and backup restores alike.

### 🛡️ Security & Anti-Cheat
Split honestly into what the server can actually do something about:
- **Enforceable** — a periodic **item-integrity sweep** checks every tracked container against real item definitions and vanilla stack ceilings, flagging (or optionally removing) fabricated items. Containers only: a player's own bag is never networked to the server, so there is nothing there to check.
- **Detect-only** — max HP above a configured ceiling, implausible current stamina, implausible movement speed, and a persistent gap between reported position and expected terrain height (likely flight/noclip) are all logged as signals for an admin to review, never auto-corrected. Expect some false positives by design: a portal trip reads as impossible speed, and a tall build reads as flight — they're signals, not verdicts. Max HP in particular *cannot* be corrected from the server — the owning client rewrites it from food every second and discards stale server writes while moving — so it's reported honestly as a signal rather than promised as a clamp. Rubber-banding a player back based on a guess would be worse than the problem.
- **Structural blind spots, named honestly** — max stamina, skill levels, save-file edits, and ESP-style rendering cheats never reach the server at all, on any Valheim build; no amount of server-side logic changes that. (Note: carry capacity was previously in this list, but was unlocked in 0.6.0 via Valheim 1.0's native World Modifier system). Combat hits are in the same bucket: a damage RPC goes to the victim's own client and the server only relays it, which is why there is no damage-plausibility check here.

### 📣 Discord Notify
Your community's Discord knows what the server is doing: online and offline, who joined and left, who died, the first time an **account the world has never seen** joins, the first fall of each of the five classic bosses — and, if you want it, a heartbeat with uptime and who's on. Every roster says what each player is on — `Alice (PC), Bob (Xbox), Cara (Switch 2)` — and so does the join, leave and welcome headline, because in a crossplay world that's half the story; `{platform}` is there for any other template that wants it. Each message is clean Discord markdown — a bold headline with a small grey line beneath it, the live roster where it fits — and every one of them is a template you can rewrite. Optional extras, all off by default: your own avatar, a role to ping on boss kills and newcomers, a heartbeat rhythm of its own. Nothing is ever pinged by accident — not even by a player who names their character `@everyone`. Paste one webhook URL and you're done; it lives on your server and is never synced to clients.

### 💓 Heartbeat
A single summary line — world name, uptime, and who's currently online and on what — logged periodically (every 15 minutes by default) so an admin tailing the server log can confirm Wonderland is alive at a glance, without turning on full verbose logging. The optional Discord heartbeat above shares this interval unless given its own (`DiscordHeartbeatIntervalMinutes`). Configurable in `1 - General` (`HeartbeatEnabled`, `HeartbeatIntervalMinutes`).

### 🤖 BarrkBOT Export
Run BarrkBOT, the community Discord bot that answers members' questions from what the server's mods write down? Wonderland feeds it: who's online, each player's platform, sessions, time on the server, deaths and when it first saw them, world progress, the day count, the rates in force and lifetime tallies of what the automation has fed, vacuumed, cached and granted — all **measured by the server itself**, never taken from a client's word. A clean shutdown tells the bot the server is offline instead of leaving it guessing, and nothing private goes out: account ids stay with the admin. Harmless without BarrkBOT — it's two small files beside your config, and it can be switched off.

### 🧬 Valheim 1.0 Native
Built against the Valheim 1.0.12 dedicated-server assemblies — the same binary a live Linux dedicated server runs — and every release is booted on that server. Every game API the mod touches was verified against the 1.0 decompile — including the sector-query API that 1.0 reshaped, which is detected by parameter shape at startup (the older 0.221.x five-argument form is still bridged by reflection, best-effort). The startup log says which path it picked.

---

## ⚙️ Configuration

Settings live in `BepInEx/config/wubarrk.wonderland.cfg`, split into numbered sections, each entry documented with its own description, unit, and range right in the file. **Edits are picked up live**: the file is polled every 5 seconds and applied automatically — no server restart needed for a config change to take effect.

### Server-Synced (Admin Controlled)
| Section | What it covers |
| :--- | :--- |
| `1 - General` | Whether the server locks synced config against client overrides (default on). |
| `2 - Vacuum & Auto-Harvest` | Enable, interval, batch size, radius, exclusion lists, pickup-effect toggle, and the All Items Float buoyancy toggle (`AllItemsFloatEnabled`) for ground items in water. |
| `3 - Production Supply` | Enable, interval, batch size, range, reserve floor, and the kiln wood-type filter for fuel/process-material auto-supply. |
| `4 - Sort` | Enable, interval, and batch size for background stack consolidation. |
| `5 - Container Rows` | Enable, row multiplier, sweep interval, batch size, and exclusion list for server-side container row growth on vanilla clients (chests and ship cargo alike). |
| `6 - Raids` | Enable, blocked biomes, and blocked raid-event name list. |
| `7 - Night Spawns` | Enable, blocked biomes (default includes Mistlands, AshLands, and Plains), and blocked creature prefab list (default includes the full Seeker, Charred, and Fenring families). |
| `8 - Player Cap` | Maximum concurrent connected players (default 10, vanilla's own limit). |
| `9 - Structure Upkeep` | Enable, interval, per-player radius, background sectors-per-sweep, and player-built-only for the no-decay correction pass. |
| `10 - Starter Grant` | Enable, kit contents, boat hull prefab, and boat placement search radius. |
| `12 - Security` | Vitals guard (HP ceiling and stamina plausibility, detect-only), position watch (speed/fly detection), and the container item-integrity sweep. |
| `13 - Player Controls` | Enable, target range, and which emotes switch a station's auto-supply off and on or recover cached items. |
| `15 - World Modifiers & Capacity` | Multiplier for player max carry weight (`CarryWeightMultiplier`, default `2.0` = 600 lbs base) and passive stamina regen (`StaminaRegenRateMultiplier`, default `3.0`). Both synced to vanilla clients in real time via Valheim 1.0's native world rate channel. |
| `16 - Status Effect Roster` | Master switch, one on/off toggle per curated vanilla status effect asset (run speed, swim stamina, stamina regen, boss powers), and the boat-steering requirement for Moder. |

### Local to Your Game
| Setting | Section | What it does |
| :--- | :--- | :--- |
| `VerboseLogging` | `1 - General` | Verbose diagnostic logging. Security findings always log regardless of this setting. |
| `HeartbeatEnabled` / `HeartbeatIntervalMinutes` | `1 - General` | The periodic "still alive" server-log summary (see Heartbeat above). |
| `BarrkBotExportEnabled` / `BarrkBotWriteSeconds` | `17 - BarrkBOT Export` | Whether the BarrkBOT export is written, and how often. Local because it describes this server's own files. |
| *(all of section 14)* | `14 - Discord Notify` | The webhook, which events to announce, the display name, the optional avatar / mention / heartbeat rhythm, and every message template. Local because a webhook URL is a per-server secret. |

**Upgrading from an older release?** Settings carry over automatically on first launch wherever the concept still exists. Anything tied to a removed feature is logged once as a summary and dropped. What changed between releases lives in the changelog, not here.

---

## 📦 Dependencies

> ⚠️ **Requires:** BepInEx (the Valheim 1.0 pack, 5.4.2350 or newer). Nothing on the client, ever.

| Dependency | Why |
| :--- | :--- |
| **BepInExPack Valheim** (denikson) | The mod loader (also provides HarmonyX). |
| **JsonDotNET** (ValheimModding) | Declared so mod managers install it; the game already ships its own copy, which is enough. |

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
