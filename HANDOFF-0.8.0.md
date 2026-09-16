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
