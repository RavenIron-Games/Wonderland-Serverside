<div align="center">

# 🌐 Wonderland

![Valheim Mod](https://img.shields.io/badge/Valheim-Serverside_Automation-orange.svg)
[![Multiplayer Compatible](https://img.shields.io/badge/Multiplayer-Server--Synced-blue.svg)]()
[![Framework](https://img.shields.io/badge/Requires-BepInEx-red.svg)]()
[![Crossplay](https://img.shields.io/badge/Crossplay-PlayFab%2FXbox_Ready-purple.svg)]()
[![Valheim 1.0](https://img.shields.io/badge/Valheim-1.0.7_Server-green.svg)]()

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
  - [🔥 Production Supply](#-production-supply)
  - [🗂️ Background Sort](#️-background-sort)
  - [📐 Container Rows](#-container-rows)
  - [📦 Item Cache & Overflow Guard](#-item-cache--overflow-guard)
  - [⚔️ Raids & Night Spawns](#️-raids--night-spawns)
  - [👥 Player Cap](#-player-cap)
  - [🏚️ Structure Upkeep](#️-structure-upkeep)
  - [🎁 Starter Grant](#-starter-grant)
  - [🛡️ Security & Anti-Cheat](#️-security--anti-cheat)
  - [🧬 Valheim 1.0 Native](#-valheim-10-native)
- [⚙️ Configuration](#️-configuration)
- [📦 Dependencies](#-dependencies)
- [📥 Installation](#-installation)

</details>

---

## 🌱 Features

### 🧲 Vacuum & Auto-Harvest
Containers and carts quietly pull in matching ground items within a configurable radius — **match-required**, so a chest only tops up an item type it already holds and never has a new one sprout inside it. Mining spoils, harvest drops, anything on the ground near a linked container just walks itself home. Paired with this: when a player harvests something, nearby ripe pickables of the same type get swept in too, so one swing of the axe can clear a whole stand of trees or a patch of berries instead of just the one you touched. Exclusion lists (containers and items) keep this out of anything you want left alone.

### 🔥 Production Supply
Fireplaces, hearths, torches, smelters, and kilns stay fed from linked containers within range — **fuel and process material both**, tracked separately, so a smelter never runs dry of ore just because its coal bin is full. Runs independent of anyone being online: a base doesn't go dark and a smelter doesn't go idle because its owner logged off. A configurable **reserve floor** means a source container is never drained below a minimum stock, so automation can't strip a stockpile out from under you.

### 🗂️ Background Sort
A slow, low-frequency pass quietly merges partial stacks of the same item across a base's containers. Consolidation only — a stack that's already whole is never touched, and nothing gets relocated while you're actively looking at it.

### 📐 Container Rows
Every player-built container gets more rows — doubled by default, so a wood chest goes from 2 rows to 4 and a reinforced chest from 4 to 8 — on completely vanilla clients, with nothing installed on their side. The multiplier is configurable, and total rows are capped at 32. The mechanism is vanilla's own: a 1.0 client accepts item rows beyond a chest's prefab grid when it loads the chest from the server, resizes the grid to fit them, and the chest panel scrolls. All the server does is keep one stack parked in the last row so the client keeps drawing it — a pure position move that can never add, remove or resize a stack — and since vanilla already places materials bottom-first, that anchor mostly takes care of itself. The vacuum, production supply and sort all see and use the grown rows. Width and stack sizes are **not** part of this: a vanilla client refuses extra columns and clamps every stack to its own maximum on load, so those stay vanilla (see below). Tombstones, treasure and dungeon chests, cargo crates and every other world-spawned container keep their vanilla size; add any player-built prefab you want left alone to the exclusion list.

### 📦 Item Cache & Overflow Guard
Wonderland does **not** boost stack sizes: a vanilla client — the only kind that ever connects to a server-only mod — clamps every stack to its own vanilla maximum the moment it loads a chest, so a server-side boost is invisible in play and only puts the excess at risk. (Extra *rows* are different — see Container Rows above.) The guard is the safety net that keeps anything a client would clamp or refuse from ever being lost:
- **Never-Lost Overflow Guard**: Any items a chest cannot actually hold — a column past vanilla width, a row past the chest's grown height, a stack above vanilla max — are safely routed into nearby sibling chests, or into the persistent server-side `ItemCache` (`Wonderland.Cache.<WorldName>.dat`) if all nearby chests are full.
- **Automatic Drain**: As soon as new chests are placed or space opens up, cached items automatically flow right back into storage.

> ⚠️ The `/cache` and `/cache claim` chat commands do not currently work on a dedicated server — chat is relayed as per-recipient targeted packets, so the server never sees the text. Automatic draining is unaffected.

### ⚔️ Raids & Night Spawns
Block configured high-tier raid events from ever triggering in configured biomes (defaults: Meadows, BlackForest) — genuinely server-authoritative, not a guess. A companion system destroys hostile night-spawn creatures matching a configured biome/tier list the instant the server registers them, so a Meadows base stays a Meadows base no matter how long it's been standing.

### 👥 Player Cap
Raises or lowers vanilla's hardcoded 10-player connection limit. Connection admission is a server decision through and through, so this is one of the few levers here with zero ambiguity about where authority lives.

> ⚠️ **Crossplay note:** `-crossplay` routes every connection through PlayFab, whose own lobby registration is separately hardcoded to 10 players. Raising the cap past 10 helps Steam-direct joins only — PlayFab/Xbox joins past the 10th are still rejected by PlayFab itself, independent of this or any mod. A startup warning fires automatically if this applies to your server.

### 🏚️ Structure Upkeep
A periodic correction pass resets tracked building pieces back to full health, rather than trying to intercept the (client-owned) decay tick directly. The effect is the same as disabling decay — the mechanism is just a standing correction instead of a patch that could never reliably fire on a real dedicated server.

### 🎁 Starter Grant
New characters get a one-time starter kit and a labeled boat, automatically, the first time they're seen in the world — configurable kit contents, hull type, boat placement search radius, and an automatic vanilla map pin discovery. The boat is sited in open water with proper clearance, oriented seaward, rather than placed on dry land. The record is a world global key per character (`wonderland_starter_<playerID>`), saved inside the world file itself, so it only ever happens once per world — across reconnects, restarts, and backup restores alike.

### 🛡️ Security & Anti-Cheat
Split honestly into what the server can actually do something about:
- **Enforceable** — a periodic **item-integrity sweep** checks every tracked container against real item definitions and vanilla stack ceilings, flagging (or optionally removing) fabricated items. Containers only: a player's own bag is never networked to the server, so there is nothing there to check.
- **Detect-only** — max HP above a configured ceiling, implausible current stamina, implausible movement speed, and a persistent gap between reported position and expected terrain height (likely flight/noclip) are all logged as signals for an admin to review, never auto-corrected. Expect some false positives by design: a portal trip reads as impossible speed, and a tall build reads as flight — they're signals, not verdicts. Max HP in particular *cannot* be corrected from the server — the owning client rewrites it from food every second and discards stale server writes while moving — so it's reported honestly as a signal rather than promised as a clamp. Rubber-banding a player back based on a guess would be worse than the problem.
- **Structural blind spots, named honestly** — max stamina, carry weight, skill levels, save-file edits, and ESP-style rendering cheats never reach the server at all, on any Valheim build; no amount of server-side logic changes that. Combat hits are in the same bucket: a damage RPC goes to the victim's own client and the server only relays it, which is why there is no damage-plausibility check here.

### 🧬 Valheim 1.0 Native
Built against the Valheim 1.0.7 dedicated-server assemblies and loaded on a real 1.0.7 Linux dedicated server before release. Every game API the mod touches was verified against the 1.0 decompile — including the sector-query API that 1.0 reshaped, which is detected by parameter shape at startup (the older 0.221.x five-argument form is still bridged by reflection, best-effort). The startup log says which path it picked.

---

## ⚙️ Configuration

Settings live in `BepInEx/config/wubarrk.wonderland.cfg`, split into numbered sections, each entry documented with its own description, unit, and range right in the file.

### Server-Synced (Admin Controlled)
| Section | What it covers |
| :--- | :--- |
| `1 - General` | Whether the server locks synced config against client overrides (default on). |
| `2 - Vacuum & Auto-Harvest` | Enable, interval, batch size, radius, and exclusion lists for the vacuum/auto-harvest sweep. |
| `3 - Production Supply` | Enable, interval, batch size, range, and reserve floor for fuel/process-material auto-supply. |
| `4 - Sort` | Enable, interval, and batch size for background stack consolidation. |
| `5 - Container Rows` | Enable, row multiplier, sweep interval, batch size, and exclusion list for server-side chest row growth on vanilla clients. |
| `6 - Raids` | Enable, blocked biomes, and blocked raid-event name list. |
| `7 - Night Spawns` | Enable, blocked biomes, and blocked creature prefab list. |
| `8 - Player Cap` | Maximum concurrent connected players (default 10, vanilla's own limit). |
| `9 - Structure Upkeep` | Enable, interval, and batch size for the no-decay correction pass. |
| `10 - Starter Grant` | Enable, kit contents, boat hull prefab, and boat placement search radius. |
| `12 - Security` | Vitals guard (HP ceiling and stamina plausibility, detect-only), position watch (speed/fly detection), and the container item-integrity sweep. |

### Local to Your Game
| Setting | Section | What it does |
| :--- | :--- | :--- |
| `VerboseLogging` | `1 - General` | Verbose diagnostic logging. Security findings always log regardless of this setting. |

**Upgrading from an older release?** Settings carry over automatically on first launch wherever the concept still exists. Anything tied to a removed feature is logged once as a summary and dropped. What changed between releases lives in the changelog, not here.

---

## 📦 Dependencies

> ⚠️ **Requires:** BepInEx (the Valheim 1.0 pack, 5.4.2350 or newer) — and only BepInEx.

| Dependency | Why |
| :--- | :--- |
| **BepInExPack Valheim** (denikson) | The mod loader (also provides HarmonyX). |

## 📥 Installation

Wonderland is a **server-side-only** mod — install it once, on the server, and every connected player benefits with nothing to download.

**With Gale (recommended):** just install Wonderland on the server — the dependency above is pulled in for you.

**Manual install:**
1. Install **BepInExPack Valheim** on the server.
2. Drop `Wonderland.dll` into the server's `BepInEx/plugins` folder.
3. Restart the server. That's it — no client install, no client config, nothing for players to do.

<div align="center">

*A Valheim mod by [Raven Iron](https://ravenirongames.com/).*

</div>
