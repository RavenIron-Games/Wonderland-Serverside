# Wonderland 0.8.0 — handoff (2026-09-15 14:57, updated 2026-09-15 evening: both open reviews closed, fixes applied)

Everything below is **uncommitted** in the working tree on `master` (20 modified, 5 new paths; `git status` at the
end). `~/.dotnet/dotnet build -c Release` is clean: 0 warnings, 0 errors. `HexiumDist/plugins/Wonderland.dll`
(229,376 bytes, 20:12) is the build of exactly this tree. Nothing has been deployed to the live server.

## 1. What is in 0.8.0

| Area | Change | Files |
|---|---|---|
| Build | csproj HintPaths were stale after the folder move (`..\libs-Tools` → `..\..\libs-Tools`); Newtonsoft.Json referenced `Private=False`; manifest declares `ValheimModding-JsonDotNET-13.0.4` | `Wonderland.csproj`, `HexiumDist/manifest.json` |
| Discord fix: "everyone's first time" | First join is now per **account** from vanilla's persisted `ZNet.World.m_playerHistory`, decided in an `RPC_PeerInfo` **prefix** (vanilla appends the account inside the same handshake) and handed to the postfix via `__state`. Old `wonderland_discord_seen_*` keys removed once on load. | `Subsystems/DiscordNotify/PeerJoinLeaveHook.cs`, `PlayerLifecycleWatch.cs` (first-join removed, deaths unchanged), `DiscordNotifySubsystem.cs` |
| Discord fix: Eikthyr re-announced every boot | Boss snapshot + "server online" moved to a `ZNet.ServerLoadWorld` postfix (`WorldLoadedHook.cs`, new); boss watch armed only after it; online post gated on `ZoneSystem.GenerateLocationsCompleted` | `WorldLoadedHook.cs`, `BossDefeatWatch.cs` |
| Discord "doesn't heartbeat" | Not a bug: live cfg has `DiscordNotifyHeartbeat = false` (default). Boot now logs one `[DiscordNotify] webhook configured: … \| heartbeat: off (DiscordNotifyHeartbeat) …` line | `DiscordNotifySubsystem.cs` |
| Discord restyle (Option A, approved) | All 8 templates are two-line markdown (`-#` subtext); every template accepts `{player} {boss} {world} {uptime} {playercount} {players} {time} {version} {mention}`; single-pass substitution; player-supplied text gets `@`+U+200B so it can never ping | `DiscordNotifySubsystem.cs` (`Fill`, `OnlineRoster`), `Core/WonderlandConfig.cs` |
| Discord extras (all default off, single webhook) | `DiscordAvatarUrl` (validated http/https, else omitted + one warning), `DiscordMention` (+ strict `allowed_mentions` whitelist, `[0-9]` snowflakes only), `DiscordHeartbeatIntervalMinutes` (0 = shared; range 0–1440; <1 treated as 1) | `DiscordWebhook.cs`, `Core/Heartbeat.cs` (two timers) |
| Config migration | `MigrateDiscordTemplates`: a template still equal to its pre-0.8.0 default is moved to the new default, once, gated by the new `DiscordTemplateStyle` marker (0 → 1). Verified byte-for-byte that all 8 live values match, so the live box migrates on first boot. New keys are appended by Bind. | `Core/WonderlandConfig.cs` |
| Hot-reload | Every section-14/17 setting is read at use time; BepInEx `Reload → SetSerializedValue → Unescape` verified from the decompiled BepInEx.dll (`\n` round-trips) | — |
| Smelter "ate my silver" | Root cause confirmed by two independent traces: the engine did `SetOwner(server)` on every feed; a hand-fed `RPC_AddOre` then routed to the server, which has no instance, and was dropped after the ore left the inventory. Engine no longer claims stations: **OwnerRpc** to the owner when its own tick stamp (`s_startTime` / `s_lastTime`, written only under `IsOwner`) is < 4 s old; **Direct** write when unowned / server / dead session / owner too far to hold an instance; **Skip** one sweep after any `OwnerRevision` change and while a nearby owner is not yet ticking. Fuel only topped up when a whole unit fits. Verbose `LogFeed` line per feed. | `Subsystems/ItemFlow/ProductionSupplyEngine.cs` |
| BarrkBOT export | `BepInEx/config/Wonderland/barrkbot_wonderland.json` every 60 s (`17 - BarrkBOT Export`): live `server` block, `players` keyed by stable `s_playerID`, `lifetime` counters; registry persisted as `BepInEx/config/Wonderland.BarrkBot.<world>.dat`; temp-then-rename with Delete+Move fallback. Read back through BarrkBOT 6.0.119's real reader at 0/3/12 players — all under the 6,000-char cap, no warning notes. | `Subsystems/BarrkBot/*.cs` (new), hook lines in `ItemLedger.cs`, `AuditLog.cs`, `RaidGovernor.cs`, `SpawnGovernor.cs`, `PeerJoinLeaveHook.cs`, `PlayerLifecycleWatch.cs`, `BossDefeatWatch.cs`, `Heartbeat.cs`, `Plugin.cs`; `BARRKBOT_CONTRACT.md` (new); `tools/barrkbot/` (probe + samples) |
| Docs | `HexiumDist/CHANGELOG.md` 0.8.0 entry, `HexiumDist/README.md` (Discord, BarrkBOT sections + config tables) | |

Not done, by design: **blast furnace accepting all ore** — impossible from a server-only mod (the client's own
`Smelter.OnAddOre` rejects before any network traffic; a queued-but-unconvertible ore is *eaten* by the client's
`Spawn`). AwayFromHome 1.0.2 (client+server, same author) already ships it.

## 2. Verification status

| Review | Result |
|---|---|
| Discord fixes (first-join, boss, online) — 1 fix agent + 1 adversarial | ship; all minor findings applied |
| Discord restyle/extras/migration/hot-reload — 2 lenses | 1 major (mention re-substitution) + minors, **all applied** |
| Smelter root-cause audit + adversarial check | rank-1 mechanism confirmed by both |
| Smelter fix — 2 lenses | 2 majors (RPC dropped while owner loading; portal-away owner skipped forever) + minors, all applied (liveness stamp, distance rule, settle sweep, honest logging) |
| Smelter fix re-review (evening) — reviewer + adversarial verifier | 1 major confirmed (Direct write to a **connected far owner** reverted by that client's retained stale copy on return), 1 downgraded to minor (fresh stamp trusted before the position check: RPC dropped for ~1 tick after a portal jump), settle transition and cadence minors, notes. **All applied** — see §9. |
| BarrkBOT understand phase — 3 readers | done; shape rules folded in (schema_version 3, `_alltime`, `tracking_since`, no block-level `generated_at`, ids as strings, notes trimmed) |
| BarrkBOT export (evening) — reviewer + adversarial verifier | 3 majors (shutdown leaves `online:true` for 60 min; auto-harvest never counted; every death counted as a session), 5 minors, notes. **All applied** — see §9. Newtonsoft verified against the live box. |
| Fixes applied in §9 — 6 agents (2 lenses + adversarial verifier per track) | No major survived: 3 raised, 2 downgraded to minor, 1 refuted (already fixed). Minors applied (per-peer instance test, timed settle, respawn gap, shutdown attribution without `Disconnect`, toggle edges, duplicate-id guard, online base); notes applied where cheap. Build clean 0/0; samples re-verified through the real reader. **The second round of fixes has not itself been re-reviewed.** |

Nothing has run on a real server. The live box is `/home/rohan/ArchShare/VanillaBean Server/` (read-only mirror
of `/home/wubarrk/WindowsShare/VanillaBean Server/`). **Game version:** the box logs `Valheim l-1.0.12 (network
version 40)`; the libs-Tools reference DLL and decompile carry the same md5 / `GameVersion(1, 0, 12)` as that box
(captured 2026-09-11) — every "1.0.7" label in this repo and in `libs-Tools/1.0/README.md` is stale naming, the
bytes are 1.0.12.

## 3. Deploying and what to look for on the first boot

1. Commit (suggested subject: `Fix Discord first-join/boss re-post, restyle messages, stop stations eating hand-fed ore, add BarrkBOT export (v0.8.0)`).
2. Copy `HexiumDist/plugins/Wonderland.dll` to the live box's `BepInEx/plugins/Wonderland-v0.8.0/plugins/` (whatever the usual Hexium layout is); the JsonDotNET package is optional at runtime — the game's own `Managed/Newtonsoft.Json.dll` (13.0.2, AssemblyVersion 13.0.0.0) satisfies the reference, same as TortalPortalLite already does on that box.
3. On the live cfg, set **`DiscordNotifyHeartbeat = true`** if the community wants the heartbeat (it was never a code fault). Optionally `DiscordHeartbeatIntervalMinutes = 60`.
4. First-boot log lines to expect (`BepInEx/LogOutput.log`):
   - `[Config] 8 Discord message template(s) were still on the pre-0.8.0 default and have been moved to the 0.8.0 style` — once, never again (`DiscordTemplateStyle = 1` written).
   - `[DiscordNotify] removed 7 stale 'wonderland_discord_seen_*' global key(s)` — once.
   - `[DiscordNotify] webhook configured: yes | server status: on | … | heartbeat: … | interval: … | avatar: webhook default | mention: none`.
   - **No** `[DiscordNotify] boss defeated: Eikthyr.` during `ZoneSystem.Load` (that line appeared on 4/4 boots before).
   - `[BarrkBot] exporting to …/BepInEx/config/Wonderland/barrkbot_wonderland.json every 60 s …` and the file appearing; `[BarrkBot] loaded N player row(s)` on later boots. On a clean stop the file's `server.online` flips to `false`.
   - No `Newtonsoft` resolve error (first Wonderland build that JITs `JsonConvert`).
5. First-session checks: a veteran logging in gets **no** "first time" post; a genuinely new account does; a player standing at an auto-fed smelter can hand-feed ore without loss (the 0.7.2 bug); verbose log shows `[ProductionSupply] … via rpc sent to owner '…'` while they stand there (after one scanner cycle — 20-45 s for smelters, 2-4 min for fireplaces — not 4 s), `via direct write` when nobody is near, and `via direct write, copy pushed to far owner '…'` after they portal away; `barrkbot_wonderland.json` gains a row for them within ~10 s, and dying does not add a session.

## 4. How to re-verify the BarrkBOT export shape

```bash
cd /home/rohan/WubarrkCODING/WindowsDEV/Discord-BarrkBOT
/usr/bin/node "/home/rohan/WubarrkCODING/SERVER SIDE ONLY/Wonderland/tools/barrkbot/barrkbot-render-probe.mjs" all
# or one scenario: populated | fresh | players12 — or a path to a real barrkbot_wonderland.json
```
Prints what the bot renders for the overview, a per-player lookup, rankings, the staleness and clean-shutdown
readings, with the serialized size vs the 6,000-char cap (last run 20:10: 5,720 / 4,148 / 5,654 chars, all fit; at 12
players only 1 detail row is shown, rankings still over all 12). The probe speaks the BarrkBOT 6.1.5 reader API
(server registry entry first on every call; `VALHEIM_SERVERS_FILE=''` makes the bot synthesise a `default` server
from `VALHEIM_SERVER_DIR`); `BARRKBOT_DIR=` points it at another checkout. The samples are generated to
`BarrkBotExport.Build()`'s exact key set and order; once the mod has run, feed the real file instead.
`BARRKBOT_CONTRACT.md` lists every field name the bot now depends on.

## 5. Open items (in priority order)

1. The §9 fixes were verified by 6 agents (no major survived); the small second round applied from that pass
   (per-peer instance test, timed settle, respawn gap, shutdown attribution, toggle edges) has not itself been
   re-reviewed — the first live session is the review (§3.5).
2. **Chest-side `SetOwner` in `Core/Data/ZdoInventoryIO.cs:54`** — same class of bug as the smelter one (server
   owns a chest a player is using → `RPC_RequestOpen` dropped → "press E twice"; a close within one sync cycle of
   a debit can duplicate). Deliberately left out of 0.8.0. Both smelter reviewers flagged it; the verifier notes
   its races are gated by `s_inUse` and need a sub-100 ms coincidence.
3. Overview rendering at ≥12 players shows 1 row (ranking is still over all rows). If members complain, split
   per-player rows into a second `barrkbot_*.json` under `BepInEx/config/Wonderland/` (v4 rollover +
   `players_leaders`; BarrkBOT's maintainer agreed v3 is fine for now).
4. BarrkBOT reader quirk: `players.*_alltime` described as resetting on restart (`cumulativeCollections` does not
   inspect id-keyed maps). Reported to the BarrkBOT session; fix belongs there.
5. BarrkBOT sweeps only `ValheimServer/` today; Vanilla Bean's export is picked up once 6.1 adds a second root.
   The Wonderland website (v1.5.0) already reads it directly.
6. `Wonderland.Cache.default.dat` (8 bytes) on the live box is a stray from an old boot where `ZNet.instance` was
   null at `ZNetScene.Awake`; harmless, can be deleted. The BarrkBot registry pins its path at load, so it cannot
   repeat this.
7. Log cosmetics (0.7.2 code, untouched): `[PlayerCapGovernor] patched RPC_PeerInfo…` prints 6× per boot, including
   during shutdown — Harmony re-running the transpiler whenever any plugin patches/unpatches that method.
8. libs-Tools docs: `1.0/README.md` says 1.0.7 for a DLL set that is 1.0.12 (see §2). A fact worth adding to
   `VALHEIM-DEDICATED-SERVER-FACTS.md`: `ZDO.Set` bumps `DataRevision` unconditionally; only `SetPosition` is
   owner-gated; `ZDOMan.RPC_ZDOData` accepts any higher DataRevision regardless of owner; `ForceSendZDO(peer, id)`
   delivers regardless of sector range.

## 6. Decompile facts the code relies on (1.0.7 server build, `libs-Tools/1.0/DECOMPILED/assembly_valheim_SERVER.decompiled.cs`)

- `ZNet.RPC_PeerInfo` sets `m_uid`/`m_playerName` then calls `SendPlayerList → UpdatePlayerList → UpdatePlayerHistory` (appends the account) — hence the prefix. `UpdatePlayerList` builds `PlatformUserID` per `m_onlineBackend`; `ResolvePlatformUserID` mirrors it.
- Boot order: `ZNet.Start → ServerLoadWorld → LoadWorld → ZoneSystem.Load` (re-adds every saved key via `GlobalKeyAdd`) `→ WorldSetup/SetStartingGlobalKeys → OnWorldSaveLoaded`; all after `ZNetScene.Awake`. `OnWorldSaveLoaded` is skipped on the missing-file path, hence the hook is on `ServerLoadWorld`.
- `ZoneSystem.GenerateLocationsCompleted`'s `add` accessor invokes immediately when locations already exist.
- Routed RPC to a ZDO with no local instance is dropped silently (`ZRoutedRpc.HandleRoutedRPC → ZNetScene.FindInstance == null`); the dedicated server's reference position is pinned at (1e6,0,1e6) so it never has station instances.
- `Smelter.GetDeltaTime` writes `s_startTime = ZNet.GetTime().Ticks` each tick under `IsOwner`; `Fireplace.GetTimeSinceLastUpdate` does the same with `s_lastTime`. `ZDOMan.ReleaseNearbyZDOS` only walks the owner's current near block, so ownership of a far station is never stripped.
- `RPC_AddOre(long, string, bool)` registered `Register<string,bool>`; `RPC_AddFuel()`; both bodies verified free of `Player.m_localPlayer`.
- BepInEx `TomlTypeConverter.Escape/Unescape` handle `\n`; `ConfigFile.Reload` re-parses every bound entry.

## 7. Session memory written (`~/.claude/projects/-home-rohan-WubarrkCODING-SERVER-SIDE-ONLY-Wonderland/memory/`)

live-server-share-read-only, wonderland-build-setup, agent-cap-and-approval-workflow, wonderland-config-constraints,
libs-tools-gotcha-sweep.

## 8. `git status` (evening)

```
 M Core/Data/ItemLedger.cs
 M Core/Heartbeat.cs
 M Core/WonderlandConfig.cs
 M HexiumDist/CHANGELOG.md
 M HexiumDist/README.md
 M HexiumDist/manifest.json
 M Plugin.cs
 M Subsystems/DiscordNotify/BossDefeatWatch.cs
 M Subsystems/DiscordNotify/DiscordNotifySubsystem.cs
 M Subsystems/DiscordNotify/DiscordWebhook.cs
 M Subsystems/DiscordNotify/PeerJoinLeaveHook.cs
 M Subsystems/DiscordNotify/PlayerLifecycleWatch.cs
 M Subsystems/ItemFlow/ProductionSupplyEngine.cs
 M Subsystems/ItemFlow/VacuumEngine.cs
 M Subsystems/Security/AuditLog.cs
 M Subsystems/Security/PositionWatch.cs
 M Subsystems/Security/VitalsGuard.cs
 M Subsystems/WorldGovernor/RaidGovernor.cs
 M Subsystems/WorldGovernor/SpawnGovernor.cs
 M Wonderland.csproj
?? BARRKBOT_CONTRACT.md
?? HANDOFF-0.8.0.md
?? Subsystems/BarrkBot/
?? Subsystems/DiscordNotify/WorldLoadedHook.cs
?? tools/barrkbot/
```
20 files changed, 960 insertions(+), 147 deletions(-) (+ untracked)

## 9. Evening session (2026-09-15, after the handoff): what changed

Both outstanding reviews were run (reviewer + adversarial verifier each) and every confirmed finding applied.
Build clean (0 warnings, 0 errors); still uncommitted, still not deployed.

**BarrkBOT export** (`Subsystems/BarrkBot/*`, `PeerJoinLeaveHook.cs`, `AuditLog.cs`, `VitalsGuard.cs`,
`PositionWatch.cs`, `VacuumEngine.cs`, samples, probe, contract, README, CHANGELOG):
- Sessions/connected time/online follow the **peer** (`ZNetPeer.m_uid`), not character presence — a death no
  longer counts as a session; connected time keeps accruing through the respawn gap.
- Clean shutdown writes one last export with `online:false`, `players_online` 0, every `online_now` false
  (`BarrkBotExport.Write(stopping: true)`). `OnPeerLeft` (ZNet.Disconnect prefix) covers every runtime
  disconnect; it does **not** fire at shutdown (`ZNet.StopAll` disposes peers without calling `Disconnect` —
  decompile 79484-79515), so `OnShutdown` credits the last partial sweep interval from the bindings instead.
- `vacuum_items_moved_*` now sums `VacuumEngine.VacuumTag` + `HarvestTag` (`"AutoHarvest"`, was `"Harvest"`).
- Registry path pinned at load; unreadable registry moved to `.corrupt-<timestamp>` (or run read-only if even
  that fails) instead of being overwritten; hooks gated on `BarrkBotExportEnabled`; off (at boot or at runtime)
  = remove the file, save, pause, keep the id→connection bindings; re-enable = one zero-time sweep then an
  immediate write (no phantom sessions, first file consistent). Two live connections presenting one profile id
  (copied character file) keep the first binding instead of flip-flopping. `players_online` and
  `players_online_names` now come from the same peer-based set as `online_now`/peak.
- Handshake facts keyed by peer uid (was character name) and cleared on disconnect; security flags attributed by
  stable id only (`AuditLog.Flag(..., playerId)`).
- Export shape: `server.name` added (`ZNet.m_ServerName` via reflection), `world_day` null when unmeasured,
  `intervals.write_seconds` is the clamped value; `platform_id` and per-player `security_flags_alltime`
  **removed from the export** (kept in the .dat); notes updated. Samples regenerated to `Build()`'s shape; probe
  fixed (real filenames, path arg, try/finally cleanup, shutdown scenario).

**Smelter fix** (`Subsystems/ItemFlow/ProductionSupplyEngine.cs`):
- OwnerRpc requires a fresh stamp **and** an instance on the owner's client: the owner's *own* validated
  simulation distance around their character ZDO position (no +1 margin, corner zones excluded unless classic —
  the same set `ZNetScene.CreateDestroyObjects` uses), and no character (death→respawn gap) means no instance.
  Fresh-but-no-instance → Skip.
- Direct write to a connected far owner is followed by `ZDOMan.ForceSendZDO(owner, id)` so the owner's retained
  copy is current before they return (the confirmed major; keeps unattended feeding).
- Settle is **timed** (`SettleSeconds` = 3 s of world time with the OwnerRevision unchanged), advanced every
  engine interval for the stations waiting on it, so a station whose ownership flaps at a zone border is fed on
  the first visit that finds it quiet; applies to the owner→0 transition when the ZDO was owned this uptime
  (`OwnerRevision > 0`); never-owned (`OwnerRevision == 0`) and server-owned feed immediately. A stamp must
  post-date the ownership record (paused-clock case). Per-family freshness (smelter 2.5 s, fireplace 4.5 s).
  Fireplace bookkeeping runs before the headroom test. `s_cheatedQueued` reset on Direct ore writes. Ownership
  records for demolished stations are dropped by a periodic existence check (the scanner never yields a destroyed
  ZDO); skip-log state cleared on every Skip/early branch. Class summary rewritten, including the honest residual
  windows (departure lag, fireplace cap race on a concurrent hand-feed, a second stale-copy holder that missed its
  release).

**Docs / package (20:45):** per Rohan's rule (README = features and the fancy pitch, CHANGELOG = the data) the README's
Production Supply / Discord / BarrkBOT sections were rewritten as feature pitches and the 0.8.0 CHANGELOG entry gained a
"Reference" block with every path, key, default, field, constant and log line; a 2-agent fact-check of both against the
code found 14 wording issues (e.g. "every boss" → the five classic bosses; "only BepInEx" vs the declared JsonDotNET
dependency; "seven" seen-keys was the live box's count, not the mechanism) — all applied. `HexiumDist/manifest.json`
description updated (250-char cap). `HexiumDist/Wonderland-v0.8.0.zip` built from manifest + README + CHANGELOG + icon +
the 20:12 DLL (zips are gitignored).


## 10. 2026-09-16 (early): 0.8.0 is live; player platform in every player listing (→ 0.8.1)

**0.8.0 is deployed.** Rohan installed it on 2026-09-15 at 22:05 (`BepInEx/plugins/Wonderland-v0.8.0/` on the box).
The 02:40 boot on 2026-09-16, read from the mirror's `LogOutput.log`, passed the §3.4 checks that can show on a second
boot: `[DiscordNotify] webhook configured: yes | server status: on | logins: on | deaths: on | first join: on | boss
defeats: on | heartbeat: off …`, `[BarrkBot] loaded 0 player row(s) …` / `exporting to … every 60 s`, **no**
`boss defeated: Eikthyr` during load, no Newtonsoft error (the Newtonsoft.Json Detector plugin confirms 13.0.0.0 from
`Managed/`). The template migration and seen-key cleanup lines are absent because they ran on the first boot, whose
log BepInEx overwrote - the cfg shows `DiscordTemplateStyle = 1` and the 0.8.0 join template. The first real
`barrkbot_wonderland.json` exists (`server.name` "Wubarrks Vanilla Bean", world_day 152, known_accounts 14, Eikthyr
defeated, 4 stations off, no player rows yet - nobody connected since the boot) and discord-barrkbot-57 was told.
The server runs `-crossplay` (PlayFab); its player history is 13 Steam + 1 PlayStation account. §3.5's first-session
checks (veteran not welcomed, hand-feed at an auto-fed smelter, `via rpc sent to owner` lines) are still to be watched.

**Platform labels (0.8.1, in the working tree, build clean 0/0, not committed).** Rohan: "when we output current
player information … include the platform they are on (PC, Xbox, Switch 2, etc)". Because 0.8.0 is live, this is
**0.8.1** (`Plugin.cs` ModVersion, csproj, manifest, samples bumped; CHANGELOG has its own 0.8.1 entry).

- `Core/Data/PeerPlatform.cs` (new): `Id(peer)` is the former `PeerJoinLeaveHook.ResolvePlatformUserID` (same
  construction as `ZNet.UpdatePlayerList`; PlayFab side now `TryParse`, which does not `Debug.Log` on failure);
  `Label(peer)` / `LabelForId(string)` / `LabelFor(Platform)` map `Steam`→`PC`, `Xbox`→`Xbox`, `PlayStation`→
  `PlayStation`, `Nintendo`→`Switch 2`, other→its own name, unparseable→`""`; `WithLabel(name, peer)` → `Name (PC)`.
  Account platform only - `RPC_PeerInfo` sends `Version.CurrentVersion.ToString()` without the platform prefix, and
  nothing else in the handshake names the device - so Xbox console and PC Game Pass are one label. The id never
  leaves the class.
- Shown: `[DiscordNotify] 'Name' (PC) connected./disconnected./died./joined … first time.`; `[Heartbeat] … Name (PC),
  Name (Xbox)`; Discord `{players}` roster (join/leave/boss/heartbeat defaults) and the new `{platform}` placeholder
  (all 8 template descriptions and the regex updated); BarrkBOT `players.<id>.platform` (+ `players_notes` clause;
  samples regenerated, probe run on `populated`: the string rides in the row, not ranked). `ConnectedCharacter`
  gained `Platform` / `NameWithPlatform`. README (pitch), CHANGELOG (0.8.1 Added + Reference), `BARRKBOT_CONTRACT.md`
  updated.
- **Discord defaults (Rohan approved "proposed", 03:10):** join / leave / first-join headlines now read
  `**{player}** ({platform}) joined|left **{world}**` / `Welcome **{player}** ({platform}) to **{world}**`; death,
  boss, heartbeat, online, offline unchanged. `MigrateDiscordTemplates` is staged (`CurrentTemplateStyle = 2`): style
  0 runs the 0.7.2 step then the 0.8.0 step, style 1 (the live cfg) runs only the 0.8.0 step, so on the first 0.8.1
  boot expect `[Config] 3 Discord message template(s) were still on an earlier version's default and have been moved
  to the 0.8.1 style …` once and `DiscordTemplateStyle = 2` in the cfg (Rohan's cfg had all three on the 0.8.0
  default at 02:40). A blank platform collapses the `()` in Fill instead of posting it.
- `HexiumDist/manifest.json` also carried an uncommitted `website_url` change (`https://live.ravenirongames.com/`)
  that was in the tree before this work - not mine, committed along.
- Deploy: `HexiumDist/Wonderland-v0.8.1.zip` (Rohan's step, same as §3.2); first-boot lines: the `[Config] 3 …`
  line above, then the usual `[DiscordNotify] webhook configured …` / `[BarrkBot] exporting …`; first join after
  that should log `'Name' (PC) connected.` and post the bracketed headline.


## 11. 2026-09-16 (mid-morning): 0.8.1 is live; auto-harvest reaches crops (→ 0.8.2)

**0.8.1 is deployed and its first boot passed.** The 03:16 boot on 2026-09-16 shows `Loading [Wonderland 0.8.1]` /
`Starting Wonderland v0.8.1`, the staged migration exactly once - `[Config] 3 Discord message template(s) were still
on an earlier version's default and have been moved to the 0.8.1 style …` - and the cfg now reads
`DiscordTemplateStyle = 2` (so it will not run again). The first join after it logged
`[DiscordNotify] 'TiCkLeChIcKeN' (PC) connected.`, and the live `barrkbot_wonderland.json` player rows carry
`"platform": "PC"` at `schema_version` 3. §10's open items are closed; 0.8.1 is the version in the field.

**The report, and what was actually wrong.** A player said auto-harvest was not doing anything on their farm; Rohan's
first read was "it's a misconception" - and half of it was. The `2x` on every `[ItemLedger] AutoHarvest moved 2x
$item_…` line is the world's own `resourcerate` global key running through `Game.ScaleDrops` (101052-101071), not
Wonderland doubling drops, and the feature was demonstrably alive: 259 auto-harvest drops in this boot's log. The
other half was real. Every one of those 259 is blueberries / thistle / common mushroom / flint / dandelion / yellow
mushroom / raspberries / wood / surtling core - **zero crops** - while a carrot field was being hand-picked next to
a carrot chest. Root cause: `Pickable.SetPicked` (71058-71082) is the only write of `ZDOVars.s_picked` in the
assembly and only writes it when `m_respawnTimeMinutes > 0` or `m_hideWhenPicked != null`; everything else is
`m_nview.Destroy()`ed on pick. The old trigger polled that key around each player every frame, so 47 of the 67
`Pickable` prefabs - every farm crop among them - could never trigger a sweep at all. (Live `AutoHarvestRadius` had
been 20; Rohan set it back to the 8 default today, which is what the new log lines report.)

**0.8.2 (in the working tree, build clean 0/0, not committed).** The trigger is now the pick itself.
`Subsystems/ItemFlow/HarvestTriggerPatch.cs` (new) is a Harmony prefix on `ZRoutedRpc.HandleRoutedRPC` (83646) that
catches the picking client's `"RPC_SetPicked"` true broadcast (`Pickable.RPC_Pick` 71050 → `RPC_RoutedRPC` 83632 →
`HandleRoutedRPC`, where vanilla drops it for lack of an instance) and calls `VacuumEngine.OnPickedRpc`. Separate
patch class from `WaterBuoyancyEngine`'s `RoutedRpcHandlerPatch` on the same method (SafePatch isolation); the body
never throws outward, or `RPC_RoutedRPC` would not relay the message to the other clients.

- Guards, all in `VacuumEngine`: sender `== ZDOMan.GetSessionID()` is skipped (our own `HarvestPickable` broadcast
  comes back through the same handler - `InvokeRoutedRPC(Everybody)` is handled locally on the sender, 83587-83590 -
  and without it the sweep re-enters itself without bound); the bool is read from `m_parameters` at position 0 with a
  `GetPos`/`SetPos` restore and `false` is ignored (a bush respawning); the prefab must be `IsSweepable`
  (`!m_tarPreventsPicking && (respawns || hides || m_harvestable)`) and not in `VacuumExcludedItems`, which now
  matches the plant name or the item name; the sender must be a `ConnectedCharacter` within `MaxPickReachMeters` 16 m
  (the RPC is client-forgeable). Then `MaxPendingTriggersPerPeer` 16 / `MaxPendingTriggers` 128,
  `HarvestSweepDelaySeconds` 0.5, `MaxSweepsPerFrame` 4, `TriggerWarningIntervalSeconds` 30,
  `FailureLogIntervalSeconds` 60 in the patch, and `PruneHarvestLedger` once a minute above `MaxTrackedPickables`
  50000 (drops entries older than a game day or whose ZDO is gone).
- The half-second deferral is not cosmetic: a scythe swing picks every plant in reach in one client frame and its
  `DestroyZDO` batch only leaves on the client's next `ZDOMan.Update` (76812-76821), so an inline sweep would
  re-harvest plants the player had already cut.
- `HarvestPickable` order changed to ledger + `SetOwner` + flags first, then the scaled `DropItem`, then
  `m_extraDrops` via `GetDropListItems()` (new - magecap / jotun puffs / vine ash / vine green / fiddlehead), then
  the `RPC_SetPicked` broadcast, then `DestroyZDO` for the destroy class. Farming-skill bonus yield, theft aggro and
  pick effects are deliberately not reproduced. `Pickable_RoyalJelly` no longer sweeps (tar check is client physics).
- Bumped to 0.8.2 in `Plugin.cs`, `Wonderland.csproj`, `HexiumDist/manifest.json`, `tools/barrkbot/sample_*.json`;
  config text updated in `Core/WonderlandConfig.cs` (~173-178) with no new keys and no changed defaults; README
  (pitch) and CHANGELOG (0.8.2 Fixed / Changed / Reference) written.

**Live test plan for the 0.8.2 boot** (nothing below has run on a real server yet):

1. Boot: `Successfully applied Harmony patch set: HarvestTriggerPatch` in the ItemFlow block, alongside
   `ZdoSetOwnerPatch` and `RoutedRpcHandlerPatch`.
2. Regression, flag class: pick one blueberry bush with others around it → `[AutoHarvest] BlueberryBush picked by
   'Name' - swept N more within 8 m.` plus the `[ItemLedger] AutoHarvest moved 2x $item_blueberries` lines. Walk away
   and back: no repeat sweep of the same bushes (the `_harvestedAt` ledger, not `s_picked`, is what holds).
3. The fix: at the carrot chest (33, 32, 183) pick **one** carrot out of a patch → `[AutoHarvest] Pickable_Carrot
   picked by 'Name' - swept N more within 8 m.` about half a second later, then the vacuum line
   `Vacuum moved … $item_carrot` as the chest pulls the drops in.
4. Scythe: one swing over 4+ barley → **zero** `[AutoHarvest]` lines for the plants that swing cut, and no extra
   items on the ground beyond what the swing itself dropped. (This is the deferral doing its job; a bonus sweep of
   barley standing *outside* the swing is fine and expected.)
5. Hot edit `VacuumExcludedItems = Carrot` while running → the next carrot pick sweeps nothing, no restart.
6. Hot edit `AutoHarvestEnabled = false` → sweeps stop immediately (pending ones are dropped), still no restart.
   Set both back afterwards.
7. Watch for `[AutoHarvest] ignored a pick …` warnings (distance or non-player), `[AutoHarvest] trigger failed: …`,
   and any `Exception in ZRpc::HandlePackage` in the log - expect none of the three. A distance warning during normal
   play means `MaxPickReachMeters` is too tight for the world's lag and should be raised, not that a cheat happened.
8. Expanded-chest hiccup - diagnosed the same morning by three parallel readers of the decompile and the live log,
   fixed in 0.8.2. Every `ZdoInventoryIO.Save` took ownership of the chest ZDO; the client of the player with that
   chest open hides the container panel and cancels the drag while it is not the owner (`InventoryGui.UpdateContainer`)
   until `ReleaseZDOS` hands it back (<= 2 s), and a taken chest can never write `s_inUse` again (owner-gated), so the
   busy gate failed open. `Save` no longer touches ownership; `PrefabSetScanner` drops vanilla's one-sector chunk
   overlap (the Karve anchored twice on adjacent log lines); `ContainerRowsEngine` only restarts its round-robin when
   the eligible list changed (the `eligible set refreshed` line, 438x this boot, is gone). Test: with two players on,
   one keeps a grown chest open for a minute, dropping and shift-clicking stacks - the container panel must never
   blink and no drag may cancel; `parked` lines should still appear now and then. If it still hiccups, the live
   tuning is the next lever, hot-reloaded: `ContainerRowsInterval` 2 -> 5, `ContainerRowsBatchSize` 125 -> 25,
   `SortInterval` 15 -> 30, `SortBatchSize` 100 -> 10.

## 12. 2026-09-16 (midday): 0.8.2 live and confirmed; vacuum goes near-player first (→ 0.8.3)

**0.8.2 deployed 11:56:56, boot clean:** `Loading [Wonderland 0.8.2]`, `Successfully applied Harmony patch set:
HarvestTriggerPatch`, one ContainerRows announce (18 of 64 types), zero `eligible set refreshed` lines, zero
warnings. First crop sweep at 11:59: `[AutoHarvest] Pickable_Carrot picked by 'Coffee' - swept 15 more within 8 m.`
followed by 30 carrots vacuumed. Rohan confirmed the expanded-chest hiccup is gone ("no delay noticed when placing
items in chest anymore"). Also published to Hexium.

**Live cfg edits Rohan asked me to make** (the read-only rule was lifted for these keys only, `sed` on the one line
each, file never displayed): `AutoHarvestRadius = 10`, `VacuumRadius = 20` (12:03), then `VacuumInterval = 1`,
`VacuumBatchSize = 60` (12:04) for "vac time needs to be quicker". Both hot-reloaded (`[Config] config file changed
on disk` at log lines 788 and 824).

**Why the vacuum was slow, and 0.8.3:** the vacuum was only the world-wide round-robin over 64 container prefab
types at 25 chunks per 2 s; on the 604,766-ZDO live world that is roughly half a minute per chest. 0.8.3 adds a
near-player pass (`VacuumEngine.ProcessVacuumNearPlayers` → `VacuumAround`): ground items around each connected
player first, then only the containers within `VacuumRadius` of one of them, plus an instant `VacuumAround` right
after every auto-harvest sweep. New key `VacuumNearPlayersRadius` (32 m). `_destroyedThisBatch` is now cleared once
per frame (a per-pass clear would have let the post-sweep vacuum double-move a stack). Build 0/0; the reviewer
workflow for it was killed twice by session interrupts, so the verification is mine plus a background second opinion.

**Defaults (Rohan, 12:16):** `AutoHarvestRadius` 4.5 and `VacuumRadius` 15 are the shipped defaults from 0.8.3, with a
`VacuumDefaultsStyle` stamp + `MigrateVacuumDefaults` (float `MoveOffOldDefault`) so files still on 8 / 10 move over
once; the live cfg already holds 4.5 / 15, so its first 0.8.3 boot should log no radius migration line and write
`VacuumDefaultsStyle = 1`.

**Test after the 0.8.3 boot:** (1) `Starting Wonderland v0.8.3`; (2) drop a stack of something a nearby chest
already holds and count seconds to `Vacuum moved` (expect ≤ VacuumInterval); (3) pick one carrot in a patch: the
`[AutoHarvest] … swept N` line should be followed by `Vacuum moved` lines in the same second; (4) then set
`VacuumInterval = 2` and `VacuumBatchSize = 25` back and re-check step 2.

