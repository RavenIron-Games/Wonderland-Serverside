# Wonderland Companion — design for an optional client-side mod

Status: **design, nothing built.** Written 2026-10-08 against Wonderland 0.10.11 (this tree). Every server-side fact
below is taken from the code in this repository and the decompile citations already in its comments. The facts about
the vanilla **client** that this design needs and that this repository does not yet confirm are listed in
[§11 Verify before coding](#11-verify-before-coding). `libs-Tools` was not available when this was written.

---

## 0. In one page

**What it is.** `WonderlandCompanion.dll`, an optional BepInEx plugin for PC players. A player who installs it on a
Wonderland server gets:

| Feature | What the companion player gets | What a vanilla player sees |
|---|---|---|
| **Containers** | Grown rows the moment a chest loads, empty or not, ship holds included. A chest that opens on the first press of E. A client that can never be the one that drops or clamps a stack. Optional *enhanced* chests: extra columns, then bigger stacks. | Exactly today: anchored rows. Enhanced chests (only if the admin turns them on) refuse to open for them, with a message saying why. |
| **Vacuum** | No 1 s settle hold on their own drops, so no pickup duplication. A pull within one round trip of a drop instead of up to `VacuumInterval`. Per-chest settings: vacuum on or off, a filter list, catch-all, radius. Toasts showing what went where. | Exactly today. |
| **Craft from containers** | Crafting, upgrading and later building use materials from chests in range. The requirement list shows `12 (+40 in chests)`. | Nothing. A vanilla client checks its own inventory before it sends anything, so this cannot work without the companion. |
| **Production** | Station status on hover (`ON · coal ✓ · copper from 2 chests`). A hotkey toggle in place of emotes. Separate fuel and ore switches per station, an allowed-input list, a reserve, a fill target and linked source chests. A panel listing every station nearby, with bulk on and off. | Emotes still work. Any station they have not set up a companion player for behaves exactly as today. |

**The three rules everything else follows from:**

1. **Vanilla stays first-class.** Console and crossplay players cannot mod, so the companion is purely additive.
   Nothing a vanilla client experiences changes unless an admin opts in, and the one opt-in that affects them,
   enhanced chests, is per chest.
2. **The server stays the single writer of shared state.** The companion supplies UI, intent and presentation.
   Every change to a container or station is made by the server's existing engines, under their existing in-use,
   ownership and ledger rules. This is the main reason it is more reliable: the duplication and loss bugs this
   repository has fixed (0.8.0 smelter, 0.8.2 chest ownership, 0.8.4 pickup, 0.10.10 hull) all came from two machines
   writing one ZDO. A client-side craft-from-containers that takes ownership of chests would bring that class of bug
   back.
3. **It has to pass Wonderland's own vanilla enforcement.** The companion speaks only through routed RPCs that the
   server itself registers. It ships no ServerSync and no Jotunn, makes no custom ZRpc calls, changes no version
   string and adds no player sync keys. It stays silent until a server advertises support.

**Recommended build order** (cheapest, lowest risk and most visible first): Phase 0, protocol → Phase 1, production
management plus container reliability → Phase 2, craft from containers → Phase 3, vacuum cooperation plus build from
containers → Phase 4, enhanced chests (4a columns, 4b stacks). See [§9](#9-rollout).

---

## 1. Constraints the server already imposes

These are not choices. The current code forces each of them.

### 1.1 Mod enforcement (`Subsystems/Security/ModEnforcement*.cs`)

A non-admin client is kicked when it does any of the following:

| Trigger | Where | Consequence for the companion |
|---|---|---|
| Sends a ZRpc method that is not on `InitVanillaZRpcWhitelist` | `Patch_ZRpc_HandlePackage` | **No custom ZRpc, ever.** |
| Answers a probe: `ServerSync VersionCheck`, `RPC_Jotunn_*`, `ValheimPlus_VersionCheck`, `AzuAntiCheat_VersionCheck` (sent in the `RPC_PeerInfo` postfix) | `SendActiveModProbes` plus the trap table | **No ServerSync and no Jotunn.** The companion cannot use the `Shared/ServerSync.cs` that the server uses. Gameplay settings reach it through the handshake ([§3](#3-protocol)). |
| A version string that fails `IsVanillaVersionString` | `Patch_ZNet_RPC_PeerInfo` | Never touch `Version.GetVersionString()`. |
| A `ServerSyncedPlayerData` key that is not vanilla | `Patch_ZNet_RPC_ServerSyncedPlayerData` | No player sync keys. |
| A routed RPC hash that is not vanilla | `Patch_ZRoutedRpc_HandleRoutedRPC` → `IsVanillaRoutedRpc` | **The way in:** `IsVanillaRoutedRpc` accepts any hash in `ZRoutedRpc.instance.m_functions` ("dynamic resilience 1"). The server registers the companion's methods, so it never kicks a client for calling them. |

Two consequences shape the handshake:

- **Never speak first.** A companion that sent a hello to a 0.10.x server, which has enforcement but has not
  registered the companion's methods, would be **kicked**. So the server advertises first, and a companion that hears
  nothing stays silent for the whole session ([§3.2](#32-handshake)).
- **Always register, even when disabled.** If an admin turns the companion off, the server still registers the
  methods. It just stops advertising. Otherwise a stale client could get kicked for a hash that was registered last
  boot.

Players who run *other* mods with ServerSync or Jotunn are still kicked, as before. The companion is designed to be the
only mod a player needs on a Wonderland server. The README has to say so.

### 1.2 The server never has a live object (`Core/Data/ConnectedCharacters.cs`)

`Game.FixedUpdate` pins the dedicated server's reference position at (1e6, 0, 1e6), so the server never instantiates
a `Container`, `Smelter` or `Player`. That has three consequences:

- Every companion request is a **global** routed RPC (target ZDO `None`) and is handled at the ZDO layer, the same way
  every engine here already works. Nothing may target a `ZNetView` instance on the server.
- A routed RPC aimed at a ZDO the server owns is **dropped silently** (HANDOFF-0.8.0 §6). That is why a chest the
  server owns cannot be opened ("press E twice", HANDOFF-0.8.0 §5.2), and the companion's reliable open
  ([§4.3](#43-reliability-for-companion-players)) is built around it.
- **The server cannot write into a player's bag** (`ZdoInventoryIO` header: a character's inventory is never mirrored
  into its ZDO). Anything that has to end up in an inventory is added by the companion itself, or dropped as a ground
  item the vanilla way.

### 1.3 Ownership lessons already paid for

| Lesson | Source | How the companion design uses it |
|---|---|---|
| The server never takes ownership of a container a player might be using. `ZDO.Set` is not owner-gated and replicates without the claim. | `ZdoInventoryIO.Save` (0.8.2) | Server-side debits for crafting use exactly this write path. |
| The server never takes ownership of a station. It uses OwnerRpc, Direct or Skip. | `ProductionSupplyEngine` (0.8.0) | Station settings change *what* the engine feeds, never *how* it delivers. |
| A hand-over has to start at the client so its final packet is the last word. | `HullBorrow` (0.10.10) | The vacuum release ([§5.2](#52-owner-originated-hand-over-closes-the-pickup-duplication)) is the same pattern applied to ground stacks. |
| A claim on a client-owned stack can race that client's pickup. | `VacuumEngine` settle step (0.8.4); `ClaimEcho` (0.10.11) | With the companion, the owner releases the stack instead of the server claiming it, so there is no race. |
| A file only the server writes cannot lose a race. ZDO fields the owner rewrites can. | `SupplySwitch` | Station and container settings live in server-side files keyed by ZDOID. |
| `s_inUse` is the busy gate. Skip a busy container for the tick; do not retry. | `ZdoInventoryIO.IsBusy` | Debits skip containers that are in use and report them to the UI as "in use". |

### 1.4 Platform reality

Only BepInEx clients can run the companion: in practice Steam or Linux PC and Steam Deck. Xbox, PlayStation and
Switch 2 players stay on the vanilla path permanently, which is why rule 1 is non-negotiable and why enhanced chests
are opt-in per chest.

---

## 2. Architecture

```
            vanilla client ──(vanilla traffic only)──────────────┐
                                                                 ▼
 companion client ──(vanilla traffic + WLC_* routed RPCs)──► Wonderland server
   UI, intent, presentation                                  │
   reads replicated ZDOs for display                         ├─ Subsystems/Companion (new): protocol, peers, validation
   never writes a container it did not open by hand          ├─ existing engines: Vacuum, ProductionSupply, ContainerRows,
                                                             │  GridGrowth, Sort, ItemCache, ItemLedger, AuditLog
                                                             └─ new state files: ContainerPrefs, StationPrefs, Escrow journal
```

### 2.1 Where the code lives

**Recommendation: this repository, with a second project.**

```
Shared/CompanionProtocol.cs          ← compiled into BOTH projects: method names, op codes, codecs, version
Subsystems/Companion/*.cs            ← server side (Wonderland.csproj)
Companion/WonderlandCompanion.csproj ← client plugin, built against the CLIENT assemblies
Companion/src/**                     ← client side
Companion/HexiumDist/                ← its own manifest.json / README.md / CHANGELOG.md / icon
```

One shared protocol file is what stops the two sides drifting apart. `Wonderland.csproj` notes that the root copies
in `libs-Tools` are the **client** assembly set, so the companion project references those, and the server project
keeps its `1.0/server` references. The two DLLs ship as separate packages, and the server never depends on the
companion.

### 2.2 Client modules

| Module | Responsibility |
|---|---|
| `CompanionPlugin` | `[BepInPlugin("wubarrk.wonderland.companion", …)]`. Local-only config: keybinds, UI toggles, toast verbosity. No gameplay settings live here. |
| `Net/CompanionLink` | Registers the client's `WLC_*` handlers. Runs the handshake state machine **Dormant → Advertised → Welcomed**, correlates requests with timeouts, and resets on disconnect. Exposes `Active` and the server's capability block. |
| `Containers/` | Spec cache, lossless load, size application, reliable open, the container toolbar (prefs UI). |
| `Vacuum/` | Release responder, drop nudger, feedback (toasts, pull lines, radius ring). |
| `Crafting/` | Source scanner (reads container ZDOs in range), requirement UI patches, craft, upgrade and build flows. |
| `Production/` | Hover status line, hotkeys, the settings dialog for one station, the production panel. |
| `UI/` | A few shared widgets built from vanilla `InventoryGui` styles. No asset bundles in v1. |

**Dormancy is the safety net.** Every Harmony patch body starts with `if (!CompanionLink.Active) return;`, the
original runs, and the body is wrapped in try/catch. On a vanilla server, an older Wonderland or in singleplayer, the
companion is inert, and a bug in it can only fall back to vanilla behaviour.

### 2.3 Server modules (new `Subsystems/Companion/`)

| File | Responsibility |
|---|---|
| `CompanionSubsystem.cs` | `IWonderlandSubsystem`. Registers the `WLC_*` handlers in `OnWorldReady` (always, see §1.1), sends the advertise, drives timers. |
| `CompanionPeers.cs` | Peer uid → `{ protoVersion, clientVersion, features, tokenBucket, escrows }`. Cleared from the existing `ZNet.Disconnect` prefix (`ModEnforcementPatches.Patch_ZNet_Disconnect`). |
| `CompanionRequests.cs` | Op dispatch plus the validation pipeline ([§3.4](#34-validation-pipeline-every-request)). |
| `ContainerSpec.cs` | Effective width, height and stack multiplier **per container ZDO** ([§4.2](#42-containerspec-one-answer-to-how-big-is-this-chest)). |
| `ContainerPrefs.cs` | Per-chest settings (vacuum, craft source, supply source, enhanced flag). File-backed, keyed by ZDOID, pruned like `SupplySwitch`. |
| `StationPrefs.cs` | Grows out of `SupplySwitch` ([§7.3](#73-server-side-stationprefs-generalises-supplyswitch)). Migrates `Wonderland.SupplyOff.<world>.dat` on first load. |
| `CraftDebit.cs` | Plan, commit, escrow, refund ([§6](#6-craft-from-containers)). |
| `EscrowJournal.cs` | A crash-safe file of open escrows (`Wonderland.CompanionEscrow.<world>.dat`). Anything unresolved at boot goes to `ItemCache`. |
| `WardAccess.cs` | A ZDO-level ward check for a player id at a position. The server has no `PrivateArea` instances. |

---

## 3. Protocol

### 3.1 Transport

- Global `ZRoutedRpc` methods, target ZDO `None`, **one `ZPackage` argument each** so the payload can be versioned.
- **Five method hashes, total.** Everything else is an op code inside `WLC_Request` or an event code inside
  `WLC_Push`. That keeps the surface enforcement sees small, and it means adding a feature never needs a new hash.

| Method | Direction | Payload | Notes |
|---|---|---|---|
| `WLC_Advertise` | S→C | `protoMin, protoMax, serverVersion` | Sent to every peer right after `RPC_PeerInfo` (beside `SendActiveModProbes`). A vanilla client ignores an unknown global routed RPC (**verify**, §11.1). |
| `WLC_Hello` | C→S | `proto, companionVersion, featureBits` | Only after an advertise has been heard. |
| `WLC_Welcome` | S→C | `proto, sessionToken, enabledFeatures, ParamsBlock` | `ParamsBlock` = ranges, limits, keybind hints and the per-prefab spec table. Sent again as a `Config` push whenever a relevant `SettingChanged` fires (the 5 s hot-reload poll already raises it). |
| `WLC_Request` | C→S | `requestId, op, body` | Single entry point. |
| `WLC_Reply` / `WLC_Push` | S→C | `requestId, status, body` / `event, body` | Two hashes, counted as one row here: the reply to a request, and anything the server sends unsolicited. |

**Status codes:** `Ok, Denied(reason), Busy, OutOfRange, Insufficient, RateLimited, Disabled, BadRequest, Stale`.

### 3.2 Handshake

```
server                                      companion client
  │ RPC_PeerInfo postfix                         │
  │── WLC_Advertise(1..1, 0.11.0) ──────────────►│  Dormant → Advertised
  │◄─────────────── WLC_Hello(1, 0.1.0, bits) ───│
  │ CompanionPeers.Add(peer)                     │
  │── WLC_Welcome(1, token, features, params) ──►│  Advertised → Welcomed (Active = true)
  │      … requests / replies / pushes …         │
  │ ZNet.Disconnect prefix: refund escrows,      │
  │ CompanionPeers.Remove(peer)                  │
```

- **No advertise means silence.** A server that has the companion turned off, or an older Wonderland, is never sent
  anything.
- **Version skew.** The server picks the highest protocol both sides support. If there is none, it sends a welcome
  with no features, and the client shows a single toast: "Wonderland Companion x is too old/new for this server;
  running as vanilla". Nobody is ever kicked over the companion version: it is optional.
- A `WLC_Request` from a peer that has not completed the handshake is **ignored and audited, not kicked**, so nobody
  can grief a player by replaying hashes.

### 3.3 Op codes (protocol 1)

| Op | Phase | Body → reply |
|---|---|---|
| `QueryContainers(center, radius)` | 1 | → rows `{zdoid, prefab, spec, prefs, flags: inUse, hullSimulated, denied}` |
| `OpenContainer(zdoid)` | 1 | → `Ok` once ownership has been handed to the requester ([§4.3](#43-reliability-for-companion-players)) |
| `QueryStations(center, radius)` | 1 | → rows of `StationStatus` ([§7.2](#72-what-the-server-reports)) |
| `SetStationPrefs(zdoid, prefs)` / `SetStationsInArea(center, radius, on)` | 1 | → `Ok` + new state |
| `LinkSource(station, container, add)` | 1 | → `Ok` |
| `CraftDebit(purpose, stationZdoid, reqs[])` | 2 | → `{escrowId, taken[], sourcesBusy}` or `Insufficient{have[]}` |
| `CraftCommit(escrowId)` / `CraftCancel(escrowId)` | 2 | → `Ok` (cancel refunds) |
| `SetContainerPrefs(zdoid, prefs)` | 3 | → `Ok` + new prefs |
| `VacuumNudge(zdoids[])` | 3 | no reply (a hint) |
| `ReleaseAnswer(itemZdoid, released, dataRevision)` | 3 | no reply |
| `RecoverCache()` | 1 | the same as `/comehere` (`CacheClaimControl`) |

**Push events:** `Config(ParamsBlock)`, `VacuumMoved{container, item, count}` (batched every 0.5 s, only to
companion players within the radius), `ReleaseOffer{item, container}`, `StationChanged{zdoid, status}`,
`EscrowResolved{escrowId, how}`.

### 3.4 Validation pipeline (every request)

The server treats each request as hostile input. A forged "companion" must never gain anything a player standing
there could not already do by hand.

1. **The peer has completed the handshake** and its feature is enabled. A token bucket per peer (default 20 req/s,
   burst 40) gives `RateLimited`. Payloads are capped: 64 requirements, 256 ZDOIDs, radius ≤ the feature's
   server-side cap.
2. **Position comes from the server.** Distance is measured from the character ZDO (`peer.m_characterID`) to the
   target, never from a client-supplied position. The slack follows the auto-harvest check
   (`VacuumEngine.MaxPickReachMeters`, 16 m).
3. **The target is the right kind of thing.** The prefab is a container (`ContainerRegistry.IsContainerPrefab`) and
   player-buildable (`ContainerRows.IsEligible`), or it is a tracked station.
4. **Access is checked the way vanilla checks it:** container privacy (`Container.m_privacy` on the template plus
   `s_creator` on the ZDO) and the ward (`WardAccess`). Admins get no special path here. These are player actions.
5. **The engine's own gates apply:** busy (`s_inUse`), a hull a client is simulating (`ShipAttachment.IsSimulatedByClient`),
   and an enhanced chest opened by a non-companion client.
6. **Ledger and audit.** `ItemLedger.RecordTransfer("Companion:<op>", …)` for every item moved, `AuditLog` for every
   `Denied` that names a tampered field. The BarrkBOT `lifetime` counters gain `crafted_from_containers` and
   `vacuum_released`. Those are additive fields: see `BARRKBOT_CONTRACT.md` before adding them.

---

## 4. Containers: sizes and reliability

### 4.1 What vanilla clients force today

From `ContainerRows` and `GridGrowth` (client decompile 67784 / 68700 / 68817 as cited there):

- **Rows** grow because `Inventory.Load` accepts a row past the grid and `UpdateRows()` resizes to fit, *but only
  while a stack sits in the last row*. So the server has to keep an anchor parked there. A chest whose bottom row
  empties shrinks until the next sweep visit (`ContainerRowsInterval` × the round-robin). A floating hull only gains
  its rows after its crew has left the area and come back (CHANGELOG, boat storage).
- **Columns** past the prefab width are **dropped** on load.
- **Stacks** above the client's `m_maxStackSize` are **clamped** on load.

### 4.2 `ContainerSpec`: one answer to "how big is this chest?"

Today `ContainerRows.GetGridSize(prefab, template)` answers per *prefab*. Enhanced chests need a per-*ZDO* answer, so
it becomes:

```csharp
struct ContainerSpec { int Width, Height; int StackMultiplier; bool Enhanced; }
ContainerSpec ContainerSpec.For(ZDO zdo, GameObject prefab, Container template)
```

| Mode | Width | Height | Stacks | Who can open it |
|---|---|---|---|---|
| **RowsOnly** (default, which is today) | vanilla | vanilla × `ContainerRowMultiplier` | vanilla | everyone |
| **Enhanced**, chest flagged in `ContainerPrefs` | vanilla + `CompanionEnhancedExtraColumns` | the same as RowsOnly | × `CompanionEnhancedStackMultiplier` (phase 4b) | companion players only |

Every caller of `GetGridSize` moves to `ContainerSpec.For`: `VacuumEngine`, `ProductionSupplyEngine`, `SortEngine`
(its merge cap at `SortEngine.cs:91/104`), `GridGrowth.HasOverflow`/`EnforceOverflow` (its width and stack tests,
`GridGrowth.cs:53/83`), `ItemSanityGuard.IsPlausible` (its stack ceiling, `ItemSanityGuard.cs:31`, × multiplier for
enhanced chests, so they are not flagged), and `ItemIntegritySweep`. That is mechanical, but it is the one change that
touches most engines, so it lands on its own first, behind no feature flag, with no change in behaviour (every chest
is RowsOnly).

**Taking enhancement off is free.** Clearing the flag shrinks the spec, and the existing overflow guard moves
whatever no longer fits to a sibling chest or the `ItemCache`, exactly as it does today when an admin lowers the
multiplier.

### 4.3 Reliability for companion players

These ship in **Phase 1**, need no admin opt-in, and keep vanilla compatibility:

1. **Rows without an anchor.** The companion sets `Container.m_height` (and its inventory's height) from the spec
   table in `Welcome`, before the first load. A grown chest is full size the moment it appears: empty, freshly built,
   or a ship hold with its crew aboard. The server keeps anchoring for vanilla players, and an anchor costs a
   companion player nothing. Items the companion places in a grown row are ordinary row positions, which vanilla
   clients already render.
2. **Lossless load.** The companion patches its own container `Inventory.Load` to **grow to fit and never drop or
   clamp** (width, height and stack). A companion client is therefore never the machine that destroys a stack, even
   with a stale spec cache or an admin's mid-session change. For a chest that is not enhanced it only *preserves*
   what it found, and the server's overflow guard still corrects the layout.
3. **Reliable open.** When a companion player presses E and the vanilla `RequestOpen` would go to the server (server
   owned, so it would be dropped) or gets no response in 300 ms, the companion sends `OpenContainer(zdoid)`. The
   server validates it (§3.4), then calls `SetOwner(requester)` + `ForceSendZDO`, the grant `HandlePickupRequest`
   already makes for items. The client then opens locally as the owner. That closes HANDOFF-0.8.0 §5.2 ("press E
   twice") for companion players.
4. **A visible sync state.** When a server write lands on the chest a companion player has open (vacuum, supply,
   sort), the panel re-renders with a brief highlight on the changed slots instead of looking like it glitched.

### 4.4 Enhanced chests (Phase 4, opt-in)

The risk is a mixed lobby. A vanilla client that loads an enhanced chest drops the extra columns and clamps the
stacks in its local copy. If it ever writes that copy, the items are really gone. The design closes every way that
can happen:

1. **The opt-in is per chest.** An admin enables `CompanionContainerMode = Enhanced`. A companion player then chooses
   **Enhance** on a chest they can access (in the toolbar) and confirms "Console and vanilla players will not be able
   to open this chest". `AllPlayerBuilt` scope is available but not recommended on crossplay servers.
2. **Vanilla requests are vetoed at the relay.** Every client-to-client routed RPC passes through the server. The
   prefixes on `ZRoutedRpc.HandleRoutedRPC` and `RouteRPC` already exist for `RPC_RequestOwn`
   (`WaterBuoyancyEngine.cs:506-534`). They drop a **non-companion** sender's open, stack and take-all requests aimed
   at an enhanced chest (exact method names: §11.2 and §11.3) and send `PlayerNotify.Toast`: *"This chest is
   enhanced: it needs Wonderland Companion (PC)."*
3. **Ownership is pinned away from vanilla peers.** `ZdoSetOwnerPatch` gains `ContainerSpec.ShouldBlockOwnerChange`:
   an enhanced chest is never handed to a non-companion peer, whether by `ReleaseNearbyZDOS` or by anything else. It
   stays with the server or with a companion peer. Reliable open (§4.3.3) is what lets companion players open
   server-owned chests.
4. **Columns first (4a), stacks second (4b).** Columns are a pure grid change: the server's scratch `Inventory` just
   gets the wider width, and the companion widens the container panel (a layout change, §11.10). Stacks are harder:
   `Inventory` reads `m_shared.m_maxStackSize` directly when it adds and stacks (§11.5), and `m_shared` is shared by
   every inventory, including the player's. Container-scoped stacks therefore need a scoped Harmony patch keyed on
   the `Inventory` instance, on **both** sides (server scratch inventories and the companion's container
   inventories), plus a split-on-withdraw rule so a stack bigger than vanilla never reaches a bag. That is real work
   with real risk, hence the separate sub-phase.
5. **Not in scope:** bigger stacks in the player's own inventory. That is the classic "stack size mod". It is
   client-authoritative, it collides with vanilla clients on every hand-over (dropped stacks, trades), and the
   README's position that Wonderland does not boost stack sizes stays true for bags.

---

## 5. Vacuum: speed and reliability

### 5.1 What the companion fixes

From `VacuumEngine`'s settle-step notes and the CHANGELOG:

- A ground stack owned by a client has to be **claimed** (`SetOwner` + revision +4096 + `ForceSendZDO`) and held for
  `HoldSeconds` = 1 s before it is moved, because the server cannot veto a client's `Humanoid.Pickup` (7397), which
  adds to the inventory before the destroy leaves.
- **Known residual:** a partial manual pickup inside the ~100 ms before the claim arrives duplicates the part taken.
- Latency is up to `VacuumInterval`, plus the 1 s hold, plus the at-rest gate (0.10.5).
- The only settings are admin exclusion lists by prefab name. A player cannot say "this chest collects everything" or
  "leave this chest alone".

### 5.2 Owner-originated hand-over (closes the pickup duplication)

This is the `HullBorrow` principle applied to ground stacks: the hand-over starts at the owner, so its final packet is
the last word.

```
server: VacuumAround finds stack S (owner = companion peer P) wanted by chest C
server ── Push ReleaseOffer{S, C} ─────────────────────────────► P
P: if the local player is not picking S up (not mid-Interact, not in the auto-pickup queue):
     S.zdo.SetOwner(serverSession); ZDOMan.ForceSendZDO(serverPeer, S)    // P stops simulating S
P ── Request ReleaseAnswer{S, released: true, dataRevision} ───► server
server: move S into C the frame its own copy shows owner == server at a revision ≥ the answer's
        (the ZDO packet and the RPC can arrive in either order; wait up to 1 s, then fall back to today's claim path)
P refuses (player is on it): server files S like _movingThisPass and tries again next pass.
```

- No hold and no ghost. `ClaimEcho` stays as the backstop. P never picks up a stack it gave away, and if the player
  presses E afterwards, vanilla `RequestOwn` goes to the server, and `HandlePickupRequest` still lets the player win
  every tie.
- Stacks owned by vanilla clients keep the existing settle path, unchanged. Only the stack's owner matters. In
  practice a companion player's own mining, kill and drop spawns are owned by their client, so most stacks around
  them take the fast path.

### 5.3 Instant trigger

The companion batches the ZDOIDs of item drops its client creates (every 250 ms, rate-limited) into `VacuumNudge`.
The server queues `VacuumAround` at those positions. That method already exists and already runs right after every
auto-harvest sweep. Together with §5.2, drop to chest is about two round trips instead of up to `VacuumInterval` + 1 s.

### 5.4 Per-chest settings (`ContainerPrefs`)

| Pref | Values | Default (today's behaviour) |
|---|---|---|
| `Vacuum` | on / off | on |
| `VacuumMode` | MatchOnly / Filter(list) / CatchAll | MatchOnly |
| `VacuumRadius` | ≤ the admin's `VacuumRadius` | the admin value |
| `CraftSource` | on / off | on |
| `SupplySource` | on / off | on |
| `Enhanced` | on / off (Phase 4) | off |

`CatchAll` makes a "sink" chest that takes what no matching chest in reach wants. It ranks below every match. It
breaks the match-required invariant on purpose, so it has to be carried into the names cache: `KnownNotToHold` must
treat a catch-all chest as "wants anything", or the zero-load steady state would skip it. Settings changes come only
from players who can access the chest (§3.4). Vanilla players see the effect (a chest that stops vacuuming), and
optional emote parity can come later.

### 5.5 Feedback

`VacuumMoved` pushes become aggregated toasts (`+12 Wood → Chest (3 m)`), a short client-side pull line from the drop
point to the chest, and an optional radius ring while hovering a chest with a modifier key held. The server-side
splash effect is unchanged for everyone.

---

## 6. Craft from containers

### 6.1 Why the server does the debit

There are two ways to build this:

- **A. The client borrows each chest** through the vanilla open handshake, debits it and saves. This is how most
  existing craft-from-containers mods work. It means several round trips per craft, and it fights Wonderland's own
  writers: a vacuum or supply write that lands between the client's load and save is overwritten by the client's
  higher revision, and an item is lost or duplicated. That is exactly the bug class in §1.3.
- **B. The server debits (chosen).** The server is already the writer of idle chests: the vacuum, supply and sort all
  write through `ZdoInventoryIO.Save`, gated by `s_inUse`. The debit is one more caller of that path, on the
  single-threaded server frame, so it can never interleave with another engine. It also reaches chests that are not
  loaded around the player but are inside the server's range.

### 6.2 Flow (craft or upgrade)

```
UI       : requirements read  Wood 12 (+40 in chests)        ← local read of replicated container ZDOs (§6.4)
press    : the inventory is short by 30 Wood → Request CraftDebit{craft, station, [Wood×30]}
           the vanilla craft timer starts as normal; the bar shows "fetching from chests…"
server   : plan → all-or-nothing → commit debit → escrow #E → Reply Ok{E, taken: chestA×20, chestB×10}
timer end: InventoryGui.DoCrafting → Player.ConsumeResources consumes from the bag ONLY the part not covered by #E
           → Request CraftCommit{E}            (the server deletes escrow #E)
failure  : Insufficient (someone took it) → cancel the craft with a message, nothing debited
           the player aborts or walks off before the timer ends → CraftCancel{E} → the server refunds
```

The round trip hides behind vanilla's own craft duration, so craft speed does not change. Upgrades use the same path:
the item being upgraded stays in the bag, and only the materials come from chests.

### 6.3 Server-side debit

1. **Sources:** containers within `CompanionCraftRange` (default 20 m, max 50) **of the station**, or of the player
   for recipes and pieces with no station. They must be player-built, have `CraftSource` on, be accessible (§3.4),
   not be busy, and not be a hull a client is simulating. Chests the names cache proves lack the item
   (`VacuumEngine.KnownNotToHold`) are not loaded at all. Each candidate is loaded once per request, the same rule as
   `ProductionSupplyEngine.BeginVisit`.
2. **Eligible stacks:** only stackable materials. Never equipment or anything with `m_maxStackSize == 1`, and never a
   stack with quality > 1 or crafter data. A spare sword in a chest can never be eaten as a material.
3. **Order:** nearest chest first, and within a chest the smallest stack first, so the debit also consolidates.
4. **All or nothing:** sum across sources first. If the total is short, reply `Insufficient{have[]}` and write
   nothing.
5. **Commit:** remove from each scratch inventory, `ZdoInventoryIO.Save` each one, record the ledger, write the
   escrow to the journal, then reply. All of it happens in one frame.

### 6.4 Display (client)

The companion parses `s_items` straight from the container ZDOs its client already has replicated in range, using a
scratch `Inventory` with lossless sizes the same way `ZdoInventoryIO` does. It sums by item name and feeds the totals
into the requirement UI and `HaveRequirements` (patch points: §11.6). It filters with vanilla's client-side
`PrivateArea` access check and privacy rules. Display is advisory: the debit is the authority, and a stale count can
only produce a polite `Insufficient`.

### 6.5 Escrow: what can go wrong

| Situation | Outcome | Why it is acceptable |
|---|---|---|
| Normal craft | Commit, escrow deleted | — |
| The player cancels or walks off | Cancel → refund to the source chest, else a sibling, else `ItemCache` (the existing `GridGrowth` fallbacks) | No loss |
| Disconnect or crash before commit | After `CompanionEscrowSeconds` (default 20 s), or immediately on `ZNet.Disconnect` → refund + audit line | **Fails toward no loss**, the same ethos as `ItemCache`. The only duplication: the craft completed *and* the connection died within the same instant, before the commit (ordered and reliable, sent right after `DoCrafting`). It is bounded to one craft's materials and audited per player. |
| The server crashes mid-escrow | On boot the journal's unresolved entries go to `ItemCache`, recoverable with `/comehere` | No loss |
| A modified client never commits | Gets its materials back, which it could equally have spawned (bags are client-authoritative in Valheim) | Grants nothing new. `AuditLog` flags a player whose escrows repeatedly expire uncommitted. |

The alternative policy, commit before crafting, swaps that duplication window for a loss window of the same size.
Refund is recommended. See [§10](#10-decisions-for-you).

### 6.6 Building from containers (Phase 3)

Placing a piece is instant (`Player.PlacePiece`), so there is no craft timer to hide a round trip behind. Use
**fetch** instead: when the selected piece's requirements are not in the bag, the companion sends
`CraftDebit{purpose: fetch, reqs × N placements}`, adds the returned items to the bag, and commits immediately. Vanilla
placement then consumes them as normal. It is the same primitive, needs no placement patch, and respects carry weight
and bag space (the client checks both before asking). The same op powers an optional **Fetch** button on the crafting
panel.

---

## 7. Production management

### 7.1 What exists today

- `/nonono` and `/thumbsup` within `ControlRange` toggle the nearest station (`SupplySwitch`, a ZDOID set in a file).
- `ProductionSupplyReserve` and `KilnWoodTypes` are global.
- Feedback is a toast. There is no way to see why a station is idle.

### 7.2 What the server reports

`StationStatus { enabled, fuelOn, oreOn, fuel/maxFuel, queued/maxOre, inputs[], lastDecision, lastFedAt, sources }`
is read from the same ZDO fields the engine already reads (`s_fuel`, `s_queued`, `item0..N`). `lastDecision` is new: a
small per-ZDOID record written in `ProcessFireplace` / `ProcessSmelter` that keeps the `Delivery` mode or Skip reason
and the time. It turns today's verbose-log-only reasons into player-facing text: *switched off · no coal within 15 m ·
kiln: Fine wood not allowed · owner loading in · fed 4 s ago via owner*. Status is cached for 2 s per station.

### 7.3 Server side: `StationPrefs` generalises `SupplySwitch`

| Pref | Effect in `ProductionSupplyEngine` | Default |
|---|---|---|
| `Enabled` | Today's on or off switch. Emotes still flip it. | on |
| `FuelOn` / `OreOn` | Feed fuel and ore separately. | on / on |
| `AllowedInputs` | Intersects the station's own conversion list. Overrides `KilnWoodTypes` for this kiln. | all (kiln: `KilnWoodTypes`) |
| `Reserve` | Per-station override of `ProductionSupplyReserve`. | global |
| `FillTarget` | Keep the queue at ≤ N rather than at `m_maxOre`, so one smelter does not drain a shared chest. | max |
| `Sources` | Any chest in range, or linked chests only (`LinkSource`). | any |

Storage moves to `Wonderland.StationPrefs.<world>.dat`, with the same temp-then-rename and prune-on-save approach as
`SupplySwitch`. On first load `Wonderland.SupplyOff.<world>.dat` is migrated (`Enabled = false` for each line) and
kept as a `.migrated` backup. `SupplySwitch.IsOff` stays as a façade, so the engine changes are a small set of reads.

### 7.4 Client

- **Hover line** on smelter-family pieces (smelter, blast furnace, kiln, windmill, spinning wheel, eitr refinery, all
  `Smelter` components) and fireplace-family pieces: `Wonderland: ON · coal ✓ · copper ← 2 chests  [Shift+E] toggle
  [Shift+R] settings`. Keybinds are in local config.
- **Settings dialog for one station:** fuel and ore toggles, allowed-input checkboxes built from the station's own
  `m_conversion` list (the client has the prefab), reserve, fill target, source mode, and "link chests" mode, where
  you hover a chest and press a key to toggle it.
- **Production panel** (hotkey): every station within 32 m with its status line, plus *all off*, *all on* and *kilns
  off* in range (`SetStationsInArea`).

### 7.5 Later, and not companion-only

Auto-collecting outputs from beehives, sap extractors and fermenters into chests could be built server-side at the
ZDO layer, as auto-harvest is. Vanilla players would get it too, and the companion would only add a per-station
switch. It is noted here because "etc etc" in the request may well mean it, but it is a separate server feature.

---

## 8. Server configuration (new section `19 - Companion`)

All of these are server-side and reach companion clients in `Welcome` and `Config` pushes, **not** through
ServerSync. They are read at use time, so hot reload works the same way it does for the rest of the file.

| Key | Default | Range | Notes |
|---|---|---|---|
| `CompanionEnabled` | true | — | Whether to advertise. The methods are always registered (§1.1). |
| `CompanionCraftFromContainers` | true | — | |
| `CompanionCraftRange` | 20 | 1–50 | Measured from the station, or the player if there is none. |
| `CompanionEscrowSeconds` | 20 | 5–120 | |
| `CompanionVacuumRelease` | true | — | §5.2 |
| `CompanionVacuumNudge` | true | — | §5.3 |
| `CompanionStationControls` | true | — | §7 |
| `CompanionContainerMode` | RowsOnly | RowsOnly / Enhanced | §4.4 |
| `CompanionEnhancedScope` | PerChest | PerChest / AllPlayerBuilt | |
| `CompanionEnhancedExtraColumns` | 2 | 0–4 | Phase 4a |
| `CompanionEnhancedStackMultiplier` | 1 | 1–4 | Phase 4b. 1 means off. |
| `CompanionRequestRate` | 20 | 5–100 | Requests per second per peer, burst ×2. |

---

## 9. Rollout

Each phase ships as a pair, a server minor version and a companion version, and each one ends with a live boot in the
style of the HANDOFF test plans.

| Phase | Server | Companion | Contents | Size | Risk |
|---|---|---|---|---|---|
| 0 | 0.11.0 | 0.1.0 | `CompanionProtocol`, advertise, hello, welcome, dormancy, `CompanionPeers`, rate limits, config section. No features. | S | Low |
| 1 | 0.11.x | 0.2.0 | Production: status, hover, hotkeys, panel, `StationPrefs` + migration, `lastDecision`. Containers: the `ContainerSpec` refactor (no behaviour change), rows without an anchor, lossless load, reliable open, sync highlight. | M | Low |
| 2 | 0.12.0 | 0.3.0 | Craft from containers (craft + upgrade), `CraftDebit`, escrow journal, ward and privacy checks, ledger and BarrkBOT counters. | L | Medium |
| 3 | 0.13.0 | 0.4.0 | Vacuum: release hand-over, nudges, `ContainerPrefs` incl. CatchAll, feedback. Building from containers via fetch. | M | Medium |
| 4a | 0.14.0 | 0.5.0 | Enhanced chests, columns: per-chest flag, relay veto, ownership pin, wide panel. | M | Medium–high |
| 4b | 0.15.0 | 0.6.0 | Enhanced chests, container-scoped stacks on both sides. | L | High |

**Phase 0 acceptance (the gate for everything else):**
1. A vanilla client on a 0.11.0 server: identical logs and behaviour to 0.10.11, except one `WLC_Advertise` per join.
   No error on the client.
2. A companion client on a 0.11.0 server with enforcement **on**: not kicked.
   `[Companion] 'Name' (PC) companion 0.1.0, protocol 1, features …` appears in the log.
3. A companion client on a **0.10.11** server with enforcement on: not kicked, sends nothing, logs "no Wonderland
   advertise; dormant".
4. A companion client on a vanilla server and in singleplayer: dormant, with no patch side effects.
5. `CompanionEnabled = false` hot-edited: new joins get no advertise, and companion clients already in the session get
   `Config{features: none}`.
6. A forged `WLC_Request` from a peer that never sent a hello: ignored, one audit line, not kicked.

**Docs to change when Phase 1 ships:** the README tagline ("No client install, ever.") becomes "No client install
required, ever. An optional companion for PC players adds…", plus a Companion section and a second manifest. The
enforcement section gains one line: the companion is the one client mod a Wonderland server accepts.

---

## 10. Decisions for you

These are the choices that change what gets built. Each has a recommendation, so the design can go ahead on defaults
if you have no preference.

1. **Enhanced chests in mixed lobbies.** The options: (a) per-chest opt-in, with vanilla and console players locked
   out of those chests only (**recommended**); (b) every player-built chest, which shuts out every console player;
   (c) never ship Phase 4 and stop at rows.
2. **The escrow failure policy** (§6.5). Refund when the commit is missing (**recommended**: never loses, with a
   bounded and audited duplication window), or consume (never duplicates, with a loss window of the same size).
3. **Stack sizes.** Container-scoped only (**recommended**, Phase 4b), or not at all. A global or bag stack size is
   out of scope for the reasons in §4.4.5.
4. **Where the companion lives.** This repository, as a second project with a shared protocol file
   (**recommended**), or a separate repository that vendors `CompanionProtocol.cs`.
5. **The enforcement stance.** The companion is the only accepted client mod (**recommended**, and how the current
   code behaves), or a future admin allowlist for other mods. The allowlist is out of scope here, because
   enforcement identifies frameworks, not mods.

---

## 11. Verify before coding

These are facts about the vanilla **client** assembly that this design relies on and that this repository does not
yet cite. Each one should be checked against `libs-Tools/1.0/DECOMPILED` (client) before the phase that needs it.

1. **(P0)** The client's `ZRoutedRpc.HandleRoutedRPC` ignores a global routed RPC with an unregistered hash without
   logging or erroring. That is what makes `WLC_Advertise` harmless to vanilla clients.
2. **(P1)** The container open path: `Container.Interact` → `ZNetView.InvokeRPC("RequestOpen", playerID)` to the owner
   → the owner's `RPC_RequestOpen` (the in-use check, `ForceSendZDO` + `SetOwner(requester)`) → `OpenRespons`. Confirm
   the registered name (the enforcement whitelist lists `RPC_RequestOpen` and `RPC_OpenResponse`).
3. **(P4)** 1.0's stack-to-nearby-containers and take-all paths (`RPC_RequestStack` / `RPC_StackResponse` /
   `RPC_RequestTakeAll` are on the whitelist): which machine runs them, and whether a vanilla **owner** saves its
   clamped copy. These are the veto targets for enhanced chests.
4. **(P1)** The `Inventory.Load` drop and clamp sites (client 67784 / 68700 / 68817, as cited in `GridGrowth`), and
   where `Container.Awake` builds its `Inventory` (to size it before the first load).
5. **(P4b)** Every `Inventory` member that reads `m_shared.m_maxStackSize` (AddItem overloads, CanAddItem,
   FindFreeStackSpace, stack merge, `Inventory.Load`), for the instance-scoped stack patch.
6. **(P2)** The craft pipeline: `InventoryGui.OnCraftPressed` → `m_craftTimer` → `UpdateRecipe` → `DoCrafting` →
   `Player.ConsumeResources`; the `Player.HaveRequirements` overloads; `InventoryGui.SetupRequirement` (the count text);
   the upgrade branch.
7. **(P3)** Build: `Player.PlacePiece`, `HaveRequirements(Piece, RequirementMode)`, `ConsumeResources` for pieces.
8. **(P2)** The `PrivateArea` ZDO layout for a server-side ward check (`s_enabled`, `s_creator`, the permitted list
   keys, and the radius per prefab), plus `Container.m_privacy` semantics (Private / Group / Public) with `s_creator`.
9. **(P3)** Whether a client can call `ZDO.SetOwner(serverSession)` + `ZDOMan.ForceSendZDO(serverPeer, id)` on a
   ground item it owns, and whether the server applies the owner change. The `HullBorrow` notes indicate
   `RPC_ZDOData` applies the owner with newer data. Confirm the client sends it promptly.
10. **(P4a)** The container panel layout in `InventoryGui` (the `ContainerGrid` ScrollRect, element spacing) for a
    width above 8.
11. **(P1)** `Smelter.GetHoverText` and `Fireplace.GetHoverText` (and any 1.0 hover text for windmill and spinning
    wheel) as the append points for the status line.

---

## 12. Non-goals

- Anything the server would not otherwise allow: no item spawning, no recipe unlocks, no bypassing station or tier
  rules. `EquipmentGuard` and the progression ledger apply to crafted output exactly as before.
- Bigger stacks in the player's bag, magnet auto-pickup radius, carry weight. Carry weight already exists server-side
  through `CarryWeightMultiplier`.
- Console or crossplay console support. They cannot mod; the vanilla path is their experience.
- Using ServerSync, Jotunn or asset bundles in the companion.
- Replacing any vanilla-client feature. Emotes, anchors and the settle path all stay.
