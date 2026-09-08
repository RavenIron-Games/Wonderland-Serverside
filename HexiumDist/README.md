# Wonderland

**v2.0.0** — strictly server-side, no client install ever required.

Wonderland runs entirely on the dedicated server. Nobody connecting — Steam, Xbox, PlayFab/crossplay,
or a plain vanilla client — needs to install anything. Every feature below is enforced or automated
by the server itself.

## Install

Drop `Wonderland.dll` into the server's `BepInEx/plugins/` folder and restart it. That's it — no
client-side install, no client-side config, nothing for players to do.

## Features

- **Smart drop-to-chest** — containers and carts auto-vacuum matching ground items nearby. Match-required: a container only tops up an item type it already holds, it never introduces a new one.
- **Production auto-supply** — fireplaces, hearths, torches, smelters and kilns stay fed with fuel *and* ore/process material from linked containers, even with nobody online. Never drains a source container below a configurable reserve floor.
- **Auto-harvest** — when a player harvests something, nearby ripe pickables of the same type get swept in too.
- **Background sort** — partial stacks in containers quietly consolidate over time.
- **Real capacity growth** — both a stack-size multiplier and actual grid slot growth for containers and boats, with a built-in overflow guard so nobody's items get silently destroyed.
- **Raid & night-spawn control** — block configured high-tier raids and nighttime ambient spawns in configured biomes (defaults: Meadows, BlackForest).
- **Configurable player cap** — raise or lower vanilla's hardcoded 10-player limit. (Crossplay note below.)
- **No building decay** — structures are periodically kept at full health.
- **One-time starter grant** — new characters get a starter kit and a labeled boat, automatically, once.
- **Security & anti-cheat** — a real max-HP ceiling (clamped, not just logged), an item-fabrication sweep across every container and connected player, and detect-only flags for implausible speed, flight/noclip, and outsized hits.

## Known limitations (by design, not bugs)

- **Max stamina and max carry weight cannot be changed server-side.** Neither is ever written to a ZDO — there is no server-side lever for either, on any Valheim build. Max HP *is* real and configurable.
- **Crossplay servers have a second, separate 10-player cap.** `-crossplay` routes every connection through PlayFab, whose own lobby registration is hardcoded to 10 players — raising `MaxPlayerCount` past 10 helps Steam-direct joins only. A startup warning appears if this applies to your server.
- Runs against Valheim 0.221.12 and 0.221.13+ (auto-detected at startup) — see the plan/changelog for exactly what differs between them.

## Configuration

Every numeric threshold, radius, interval, and batch size is exposed and server-synced — see the
generated `BepInEx/config/wubarrk.wonderland.cfg` after first launch for the full list with
descriptions. Upgrading from the pre-2.0.0 version of this mod carries your old settings across
automatically where the concept still exists; anything tied to a removed feature is logged once and
dropped.
