# The BarrkBOT contract — read before renaming or reshaping anything

**BarrkBOT reads this file off this server's local filesystem via its generic multi-mod scanner
(`WindowsDEV/Discord-BarrkBOT/src/actions/valheimModData.js`, 6.0.119 at the time of writing). It is
an interface, not internal state.** Renaming the file or a field breaks a program in another repo
that has no way to find out. The general contract is `libs-Tools/IMPLEMENTATIONS/BarrkBOTExports.md`.

| Path | Written by | BarrkBOT reads it as |
| :--- | :--- | :--- |
| `BepInEx/config/Wonderland/barrkbot_wonderland.json` | `Subsystems/BarrkBot/BarrkBotExport.cs`, every `BarrkBotWriteSeconds` (60) | this server's live state, per-player rows, and lifetime automation counters |

`BepInEx/config/Wonderland.BarrkBot.<world>.dat` is the mod's own persistence (the registry the
export is built from; JSON, deliberately not a `barrkbot_*.json` name so the scanner never sees two
files claiming the same facts). BarrkBOT does **not** read it.

## Why the shape is what it is

- **Everything is server-measured.** Sessions and connected time come from the connection (ZNetPeer) once
  its character has appeared in the server's own ZDO view, deaths from the character ZDO's dead flag, bosses
  from the world's global keys, automation from the mod's own ledger. Nothing is a client's word, so there is
  no RPC and no client-side component (BarrkBOTExports.md §6).
- **`players` is keyed by the stable character id** (`ZDOVars.s_playerID`, written as a decimal string),
  with `name` inside the row. A connected player whose character has not spawned yet has no row until it
  does. Sessions follow the peer, not the character: a death (8-18 s with no character ZDO) is not a session.
- **Two time bases, declared.** `session_started_at` is the server process start and `tracking_since` is
  when the persisted counters began; `server` is the only block that resets with the process, `players`
  and `lifetime` are cumulative since `tracking_since`. The `server` block deliberately has **no** own
  `generated_at` (the reader would word its zeros as "none so far in this one").
- **`online` is honest.** Every periodic write says `true`; the one write from `Plugin.OnDestroy` on a clean
  shutdown says `false` with `players_online` 0 and every `online_now` false, because the reader only starts
  doubting a file after 60 minutes. A crash leaves the last periodic file, and then the reader's age
  caveat is the signal. Switching the export off at runtime deletes the file. BarrkBOT 6.1.20 keys the realm's
  liveness on exactly this: an export that asserts `server.online` outranks any other export's roster (TortalPortal
  Lite's `barrkbot_portals.json` beside ours has a per-portal `online` flag, and before 6.1.20 whichever file was
  written last won, flipping the status to `online: null`) - so `server.online` must keep being written on every
  `write_seconds` cycle, which it is (confirmed live 2026-09-16: `status.json` took `online: true`, day 152 from
  "Wonderland 0.8.0").
- **Rows are created on first activity, never pre-seeded.** An empty `players` map means "not recorded yet",
  and `players_notes` says so.
- **Units are in the names** (`*_seconds`, `*_alltime`), and no counter name matches the reader's cadence
  exclusion (`(write|census|interval|poll|sync|tick|export|refresh|update)_seconds`).
- **`null` means not measured** (`world_day` when EnvMan is unavailable); keys are never omitted to hide a
  value.
- **Caveats are `_notes` keys**, which the reader lifts out as attributed guidance: connected time is not
  playtime, automation counts are not player actions and must never be summed or ranked against another
  mod's, security flags are suspicions not verdicts.
- **Not exported on purpose:** each player's platform id (Steam64 / PSN / Xbox - the bot has no reader for it
  and it would only be voiced verbatim; only its platform half goes out, as the `platform` label) and the
  per-player security-flag tally (the reader ranks every numeric row field, and "most flagged" from heuristics
  is not a public answer). Both stay in the `.dat` for the admin.

## Field names currently depended on

Top level: `schema_version` (3 - the 0.10.5 blocks below are additive, so it did not move), `generated_at`, `source`,
`intervals.write_seconds` (the effective value, floor 10), `session_started_at`, `tracking_since`, `export_notes`, `server`,
`server_notes`, `players`, `players_notes`, `players_not_achievements` (`deaths_alltime`, `sessions_alltime`), `lifetime`,
`lifetime_notes`, `progression`, `progression_notes`, `enforcement`, `enforcement_notes` (in that order - the two new blocks
come last on purpose, see the reader-side cap below).

`server` (live): `name` (the dedicated server's `-name` string, "" if unreadable - a cross-check for a reader that
sweeps more than one server root), `world_name`, `online`, `world_day` (integer or null), `known_accounts`,
`uptime_seconds`, `players_online`, `players_online_names`, `peak_players_online`, `wonderland_version`,
`carry_weight_multiplier`, `stamina_regen_multiplier`, `bosses_defeated`, `bosses_remaining`, `bosses_defeated_at`
(dates only for defeats seen since 0.8.0), `stations_switched_off`.

Per player (flat scalars only - nested objects are dropped from multi-row listings): `name` (required on every
row or the whole map stops being per-player), `online_now`, `platform` (string: `PC`, `Xbox`, `PlayStation`,
`Switch 2`, or `""` when unknown - the account platform of the connection they last used; `Xbox` includes the
PC Game Pass build), `first_seen_at`, `last_seen_at`, `sessions_alltime`, `connected_seconds_alltime`,
`deaths_alltime`, `welcomed_at` (only when the account's first ever visit happened after 0.8.0). Verified through
the 6.1.5 reader: a string scalar rides along in the row and is not ranked.

`lifetime` (`_alltime` names so the reader knows they do not reset; no name is shared with `server` or a player
row, which would draw a "same name, different scope" note): `production_supply_items_fed_alltime`,
`production_supply_items_fed_by_item`, `vacuum_items_moved_alltime` (ground pickups + auto-harvest),
`vacuum_items_moved_by_item`, `item_cache_items_stored_alltime`, `item_cache_items_returned_alltime`,
`starter_kit_items_granted_alltime`, `raids_blocked_alltime`, `spawns_culled_alltime`, `security_flags_total_alltime`.

`progression` (live: the ceiling in force, read from the hot-reloaded config and the world's global keys at every write,
never cached; the website binds to these exact names): `mode` (`auto` | `fixed` | `off`, lowercase - from `MaxAllowedTier`:
`Auto` -> `auto`, `None` / blank / a string that names no tier -> `off`, a tier name -> `fixed`), `configured` (the raw
`MaxAllowedTier` string), `tier` (the tier name in force, `""` when off), `tier_index` (Meadows 1 ... DeepNorth 8, 0 when off),
`tiers` (the eight names in ledger order), `unlocked_by` (tier -> boss display name, Swamp through DeepNorth, ledger order -
not alphabetical, unlike the item maps), `next_boss` / `next_key` / `next_tier` (the boss, its `defeated_*` key and the tier
its defeat unlocks; **`""`** when no boss lifts the ledger - DeepNorth reached, or mode `fixed` or `off`. Not null: the
reader words a nested null as "not measured yet", and this is a fact), `bosses_defeated_keys` (every `defeated_*` global key that is set, Eikthyr
included although it lifts nothing, ledger order), `gated_tiers` (tiers above `tier`, `[]` when off), `gated_items_count`,
`gated_items_by_tier` (gated tier -> count, ledger order), `items_by_tier_count` (`Unrestricted`, `Meadows` ... `DeepNorth`,
`Cheat` - every key always present, 0 where empty), `tier_table_file` (the classifier's on-disk table, one line per item with
its reason: `BepInEx/config/Wonderland.ProgressionTiers.txt`), `tier_json_file` (the same table as JSON for the website:
`BepInEx/config/Wonderland/progression_tiers.json` - `{generated_at, source, ledger, tiers, seeds, station_floors,
name_floors, items: {prefab: {tier, reason}}}`, `tier` is `Unrestricted` or a tier name; deliberately not a `barrkbot_*`
name so the scanner never sees two files claiming the same facts). **Item names are not in this file**: 160-260 gated
names would push the block past the local provider's 6,000-character slice (quirk below), so a consumer that wants the
list reads `tier_json_file`. Counts are the classifier's derived table, built on the first tick after a start that `ObjectDB` exists
(it is often not there yet at the world-ready hook) and rewritten within a second of a `ProgressionItemExemptions` /
`BannedItemsList` edit; until the first build they are 0 and `progression_notes` says so. Nothing in the block is a player's action or a ranking; `ItemTier.None` is always
written as the string `Unrestricted`.

`enforcement` (live config, all hot-reloaded, read at every write - what the guards DO when they find something, never
how often they did): `container_sweep`, `container_sweep_removes` (true = a gated / banned / implausible item is removed from
the chest, false = logged only), `equipment_guard`, `equipment_guard_kicks` (true = the wearer is disconnected, false = logged
only), `quality_guard`, `admin_bypass`, `vanilla_client`, `vanilla_client_kicks`, `vanilla_client_admin_bypass`,
`strict_version`, `active_probe`, `routed_rpc`, `placement_guard` (all booleans), `max_plant_batch` (integer), `banned_items`
and `pinned_items` (the `BannedItemsList` / `ProgressionItemExemptions` strings split on `,` and `;`, trimmed, config order;
a pinned entry is either `Prefab` or `Prefab:Tier`, verbatim). No per-guard counters: violations stay in
`lifetime.security_flags_total_alltime`, on purpose.

`progression_notes` and `enforcement_notes` are single strings like every other `_notes` key (the reader lifts only string
`_notes` as attributed guidance - quirk below). Same voice, same rules: guidance, never data.

## Known reader-side quirks (BarrkBOT, not this mod)

- `valheimModExports.js` `cumulativeCollections` only recognises a collection as cumulative when its name starts with
  `lifetime` or its own top-level keys end in `_alltime`; a per-player map keyed by ids is not inspected, so the
  reader's generated TWO TIME BASES note tells the model that `players.*_alltime` reset on restart, contradicting
  `export_notes`. Reported to the BarrkBOT maintainer (2026-09-15); the fix belongs in the reader.
- `dissect()` lifts a `_notes` key as attributed guidance only when its value is a **string** (`typeof v === 'string'`);
  an array renders as a summary collection (`kind: "summary"`) that the model reads but not as "From the mod author"
  guidance. This is why every `_notes` key, the two new ones included, is a single string. Verified through the probe, 6.1.20.
- A nested null is always worded as "NOT RECORDED - say the figure has not been measured yet" (`nullFields`) and there is no
  per-field meaning table entry for ours - which is why `next_boss` / `next_key` / `next_tier` are `""` rather than null when
  nothing lifts the ledger.
- **The local provider's tool-result cap is 6,000 characters of compact JSON** (`chat.js` `LOCAL_TOOL_RESULT_CHARS`,
  hard-sliced after the reader has squeezed the player rows to one). The live file measured 6,844 through the reader
  before these blocks existed (13 players, the `vacuum_items_moved_by_item` map), so the tail of `lifetime` was already
  being cut; `progression` + `enforcement` + their notes add roughly 4,200 characters (the gated item names, ~18 each,
  were moved out to `tier_json_file` for exactly this reason). The two blocks are written **after** `lifetime_notes`, so
  nothing the bot already answered moves; on the local provider the model may still not see them until the cap changes;
  the website reads the whole file and is unaffected. Measured 2026-09-20 with `barrkbot-render-probe.mjs` (6.1.20).

## Consumers

BarrkBOT (`valheimModData.js` / `valheimModExports.js`, 6.1.5 - a per-player row with a null `name` demotes the whole map to a summary, so rows without a name are never emitted) and the Wonderland website (`server/exports.js`,
binds `server.{world_name, online, world_day, players_online, players_online_names}` and carries the whole file as
a telemetry section). Before changing any field above: regenerate the samples in `tools/barrkbot/` to
`BarrkBotExport.Build()`'s exact shape and run `tools/barrkbot/barrkbot-render-probe.mjs all` from the BarrkBOT
repo (BarrkBOTExports.md §7); once the mod has run, feed the real file to the probe instead.
