# Wonderland

**A Valheim mod by [Raven Iron](https://ravenirongames.com/).**

> *No client install, ever. Wonderland runs entirely on the dedicated server — every feature below is enforced or automated by the server itself, so Steam, Xbox, PlayFab, and crossplay players all get the full experience without installing anything.*

---

## What is Wonderland?

**Wonderland** is a strictly server-side automation and world-governance mod. It operates purely on ZDO data rather than live game objects, which is what makes it work on a real headless dedicated server at all — no client mod, no client config, nothing for a player to do.

- **Smart drop-to-chest**: containers and carts auto-vacuum matching ground items nearby. Match-required — a container only tops up an item type it already holds, it never introduces a new one.
- **Production auto-supply**: fireplaces, hearths, torches, smelters, and kilns stay fed with fuel *and* ore/process material from linked containers, even with nobody online. Never drains a source below a configurable reserve floor.
- **Auto-harvest**: when a player harvests something, nearby ripe pickables of the same type get swept in too.
- **Background sort**: partial stacks in containers quietly consolidate over time.
- **Real capacity growth**: a stack-size multiplier and actual grid slot growth for containers and boats, with a built-in overflow guard so nobody's items get silently destroyed.
- **Raid & night-spawn control**: block configured high-tier raids and nighttime ambient spawns in configured biomes (defaults: Meadows, BlackForest).
- **Configurable player cap**: raise or lower vanilla's hardcoded 10-player limit.
- **No building decay**: structures are periodically kept at full health.
- **One-time starter grant**: new characters get a starter kit and a labeled boat, automatically, once.
- **Security & anti-cheat**: a real max-HP ceiling (clamped, not just logged), an item-fabrication integrity sweep across every container and connected player, and detect-only flags for implausible speed, flight/noclip, and outsized hits.

---

## ServerSync & Multiplayer

Built from the ground up for dedicated servers and crossplay worlds:
- **Server-Authoritative**: everything above is decided and enforced by the server alone — there is no client-side logic to bypass, because there is no client mod at all.
- **ServerSync Powered**: every numeric threshold, radius, interval, and batch size is exposed and locked from the dedicated server to any connecting client.
- **Dual-build support**: auto-detects Valheim 0.221.12 vs 0.221.13+ at startup and bridges the one API shape that differs between them, so one build of the mod covers both.

## Known limitations (by design, not bugs)

- **Max stamina and max carry weight cannot be changed server-side.** Neither is ever written to a ZDO — there is no server-side lever for either, on any Valheim build. Max HP *is* real and configurable.
- **Crossplay servers have a second, separate 10-player cap.** `-crossplay` routes every connection through PlayFab, whose own lobby registration is hardcoded to 10 players — raising the player cap past 10 helps Steam-direct joins only. A startup warning appears if this applies to your server.

---

## Configuration

Every setting lives in `BepInEx/config/wubarrk.wonderland.cfg`, generated on first launch with a full description for each entry:

`1 - General`, `2 - Vacuum & Auto-Harvest`, `3 - Production Supply`, `4 - Sort`, `5 - Storage Capacity`, `6 - Raids`, `7 - Night Spawns`, `8 - Player Cap`, `9 - Structure Upkeep`, `10 - Starter Grant`, `11 - Vitality`, `12 - Security`.

**Upgrading from a pre-0.1.0 install?** Your old settings carry over automatically on first launch wherever the concept still exists (stack multiplier, vacuum enable/radius/interval, auto-fuel→production-supply, raid-block); anything tied to a removed feature is logged once and dropped.

---

## Installation

### With a Mod Manager (Recommended)
1. Install via **Hexium** or your preferred Thunderstore-compatible mod manager, on the **server only**.
2. Ensure **BepInExPack Valheim** is installed on the server.

### Manual Installation
1. Extract the package zip.
2. Copy `plugins/Wonderland.dll` into the **server's** `BepInEx/plugins/` directory.
3. Restart the server. Nothing is installed on any client, ever.

---

## Links & Community

- **Official Website**: [https://ravenirongames.com/](https://ravenirongames.com/)
- **Developer**: Raven Iron
