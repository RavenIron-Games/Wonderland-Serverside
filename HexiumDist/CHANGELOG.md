# Changelog

## 0.10.11

### Fixed
- **A production-supply station visit loaded every chest in range once per fuel and once per ore, found or not** (`Subsystems/ItemFlow/ProductionSupplyEngine.cs` `BeginVisit`/`VisitContainers`/`SharedNameOf`, `TryConsumeOne`; `Subsystems/ItemFlow/VacuumEngine.cs` `KnownNotToHold` + `RememberContainerNames` made public). With the sweeps bounded (0.10.9) the profiler still showed `slow frame 117 ms - Wonderland 93.2 ms: ItemFlow 89.4 | players 3` every few seconds, each beside a `[ProductionSupply] smelter ... <- CopperOre via rpc` line. `TryConsumeOne` did its own `FindNear` and deserialised every container in `ProductionSupplyRange` until it found the item - and a smelter asks for its fuel and then for each of its conversions in turn (copper, tin, iron, silver, black metal, ...), so for every ore the base does not have it read every chest again; a smelter beside 30 chests was ~180 inventory loads in one frame, and the sweep budget cannot help because it is checked between visits, not inside one. Now a visit finds its containers once, loads each at most once (fuel and ore from the same chest reuse the loaded inventory; the second save writes the mutated one), and does not load at all a chest whose contents the vacuum's names cache already has at that chest's current `DataRevision` and which lacks the item or holds no more than the station's reserve (`KnownToHoldAtMost`, by the item's shared name, resolved once per prefab; the cache now keeps counts, not just names); every load and every save records the contents for the next visit. Every wall, floor and rock in the ring is rejected by a prefab-hash set (`ContainerRegistry.IsContainerPrefab`) rather than two native `GetComponent` calls each, a container's prefab, template and hull-or-not are resolved once per visit, and a cached inventory is trusted only at the revision it was read at. The cache is reset at the start of each station visit, so a chest emptied or filled between visits is read afresh. Reviewed against the decompile (`Inventory.Load` -> `ItemDrop.Awake` sets `m_dropPrefab` and `m_shared` from ObjectDB, so shared name and prefab name are consistent; equal `DataRevision` means identical bytes): no loss or duplication path. Steady state in a base that never changes: a visit is one sector query and zero loads; the chest being fed from is loaded once per visit.

- **Hull borrow hardened after review** (`Core/Data/HullBorrow.cs`; `Subsystems/Security/ItemIntegritySweep.cs`, `Subsystems/WorldGovernor/StructureUpkeep.cs`). A decompile-cited review of the live 0.10.10 borrow found no loss or duplication path and three edges, all closed: (1) a hand-over arriving after the 2 s timeout used to leave the hull server-owned until vanilla's next `ReleaseZDOS` pass (up to 2 s of dead-reckoning for the owner) with the write never made - a request now stays known for 10 s more and a late grant is written and handed back the frame it lands; (2) the hand-over packet is the owner's last word only if its `DataRevision` is above the server's copy (`RPC_ZDOData` applies the ownership either way, the data only when newer), so nothing on the server may bump a client-simulated hull between request and grant: structure upkeep no longer writes `s_health` on one (it repairs it through the owner's `RPC_Repair` instead, see the boat bullet), the integrity sweep no longer writes `s_items` on one - it judges the server's copy quietly and, when a removal is due and correction is on, borrows the hull and runs the check again on the copy that arrives (audit line, removal and write as the owner; detect-only mode needs no write and is unchanged) - and a grant whose revision has not moved is handed back unwritten and logged; (3) the borrow's docstring said the hull "holds still" - it dead-reckons at its last synced velocity (`ZSyncTransform.ClientSync`), under 10 cm for the round trip at the 1 m/s gate. One request per hull per minute now lives in `HullBorrow` itself, whoever asks; the rows engine's own retry table is gone.
- **Boats did not self-repair unless beached; boats now have their own upkeep loop, rate and section, and it works at sea** (`Subsystems/WorldGovernor/StructureUpkeep.cs` `UpdateBoats`/`RepairBoat`; `18 - Boat Upkeep` / `BoatUpkeepEnabled` default true, `BoatUpkeepInterval` default 60 s, both read at use time). Structure upkeep used to skip a ship while `s_user` was set or either ZDO velocity field was above 0.03 m/s - a bar every floating hull clears, frozen at the last bob on a hull the crew left, and `s_user` stays set after a crash at the tiller. A moored boat next to its crew was never repaired, a boat under attack at sea never; only a beached one was. Ships are now out of the structure loop entirely and on a loop of their own: every `BoatUpkeepInterval` a pass looks around each connected player (`StructureUpkeepPlayerRadius` - the pilot is always inside it of their own hull) and grants a world sweep of the four hull prefabs that runs within `SweepBudgetMs`; there is no rudder or speed gate, so a damaged boat is repaired wherever it is, under sail or under a serpent, the same as a wall a troll is hitting. How: a hull a client is simulating gets vanilla's own `RPC_Repair` sent to that owner (its `WearNTear` sets full health on its authoritative copy and broadcasts `RPC_HealthChanged`; no effects, no player involved) and no server-side write - which was lost on such a hull anyway and, since 0.10.10, would bump the copy `HullBorrow` relies on; a hull nobody simulates is written by the server as before. Each repair logs `[BoatUpkeep] 'Karve' at (x, y, z): 312 -> 500 through its owner (near player)`. Set `BoatUpkeepInterval` short for boats that are hard to sink, long for boats that merely do not rot.
- **Ghost items: a stack you picked up stayed in the world, and E did nothing** (`Core/Data/ClaimEcho.cs` new; `Subsystems/ItemFlow/LatePickupPatch.cs`, `WaterBuoyancyEngine.cs`, `VacuumEngine.cs`). Every claim the server makes on an item a client owns - the buoyancy lift, the vacuum's settle hold, a pickup grant - is `SetOwner` + a revision lead + `ForceSendZDO`. When the owning client picked the stack up in the same instant, it destroyed its copy and sent `DestroyZDO`, and the server's packet, already in flight, reached a client that no longer had the ZDO: `ZDOMan.RPC_ZDOData` creates unknown ZDOs (the `m_deadZDOs` guard against this runs only on the server), `ZNetScene` instantiated it, and the client was left with a stack that existed nowhere else, "owned" by the server, which had nothing to hand back - the pickup animation, `RequestOwn` into the void, no dupe, no removal, until the next log-in. The vacuum's hold has the same window and `VacuumInterval` = 1 doubled it. Now every ZDO the mod force-sends is remembered for 15 s, and a client destroy batch naming one is echoed straight back to that client as the same `DestroyZDO` RPC: its own `HandleDestroyedZDO` removes the ghost (`ZNetScene.OnZDODestroyed` drops the instance), a no-op when there is none. Same reliable ordered connection as the stale packet, and a destroyed ZDO is never force-sent again (`AddForceSendZdos` null-checks), so the echo always lands last - and inside the 0.5 s auto-pickup delay a re-created stack gets, which also closes the one way a stale grant could have been picked up twice. Ghosts already on a player's screen from before this build clear on their next log-in. `[ClaimEcho] echoed the destroy of N recently claimed item(s) back to peer ...` when it happens.
- **Items were pulled off raft decks and shorelines into the water** (`Subsystems/ItemFlow/WaterBuoyancyEngine.cs`). The sweep lifted any "waterborne" stack whose height differed from the surface line, in either direction, and re-claimed a client-owned one even when it already sat at the surface: of 547 lifts in one evening on the live world, 181 were stacks *above* the surface (29.8-30.35 m: a raft deck, a beach) being moved *down* to 29.75, and 144 were stacks already at the surface being taken back from the client beside them - each a fresh ownership fight and a fresh ghost window; Stone alone was lifted 227 times. Now only a stack that is actually below the surface line is lifted. A stack at or above it is left with whoever owns it; a client that sinks one gets it lifted on the next tick, as before.
- **The item-integrity sweep was still a one-frame pass** (`Subsystems/Security/ItemIntegritySweep.cs` on `BudgetedSweep`). 0.10.9 converted the four ItemFlow/Storage sweeps and missed this one: 25 chunks every 30 s with every container found loaded in the same frame - `slow frame 122 ms - Wonderland 89.5 ms: Security 89.3`. Same coverage per interval, now spread over the frames that follow within `SweepBudgetMs`. Nothing about what it checks changed.

### Changed
- Live tuning note, not a default change: with the sweeps budget-bound, `ContainerRowsInterval`/`BatchSize` and `ProductionSupplyInterval`/`BatchSize` above the budget's throughput buy nothing (the rows cycle on a 745k-ZDO world is ~5 s at `SweepBudgetMs` 2); the one timing players feel is `VacuumInterval` (the near-player pass), 1 s is a good value.

## 0.10.10

### Fixed
- **0.10.9's forced ship write could lose an item; ship cargo rows are now written by borrowing the hull** (`Core/Data/HullBorrow.cs` new; `Core/Data/ShipAttachment.cs` `CommitAgainstOwner` removed; `Subsystems/Storage/ContainerRowsEngine.cs`, `Subsystems/ItemFlow/SortEngine.cs`, `ZdoSetOwnerPatch` in `Subsystems/ItemFlow/WaterBuoyancyEngine.cs`). 0.10.8 got the server to write the anchor again (`[ContainerRows] 'Karve' at (556, 30, -897): parked $item_boarjerky x6 in row 3 (2 -> 4 rows)`, 80 m from its crew's base) but the write never reached a screen: a hull inside a connected client's active area is owned and simulated by that client, which bumps the ZDO's `DataRevision` a few times every frame (`ZSyncTransform.OwnerSync`), and `ZDOMan.RPC_ZDOData` applies a packet only when its revision is above the one held - so the server's +1 arrived stale at the owner and the owner's next packet, one above the server's, replaced the server's copy. Only a hull nobody was near ever kept its rows. **0.10.9 forced the write through with a revision lead (the vacuum's `DataRevision += 4096`) - withdrawn here**: `Container.SetInUse(false)` does not save, only an inventory change does, so a deposit the owner made in the round trip before the forced write landed was dropped at the server and then reloaded away on the client when the cargo closed - an item lost, or with a withdrawal, duplicated. A write to a ZDO another client is actively rewriting is never safe; the ZDO has to change hands, and the hand-over has to start at the client. Now a client-simulated hull that needs its anchor is **borrowed through vanilla's own container-open handshake**: the server sends the owner `Container.RPC_RequestOpen` (one routed RPC, exactly what a second player's client sends to open someone else's chest); the owner's client refuses while its cargo is open (its own `m_inUse`, the authority), otherwise does `ForceSendZDO(server)` + `SetOwner(server)` itself and from that instant neither simulates nor writes the hull - the packet carrying the ownership change carries its final cargo. The server, now the sole writer, loads that copy, parks the anchor if the copy still needs it, and hands the hull straight back (`SetOwner(owner)` + `ForceSendZDO`), all in the frame the hand-over arrives (`[ContainerRows] 'Karve' ... parked ... on a borrowed hull`). The owner's `Container` reloads on the revision change and vanilla `Container.UpdateRows` grows the grid. On the client the hull holds still for one round trip (`Ship.CustomFixedUpdate` and `OwnerSync` are owner-only) - a tenth of a second's pause in a moored hull's bob; a hull under sail (steered by a connected player, or moving faster than 1 m/s - a sail stays set after the pilot lets go) is never borrowed, and 0.10.8's 0.03 m/s threshold, which every floating hull exceeds, is what had kept a moored ship "underway" for as long as its crew stayed nearby. `ZDOMan.ReleaseZDOS` (every 2 s) would hand a server-owned hull in a peer's area back on its own; the existing `ZDO.SetOwner` prefix holds that off while a borrow is pending, a borrow the owner never grants is dropped after 2 s, nothing is written unless the server owns the hull at the moment of writing, and a hull is asked for at most once a minute (`[ContainerRows] '...' needs its row parked - asked its owner <id> for the hull`). `RPC_OpenResponse` coming back is a no-op on a dedicated server. Sort no longer touches a client-simulated hull at all (a merge is cosmetic); vacuum, production supply and grid overflow still refuse one.

## 0.10.9

### Fixed
- **The server stalled 100-550 ms every 2-5 s, players or not** (`Core/Data/BudgetedSweep.cs` new; `Subsystems/ItemFlow/VacuumEngine.cs`, `Subsystems/Storage/ContainerRowsEngine.cs`, `Subsystems/ItemFlow/SortEngine.cs`, `Subsystems/ItemFlow/ProductionSupplyEngine.cs`; `1 - General` / `SweepBudgetMs` new, default 2, read at use time). The 0.10.8 profiler's first five minutes on the live world (745k ZDOs): `14,165 frames, avg 21.2 ms, 114 over 100 ms | Wonderland max 437.9 ms (ItemFlow)`, with lines like `slow frame 553 ms - Wonderland 407.0 ms: ItemFlow 406.7 | players 2` every 2 s and `slow frame 165 ms - Wonderland 128.1 ms: Storage 127.8` every 5 s - the same with `players 0`, so nothing to do with who is connected. Cause: every world-wide sweep advanced its `PrefabSetScanner` by its BatchSize chunks per interval and then visited everything those chunks found *in the same frame*. One chunk is up to 400 non-empty sectors of **one** prefab (vanilla `ZDOMan.GetAllZDOsWithPrefabIterative`), the vacuum tracks 64 container prefabs, so a pass whose chunks landed on `piece_chest_wood` loaded, ground-queried (a sector query per chest for stacks within `VacuumRadius`), overflow-checked and cache-drained every wood chest in the world at once; container rows did its load-and-anchor pass the same way. Now each sweep is a `BudgetedSweep`: the interval still grants the same BatchSize chunks (coverage rate unchanged, `VacuumBatchSize`/`ContainerRowsBatchSize`/`SortBatchSize`/`ProductionSupplyBatchSize` and their intervals keep their meaning), but the visiting runs on every frame until `SweepBudgetMs` is spent and the rest waits for the next frame. A grant the budget could not finish by the next interval is replaced, not added to, so a world too big for the budget is swept at the budget's pace instead of piling up debt. The queue holds `ZDOID`s, not `ZDO` references - `ZDOPool` recycles a destroyed ZDO object for the next created one, so a reference kept across frames can silently become a different object; each id is resolved through `ZDOMan.GetZDO` when its turn comes. Visitors are cached delegates (no per-frame allocation). The vacuum's near-player pass is unchanged and now runs before the background work each interval rather than after; it judges every container in reach itself, so nothing depended on the old order. Worst case per frame with all four sweeps busy: about 4 x `SweepBudgetMs` plus one container load each.
- **Ship cargo rows: the anchor write is forced past the owning client** (`ShipAttachment.CommitAgainstOwner`: `DataRevision += 4096` + `ZDOMan.ForceSendZDO(owner, uid)` after the rows anchor and a sort merge; `UnderwaySpeedSqr` 0.03 -> 1 m/s). 0.10.8 wrote the anchor but a hull inside a connected client's active area is owned by that client, which bumps the ZDO's revision every frame, so the server's +1 was discarded and the owner's next packet replaced the server's copy - `'Karve' at (556, 30, -897): parked` in the log, never a row on screen. **Withdrawn in 0.10.10, see there: this lead can drop a deposit or withdrawal the owner made in the round trip before the forced write lands.** If you are on 0.10.9, either update or put `Karve, VikingShip, VikingShip_Ashlands` on `ContainerRowsExcludedContainers` (hot-read) until you do.

## 0.10.8

### Fixed
- **Boat storage was no longer expanded** (`Core/Data/ShipAttachment.cs` new `IsUnderway`/`IsConnectedPeer`; `Subsystems/Storage/ContainerRowsEngine.cs`, `Subsystems/ItemFlow/SortEngine.cs`, `Subsystems/ItemFlow/VacuumEngine.cs`). The 0.10.3 ship guard skipped any hull whose ZDO reported `s_user != 0` or a velocity above 0.03 m/s. `ZSyncTransform.OwnerSync` writes those fields only while a client owns the ZDO, and the server's `ZDOMan.ReleaseNearbyZDOS` drops that owner within 2 s of the client leaving, so a moored ship keeps its last bob forever and never passed - `[ContainerRows] x2 rows on 18 of 64 container type(s) ... Karve 2x2->2x4 ... VikingShip 6x3->6x6 ... VikingShip_Ashlands 8x4->8x8` was announced on every boot while the only ship parked in a day was one beached Karve. Now a hull is "underway" only when a **connected** peer owns it and it is steered or moving; with no connected owner nobody is simulating it, the velocity fields are leftovers and the server's write is the only one there is. "Connected owner" also means in range: `ZDOMan.ReleaseNearbyZDOS` only releases what a peer walks away from, so a pilot who portalled or respawned keeps a far hull owned; `ShipAttachment.IsSimulatedByClient` checks `ZNetScene.InActiveArea(hull, owner.GetRefPos())`. A steered hull is one whose `s_user` is a player who is actually connected - `s_user` is only cleared by `RPC_ReleaseControl`, which a crash at the wheel never sends. A hull that is owned but still (docked, calm water) is written once and then left alone for 60 s per ship (`_shipRetryAt`) so a client that keeps advancing the revision cannot make the anchor write loop (the 78 re-parks of one moored Karve on 2026-09-19); expect the rows to appear when the crew has left the hull's area and comes back, since a floating hull's owner rewrites the revision every frame (`Ship.CustomFixedUpdate` wakes the body each step). Sort uses the same rule. **The vacuum, production supply (`ProductionSupplyEngine.TryConsumeOne`) and grid-growth overflow (`GridGrowth.FindNearestSiblingContainer`) refuse any hull a client is simulating whatever its speed** - each of those is a write-then-remove (ground stack destroyed, ore consumed, overflow moved), and a hold write the owner's revision stream discards would leave nothing behind or duplicate it. The 0.10.3 velocity-only guard had been covering the vacuum case by accident; the other two never had a guard.
- **`PositionWatch` flagged everyone at sea as fly/noclip** (`Subsystems/Security/PositionWatch.cs`, `ShipAttachment.IsNearShip`). The height check compared the reported Y against the height map, which is the seabed (0 m in open ocean) while a swimmer or a deck-hand reports Y ~ 30 - 280 `reported Y 29.x is 29.xm above expected ground height 0.0 - possible fly/noclip` lines from one player's sail on 2026-09-20, each a synchronous append to `wonderland_security.log`. The floor is now `max(terrain, ZoneSystem.c_WaterLevel)` and a Ship ZDO within 40 m exempts the sample.

### Added
- **Server tick profiler** (`Core/TickProfiler.cs`, wired in `SubsystemRegistry.OnUpdate`; `1 - General` / `TickProfilerEnabled` default true, `TickProfilerSlowFrameMs` default 100, both read at use time). For the "game stutters while moving items, only with two players connected" report, which nothing server-side explains by reading: a plain inventory move never leaves the client (`Player.OnInventoryChanged` ends in the local `UpdateKnownRecipesList`/`UpdateAvailablePiecesList`), so if the server is involved it is as a stall in its relay. Every frame each subsystem's `OnUpdate` is timed; a frame longer than the threshold logs `[TickProfiler] slow frame N ms - Wonderland n ms: Security n, ItemFlow n | players 2` (one line per second at most), and every 5 minutes `[TickProfiler] 5 min: N frames, avg n ms, max n ms, n over 100 ms | Wonderland avg n ms, max n ms (<subsystem>) | players n`. The frame figure is Unity's whole frame (vanilla + every mod), the Wonderland figure is this mod's share. `Time.unscaledDeltaTime` read in a frame is the length of the frame before it, so the breakdown is double-buffered and a slow frame is reported with the breakdown that actually ran inside it; frames over 10 s (the world-load stall) are left out of the window. Cost: one Stopwatch per subsystem per frame, no per-frame allocation.

## 0.10.7

### Fixed
- **A crossplay server that nobody can join after a restart** (`Subsystems/WorldGovernor/CrossplayLobbyGuard.cs`, new; `WorldGovernorSubsystem.cs`; `Wonderland.csproj` now references the server's `PlayFab.dll`). Vanilla `ZPlayFabMatchmaking` registers the server as a PlayFab lobby with `string_key2 = False`, then checks its join code is unique with `FindLobbies(string_key4 == JoinCode)` and, in `OnCheckJoinCodeSuccess`, activates the lobby (`string_key2 = True`) only when exactly one lobby comes back and `Lobbies[0].Owner.Id` is this server. Every client lookup (join code, host name, IP, server list - client decompile 91234-91345) filters `string_key2 eq 'True'`, and a `-crossplay` server has no Steam listen socket, so a lobby that never activates is a server nobody can join. The join code is the same every boot (`UnityEngine.Random` is re-seeded by world generation before `GenerateJoinCode` - 637457 on every Vanilla Bean boot since 1.0.15) and the shutdown-time deactivate/leave HTTP calls almost never get out (`Deactivated PlayFab lobby` on 1 of 47 shutdowns), so the next boot's check often gets the previous run's orphan lobby back. Once PlayFab has dropped that orphan's owner, `Lobbies[0].Owner` is null: vanilla throws `NullReferenceException` at IL 0x70 (`at ZPlayFabMatchmaking.OnCheckJoinCodeSuccess ... PlayFabUnityHttp.OnResponse`), nothing reschedules the check, `Session ... is active` never logs and `UnregisterServer` later reports `State: Creating`. Live: 2026-09-19 20:02 (0.10.1) and 2026-09-20 11:54, 11:56 (0.10.5), 12:05 (0.10.6) - 4 of the last 8 boots, 0 of the ~40 before; the 11:54 one came 48 minutes after the previous shutdown. Not a Wonderland hook: none of the mod's 25 Harmony targets touch `ZPlayFabMatchmaking`, `PlayFab*` or `Random.InitState`. Now a prefix on `OnCheckJoinCodeSuccess` sends any reply containing an ownerless lobby down vanilla's own `RegenerateJoinCode` branch (new code on our lobby -> re-check -> activate; logs `[CrossplayLobbyGuard] join code N is still held by n ownerless lobby(ies) ... regenerating the join code instead`), and a finalizer turns any other exception in that callback into a logged, rescheduled check (`m_retryIn = 1s`, counted against vanilla's 100-retry budget). New `8 - Player Cap` / `CrossplayLobbyGuardEnabled` (default true, read at use time; does nothing on a Steam-only server). Boot line to look for: `Session "..." with join code N and IP ... is active with 0 player(s)` within a few seconds of `registered with join code`.

## 0.10.6

### Fixed
- **The progression tier table was never written on the live server** (`Subsystems/Security/ItemTierClassifier.cs`, `SecuritySubsystem.cs`). 0.10.5 built the table and wrote `Wonderland.ProgressionTiers.txt` / `Wonderland/progression_tiers.json` only from the world-ready hook, and on the dedicated server `ObjectDB.instance` is routinely still null there (`ObjectDB.Awake` runs after the `ZNetScene.Awake` hook - the same Awake-ordering race recorded for 0.7.x in `libs-Tools/IMPLEMENTATIONS/Wonderland.md`). The 2026-09-20 15:56Z boot logged `[Progression] ObjectDB or ZNetScene not available at world ready`, built the index lazily on the first enforcement query (`Indexed 429 recipes for 428 items and 107 conversions into 93 items; 226 items take an upgrader resource`), and never wrote either file, so the BarrkBOT export carried `items_by_tier_count` all 0 and `gated_items_count 0` and the website listed nothing. Enforcement itself was unaffected (tiers derive on first use). Now `ItemTierClassifier.OnUpdate()` runs every security tick: the index is built the first tick `ObjectDB` exists, and the two files are (re)written whenever the table is stale - after that build, and within a second of a `ProgressionItemExemptions` or `BannedItemsList` edit (previously the files only reflected pins after a restart). The world-ready message is now Info, not Warning, since it is expected. Boot line to look for: `[Progression] Tier table: Cheat n, DeepNorth n, ... - written to <path>`.
- Export/contract/sample wording updated to match (`progression_notes`: "written on the first tick the game's item database exists after a start, and rewritten within a second of a ... edit").

## 0.10.5

### Fixed
- **Dropped items jerking, freezing mid-air, or sinking and re-surfacing while a player stands next to them** (reported against 0.10.4 as "jitter when moving items around"). Three server-side causes, all in `Subsystems/ItemFlow/`:
  - `VacuumEngine.TryTakeOwnership`: the settle-step claim (`SetOwner(server)` + `DataRevision += 4096` + zeroed `s_velHash`/`s_bodyVelHash`/`s_bodyAVelHash`) is now made only once the owning client reports the stack at rest (`|s_velHash|` and `|s_bodyVelHash|` <= 0.05 m/s, `AtRestSpeedSqr`), and only when that owner is a connected peer (a stale velocity on an unowned ZDO, or one left by a peer that disconnected mid-slide, does not gate). Vanilla renders a claimed stack from the ZDO alone (`ZSyncTransform.ClientSync`: lerp to `GetPosition()`, `useGravity = false`, `m_body.Sleep()`), so a stack claimed while still falling jumped back to the server's last-known position and hung there for the 1 s hold. Stacks skipped for motion are tracked in `_movingThisPass` and are not filed as unwanted; they are taken on the next pass, at rest, where the claim is invisible. Side effect: loose items riding a moving ship deck are no longer claimed mid-voyage.
  - `WaterBuoyancyEngine.HandlePickupRequest`: the post-grant grace is back to 3 s (`PickupGraceSeconds`; 0.10.4 had raised it to 15 s). A client's copy of a floating item has no `Floating`, so a granted stack the player could not take (full inventory, over weight, swimming) sank for the whole grace and then snapped back to the surface when the sweep reclaimed it; a real pickup needs one ZDO delivery plus the auto-pickup pull (15 m/s over <= 2 m) plus the `DestroyZDO`, well under a second.
  - `WaterBuoyancyEngine.OnUpdate`: the 0.10.4 "player within 5 m owns it -> leave it and extend grace 10 s" hold-off is removed. Vanilla's `Humanoid.Pickup` is a single-frame call (`CanPickup` -> `AddItem` -> `ZNetScene.Destroy`), so the ghost-duplicate rationale did not apply, and the hold-off meant a sunk stack stayed sunk as long as a player stood beside it and rose again when they walked away. The only way a client owns a waterborne item is having dropped it or been granted it, and a grant carries its own grace.
- **Grant/re-claim loop broken** (`WaterBuoyancyEngine.NoteGrantOutcome`, `_failedGrants`): when the sweep takes a floating item back from a peer that was granted it and did not pick it up within the grace, that counts as a failed grant for that peer. After 2 consecutive failures (`FailedGrantsBeforeCooldown`) that peer's `RPC_RequestOwn` is refused for 20 s (`FailedGrantCooldownSeconds`) and the item stays afloat; other peers are unaffected, and the memory purges 20 s after the last failure. Live 0.10.4 log showed one item lifted/granted 7 times in a row and 13 more lifted 2-4 times while never being picked up; vanilla's `ItemDrop.RequestOwn` backs off exponentially (0.2 s doubling to 30 s, reset only when the client sees itself as owner), so every one of those re-claims also made the player's next attempt slower.
- `[WaterBuoyancy] Lifted ...` now logs the real previous Y (it printed the target twice).

### Changed
- **Progression tiers are derived from the game's data, not a keyword list** (`Subsystems/Security/ItemTierClassifier.cs`, rewritten). The 0.10.1-0.10.4 substring classifier tied biome *access* to boss defeats: with the live cfg at `MaxAllowedTier = Auto` (= Mountain) and `ItemIntegritySweepCorrect = true` it removed 469 stacks from player chests between 2026-09-20 00:12 and 00:58 UTC - `Feathers` x127 (126 chests, the 0.10.2 keyword bug), `BlackMetalScrap` x101, `Needle` x91, `Barley` x70, `LoxPelt`, `Flax`, `YggdrasilWood`, `BlackMarble`, `MarinatedGreens`, `FishSoup` - all obtainable in this world by sailing to the biome, which vanilla never gates behind a boss. The new rule: **raw drops are unrestricted; an item's tier is the highest gated seed or station in the cheapest chain that makes it.**
  - Seeds (`SeedItems`, every name verified against the 1.0.15 asset bundle's ObjectDB): each boss's drop and trophy at the tier that boss unlocks (`CryptKey`/`TrophyTheElder` Swamp, `Wishbone`/`TrophyBonemass` Mountain, `DragonTear`/`TrophyDragonQueen` Plains, `YagluthDrop`/`TrophyGoblinKing` Mistlands, `QueenDrop`/`TrophySeekerQueen` Ashlands, `FaderDrop`/`TrophyFader` DeepNorth - Fader is the Ashlands boss and `defeated_fader` opens the Deep North; the Deep North boss `FrozenKing` leaves `FrozenKingDrop` DeepNorth and has no trophy prefab), plus the ores, ingots and refined stock that only exist behind them: `IronScrap`/`IronOre`/`Iron` Swamp (`IronOre` is a legacy prefab nothing drops, but the smelter accepts it, so it must not be the cheap path to iron), `SilverOre`/`Silver` Mountain, `FlametalOre`/`FlametalOreNew`/`Flametal`/`FlametalNew` Ashlands, `GoldOre`/`Gold` (Petrified Tissue / Bloodgold), `Frostwood`, `NornThread` DeepNorth.
  - Station floors (`StationFloors`): `piece_artisanstation`, `blastfurnace`, `piece_spinningwheel`, `windmill`, `piece_oven` = Plains (all cost or sit at Moder's tear in vanilla); `blackforge`, `piece_magetable`, `eitrrefinery` = Mistlands; `piece_FrostFoundry` (finishes every "Cast: ..." Bloodgold piece), `piece_FrostKiln` = DeepNorth (ledger policy - vanilla builds all of these from the biome's own drops with no boss gate; Ashlands has no station of its own). A conversion with no input item (the Frigid Kiln's `FrozenFuel`) is gated by its station alone.
  - Offline simulation of the exact algorithm over the parsed 1.0.15 recipe/station data (481 recipes, 9 smelters, 4 cooking stations, 1 fermenter, 690 pieces): 1,180 items unrestricted, Swamp 54, Mountain 22, Plains 60, Mistlands 45, Ashlands 56, DeepNorth 100, Cheat 2; with the ledger at Mountain, 261 items are gated.
  - Walked at world start from `ObjectDB.m_recipes` (station + ingredients; cheapest recipe wins; `Recipe.m_requireOnlyOneIngredient` takes the cheapest ingredient), `Smelter`/`CookingStation`/`Fermenter.m_conversion` on the `ZNetScene` prefabs (station + input), and each station's own `Piece.m_resources` / `Piece.m_craftingStation` chain. The only name rule left: a **crafted** item or a station whose prefab name contains `Ashlands` or `DeepNorth` is at least that tier (`NameFloors`); drops are never judged by name.
  - Results with the ledger at Mountain: `SwordBlackmetal` Plains (via `BlackMetal` at the blast furnace), `ArmorPaddedCuirass` Plains (`LinenThread`), `Bread` Plains (`BarleyFlour` at the windmill), `SwordMistwalker`/`CapeFeather` Mistlands (black forge / `Eitr`), `ArmorAshlandsMediumChest` Ashlands, `CapeDeepNorth` DeepNorth; `BlackMetalScrap`, `Needle`, `Barley`, `Flax`, `LoxPelt`, `WolfPelt`, `Obsidian`, `Magecap`, `YggdrasilWood`, `BlackMarble`, `Carapace`, `HelmetDverger` (Haldor), `SilverNecklace`, all trophies and all cauldron food unrestricted. `BlackMetal`, `Eitr`, `LinenThread`, `BarleyFlour` stay gated as processed materials.
  - **Audit file**: `BepInEx/config/Wonderland.ProgressionTiers.txt` is rewritten at every world start - one line per ObjectDB item, highest tier first: `Tier<TAB>Prefab<TAB>reason` (e.g. `Plains	SwordBlackmetal	needs BlackMetal (smelted at blastfurnace (blastfurnace is a Plains station))`), with the seeds, floors, current ledger and per-tier counts in the header. Boot log: `[Progression] Tier table: Cheat n, DeepNorth n, ... - written to <path>`.
  - `ProgressionItemExemptions` now overrides the derived table in both directions: `Prefab` = unrestricted, `Prefab:Tier` = pinned to that tier (raise or lower); hot-reloaded, drops every memoised tier on change; an unknown tier name logs a warning and is treated as unrestricted. The old built-in exemption table is gone (nothing in it has a recipe, so it derives as unrestricted anyway). Flag lines from the sweep now carry the reason: `item 'X' (Tier: Plains - needs BlackMetal (...)) exceeds server progression ceiling 'Mountain'`.
- **"Impossible quality" respects Valheim 1.0's upgrader station** (`ItemTierClassifier.CanExceedMaxQuality`, used by `EquipmentGuard` and `ItemSanityGuard`): the `UpgradeStation` ("Forge of Potential", the only `CraftingStation.m_upgrader` in 1.0.15) upgrades an item past `m_shared.m_maxQuality` when its recipe carries a `Piece.Requirement.m_upgraderResource` (`InventoryGui` decompile 51005-51027) - in practice one of the `Upgrader0..7Armor`/`Upgrader0..7Weapon` idols, present in 271 of the 429 enabled recipes; an idol among the resources is accepted as well as the flag. The world key `GlobalKeys.NoCraftCost` lifts the cap for everything. Such items are exempt from the quality check in both the equipment scan and the container sweep. Live 0.10.4 flagged a PlayStation player (no mods possible) at quality 10 on `CapeDeepNorth` and `THSwordGold_BloodLightning`; with `EquipmentGuardKick = true` that would have kicked them.
- **The standalone Eitr check in `EquipmentGuard` is removed.** An Eitr bar comes only from food, and every Eitr food is cauldron-cooked from raw Mistlands pickables, which the rule above leaves unrestricted; magic staves and robes are still caught as equipped items by their recipe tier.
- `EquipmentGuard.OnPeerDisconnected` keyed its anti-spam cache on `ZDOID.UserID` (the per-session `Utils.GenerateUID()`), never matching the scan's `s_playerID` key, so a player's flagged-item memory was never cleared on disconnect; it now keys on `ZNetPeer.m_playerID`.

### Added
- **BarrkBOT export: `progression` and `enforcement` blocks** (`Subsystems/BarrkBot/BarrkBotExport.cs`, `BARRKBOT_CONTRACT.md`; `schema_version` stays 3, additive, written after `lifetime_notes` on every `BarrkBotWriteSeconds` cycle, all values read live from the synced config and the world's global keys):
  - `progression`: `mode` (`auto`/`fixed`/`off`), `configured` (raw `MaxAllowedTier`), `tier`, `tier_index` (Meadows 1 … DeepNorth 8), `tiers`, `unlocked_by` (tier → boss), `next_boss`/`next_key`/`next_tier` (`""` when nothing lifts the ledger), `bosses_defeated_keys`, `gated_tiers`, `gated_items_count`, `gated_items_by_tier`, `items_by_tier_count` (`Unrestricted` … `Cheat`, every key present), `tier_table_file`, `tier_json_file`. Item names are deliberately not in this file (they would push the block past BarrkBOT's local-provider 6,000-character slice); the full table is written beside it as `BepInEx/config/Wonderland/progression_tiers.json` (`{generated_at, source, ledger, tiers, seeds, station_floors, name_floors, items: {prefab: {tier, reason}}}`, not a `barrkbot_*` name).
  - `enforcement`: `container_sweep`, `container_sweep_removes`, `equipment_guard`, `equipment_guard_kicks`, `quality_guard`, `admin_bypass`, `vanilla_client`, `vanilla_client_kicks`, `vanilla_client_admin_bypass`, `strict_version`, `active_probe`, `routed_rpc`, `placement_guard`, `max_plant_batch`, `banned_items`, `pinned_items`. No per-guard counters; violations stay in `lifetime.security_flags_total_alltime`.
  - `progression_notes` / `enforcement_notes` are single strings like every other `_notes` key (the BarrkBOT reader lifts only string notes as guidance). `server_notes` now says seven bosses. Samples in `tools/barrkbot/` regenerated; verified with `barrkbot-render-probe.mjs` (BarrkBOT 6.1.20).

## 0.10.4

### Fixed
- **Duplicate "Ghost" Item & Shoreline Pickup Desync Elimination**:
  - Fixed an issue where items (especially items in water, harvested near shorelines like Thistles, or dropped) were picked up into the player's inventory yet remained stuck on screen as floating duplicate "ghost" items that could not be picked up.
  - **Root Cause**: Vanilla Valheim clients require local ZDO ownership (`zDO.IsOwner()`) at the exact instant `ZNetScene.Destroy(go)` runs to send `ZDOMan.DestroyZDO` to the server. If the server claimed ownership during buoyancy sweeps (`WaterBuoyancyEngine.OnUpdate` every 0.3s) while the client was executing `Humanoid.Pickup`, the client added the item to inventory and destroyed its local GameObject, but sent no destroy packet to the server. The server's surviving ZDO was subsequently replicated back to the client as an un-pickable ghost duplicate. Furthermore, if a client sent `RPC_RequestOwn` to an old owner or another peer, the server forwarded the packet via `ZRoutedRpc.RouteRPC`, bypassing `HandleRoutedRPC` and dropping the request if that peer was offline or not the owner.
  - **Player Proximity Buoyancy Guard**: `WaterBuoyancyEngine.OnUpdate` now includes an active player proximity check. If any connected player is within 5.0m of an item and currently owns it (e.g. freshly picked or dropped), the server will not steal ownership, granting an automatic 10-second pickup grace period.
  - **Authoritative Pickup Grant & 15s Grace**: `HandlePickupRequest` now unconditionally grants a generous 15-second pickup grace, advances `OwnerRevision` and `DataRevision` (+4096), and authoritatively transfers ownership to the requesting peer regardless of whether another client or an offline session was previously recorded.
  - **Universal Routed RPC Route Interception (`RoutedRpcRoutePatch`)**: Added a Harmony prefix on `ZRoutedRpc.RouteRPC` to intercept any `RPC_RequestOwn` routed across peers, ensuring 100% of all player pickup requests are authoritatively processed by the server without packets getting lost or dropped.

## 0.10.3

### Fixed
- **Permanent Upkeep & Boat Repair Hold (`RPC_Repair` Synchronization)**:
  - Fixed an issue where repaired boats and structures visibly healed to full health, but upon taking damage immediately reverted back to their previous pre-repair health.
  - **Root Cause**: Vanilla clients simulate piece and boat damage locally in `WearNTear.ApplyDamage`, which reads health from the client's local in-memory ZDO. Previously, `StructureUpkeep` only routed `RPC_HealthChanged` to clients; in vanilla, `RPC_HealthChanged` only updates the visual mesh and `m_healthPercentage` without updating the client's local ZDO. When the object was hit, `ApplyDamage` read the client's stale un-repaired ZDO value and immediately snapped the health back to the old damaged value.
  - **Resolution**: `StructureUpkeep` now routes vanilla's native `RPC_Repair` to the ZDO's owning client alongside `RPC_HealthChanged`. The owning client executes `m_nview.GetZDO().Set(ZDOVars.s_health, m_health)`, authoritatively committing full health into its local ZDO. Subsequent hits now correctly subtract damage from full health instead of reverting.

## 0.10.2

### Added / Improved
- **Item Exemption List & Misclassification Guard (`ProgressionItemExemptions`)**:
  - **Dual-Layer Exemption Architecture**: Core craft materials, utility items, and early treasures are exempt from progression gating or assigned appropriate baseline tiers before any keyword classification runs.
  - **Vanilla Material Safety**: Resolves an issue where common base items like `Feathers` were erroneously classified as `Mistlands` (due to colliding with Feather Cape keywords). `Feathers`, `BoneFragments`, `Resin`, `Flint`, `Wood`, `Stone`, `HardAntler`, `QueenBee`, `LeatherScraps`, and `DeerHide` are protected as baseline Meadows items.
  - **Dungeon & Trader Valuables**: Treasures legitimately found in Black Forest crypts and Haldor merchant wares (`SilverNecklace`, `Coins`, `Ruby`, `Amber`, `AmberPearl`, `FishingRod`, `FishingBait`, `Megingjord`, `YmirRemains`) are categorized at Black Forest/Meadows, preventing false-positive Mountain tier violations.
  - **Keyword False-Positive Prevention**:
    - Replaced generic `FEATHER` keyword with explicit `CAPEFEATHER` and `FEATHERCAPE`.
    - Constrained `TAR` keyword matching to avoid capturing names with "tar" substrings like `Starter...` or `Target`.
    - Constrained `TIN` keyword matching to avoid capturing English verbs ending in `-ting` (`Crafting`, `Hunting`, `Planting`, etc.).
    - Explicitly mapped `Wishbone` to Mountain tier before Black Forest bone checks.
  - **Configurable Overrides**: Administrators can whitelist modded prefabs or override tier mappings live (`PrefabName` or `PrefabName:Tier`) via server-synced `ProgressionItemExemptions` with immediate in-memory cache invalidation.
- **Authoritative Admin Freedom & Progression Bypass (`EquipmentGuardAdminBypass = true`)**: Authenticated server admins on `adminlist.txt` are **100% exempt** from all equipment, cheat item, impossible quality, Eitr magic, and progression tier checks. Admins can wield, wear, spawn, or test whatever gear they want, at any time, with zero interference, flags, or kicks. Containers created by server admins are also exempt from container integrity sweeps, ensuring admin storage is never touched.

## 0.10.1

### Added
- **Automatic World Progression Tier Ledger (`MaxAllowedTier = "Auto"`)**:
  - Automatically ties server gear progression ceilings and anti-cheat kicking directly to defeated world bosses via Valheim's persistent world global keys (`ZoneSystem.instance.GetGlobalKey("defeated_<boss>")`).
  - **Progression Flow**:
    - **Baseline (Day 1 / Pre-Elder)**: Defaults to **`BlackForest`** (Eikthyr defeat is ignored because the starter grant provides bronze tools and a Karve hull). Bronze, Troll hide, Fine wood, and Core wood gear are permitted; Iron and higher are prohibited.
    - **The Elder Slain (`defeated_gdking`)**: Unlocks **`Swamp`**. Iron weapons, Iron armor, Root armor, Banded shields, and Ancient bark gear are now legitimately permitted; Silver and higher are prohibited.
    - **Bonemass Slain (`defeated_bonemass`)**: Unlocks **`Mountain`**. Silver weapons/armor, Wolf armor, Fenris set, and Draugr Fang are now permitted; Blackmetal and higher are prohibited.
    - **Moder Slain (`defeated_dragon`)**: Unlocks **`Plains`**. Blackmetal weapons & shields, Padded armor, Porcupine, and Lox cape are now permitted; Mistlands and higher are prohibited.
    - **Yagluth Slain (`defeated_goblinking`)**: Unlocks **`Mistlands`**. Carapace armor, Krom, Mistwalker, Arbalest, and Eitr magic staffs are now permitted; Ashlands gear is prohibited.
    - **The Queen Slain (`defeated_queen`)**: Unlocks **`Ashlands`**. Flametal armor & weapons, Askvin gear, Slayer, Ripper, and Ashlands magic staffs are now permitted.
    - **Fader Slain (`defeated_fader`)**: Unlocks **`DeepNorth`** / endgame.
  - **Real-Time Live Unlock & Cache Flush**: Hooks `ZoneSystem.GlobalKeyAdd` via `BossDefeatWatch` so when a boss is defeated while players are logged in, the server progression tier advances instantly with a celebratory announcement in the server log, and player violation caches in `EquipmentGuard` immediately reset without a server restart.
  - **Boss Defeat Watch Expansion**: Expanded `BossDefeatWatch` coverage to include **The Queen** (`defeated_queen`) and **Fader** (`defeated_fader`).

## 0.10.0

### Fixed
- **Floating Item Desync & Sleeping Rigidbody Wakeup**: Resolved an issue where wood logs and other ground items dropped or released by the vacuum engine hung frozen in mid-air on vanilla clients. In Unity PhysX, switching `useGravity = true` on a sleeping Rigidbody does not wake it up when velocity is zero; `VacuumEngine.HandBack` now seeds a subtle downward velocity (`Vector3(0f, -0.1f, 0f)`) into `s_velHash` and `s_bodyVelHash` upon transferring ownership to client peers. When the client's `ZSyncTransform.OwnerSync` synchronizes velocity, PhysX immediately awakens the Rigidbody, allowing gravity to cleanly drop the item to the terrain.
- **Coastal Water Buoyancy Dry Ground Guard**: Added ground elevation guards to `WaterBuoyancyEngine.IsWaterborneItem`. Previously, coastal terrain drops sitting at $y \le 30.35\text{m}$ near sea level or overlapping `WaterVolume` box colliders could be falsely evaluated as submerged, hoisted to water elevation, and claimed server-side. Items resting on solid dry land above the local liquid level are now safely recognized as land-based and ignored by buoyancy logic.
- **Ship Sailing Impact Damage & Position Snap Elimination**: Fixed ships (Karve, VikingShip) randomly taking bursts of 10 blunt damage while underway. Server-side inventory modifications increment `DataRevision`, forcing vanilla clients actively steering a vessel to execute `zDO.InternalSetPosition` from incoming `RPC_ZDOData`, snapping position coordinates. In vanilla's `Ship.UpdateWaterForce`, an instantaneous vertical depth delta ($\Delta\text{depth} / \Delta t > 2.5\text{m/s}$) triggers 10 water impact damage and collision FX.

### Added / Improved
- **Ship Motion & Steering ZDO Write Guard**: `ContainerRowsEngine`, `SortEngine`, `VacuumEngine`, and `StructureUpkeep` now monitor `s_user` and velocity vectors (`s_velHash`, `s_bodyVelHash`), pausing all inventory serializations, row anchoring passes, and upkeep while a vessel is underway or actively steered. Full expanded cargo capacities (Karve $4\times 6$, VikingShip $6\times 6$) are 100% maintained and serviced smoothly whenever the vessel is moored or docked, eliminating mid-sail netcode snaps.
- **Starter Boat Upkeep & Mooring Repair**: `StructureUpkeep` now recognizes `Ship` prefabs with `s_creator == 0L` (such as boats granted via `FirstSpawnGrant`), ensuring moored starter boats receive periodic repair maintenance rather than permanently accumulating wave chop damage.

## 0.9.5

### Added
- **Dedicated Security Audit Log (`SecurityLogEnabled`, `SecurityLogFileName`)**:
  - Direct append-only disk writer targeting `BepInEx/logs/wonderland_security.log`, completely separated from routine BepInEx log noise (`LogOutput.log`).
  - Every security event records UTC timestamp, player name with platform, bare Steam64 / account ID (`Peer.m_socket.GetHostName()`), stable PlayerID (`s_playerID`), exact world coordinates `(X, Y, Z)`, and full infraction details.
  - Thread-safe with immediate flush on every write to guarantee zero lost logs during unexpected shutdowns.
- **Equipment & Magic Surveillance Engine (`EquipmentGuard`)**:
  - Periodic server-side surveillance of all connected player character ZDOs (`VisEquipment` visual hashes, quality levels, and Eitr).
  - **Cheat & Banned Items**: Flags Valheim's developer cheat weapons and armor (`SwordCheat`, `SledgeCheat`, `ClubCheat`, `ArmorIronChestCheater`, `HelmetCheater`, or custom `BannedItemsList`).
  - **Server Progression Tier Ceilings (`MaxAllowedTier`)**: Detects high-tier end-game gear (e.g. Ashlands/Mistlands weapons and armor) on servers capped at earlier progression biomes (`Meadows`, `BlackForest`, `Swamp`, `Mountain`, `Plains`, `Mistlands`, `Ashlands`).
  - **Impossible Item Qualities**: Flags items with qualities exceeding the legitimate maximum (`quality > m_shared.m_maxQuality`).
  - **Magic & Eitr Surveillance**: Detects Eitr presence on player ZDOs on pre-Mistlands servers.
  - **Anti-Spam Memory**: Suppresses duplicate logs so a standing equipped violation flags once upon equipping rather than flooding the log every scan.
  - **Flag-Only by Default**: Operates in logging/detection mode (`EquipmentGuardKick = false`) with optional kick enforcement.
- **Server-Side Forced Player Map Visibility (`ForcePlayerMapPosition`)**:
  - Intercepts `ZNet.UpdatePlayerList` right before server broadcast, forcing `peer.m_publicRefPos = true` and broadcasting real-time, physics-synced character coordinates.
  - Completely server-side: vanilla clients render player pins with character names on the map and minimap for all players at all times.
  - Overrides the client-side "Visible to other players" toggle so no player can hide on the map without permission.
  - `ForcePlayerMapPositionAdminBypass`: Allows authenticated server admins on `adminlist.txt` to toggle off their visibility for stealth observation.
- **Container Item Tier & Cheat Enforcement**:
  - `ItemSanityGuard` and `ItemIntegritySweep` now inspect container contents against `BannedItemsList` and `MaxAllowedTier`.
  - Smuggled cheat or over-tier items dumped into chests, carts, or boats are recorded with container coordinates in the security log and optionally purged (`ItemIntegritySweepCorrect`).
- **Unified Identity Logging Across Security Guards**:
  - `VitalsGuard`, `PositionWatch`, and `ItemIntegritySweep` updated to capture Steam IDs, player names, and world coordinates on all flags.

## 0.9.4

### Added
- **Dual-Vector Mod Enforcement Engine**:
  - **Active Framework Probing (`ModEnforcementActiveProbe`)**: Server actively probes connecting clients upon handshake
    completion with known framework challenge packets (`ServerSync VersionCheck`, `RPC_Jotunn_ReceiveVersionData`,
    `Jotunn_VersionCheck`, `ValheimPlus_VersionCheck`, `AzuAntiCheat_VersionCheck`). Authentic vanilla clients silently drop
    unknown RPC hashes with zero side effects, while modded clients with registered framework listeners respond directly
    into the server's mod honeypot and are kicked immediately.
  - **Placement Cadence Guard (`ModEnforcementPlacementGuard`)**: Server-side physics and cadence enforcement on incoming
    ZDO entity instantiation to catch client-side automation mods (e.g. `PlantEasily`, `FarmGrid`, `MassFarming`, `AutoPlant`).
    Vanilla Valheim enforces `Player.m_placeDelay = 0.4s` (limiting physical placement to at most 1 piece per 0.4s).
    The placement guard indexes all crop and plant prefabs at world ready (`ZNetScene.instance.m_prefabs`), tracks per-peer
    placement bursts over a rolling 0.5-second window, and enforces `ModEnforcementMaxPlantBatch` (default: 4).
  - **Instant Batch Purge on Detection**: When a client executes bulk planting (e.g. `PlantEasily` instantiating a 3x3 to 5x5
    grid in a single frame), all newly minted plant ZDOs in the burst are immediately claimed and destroyed server-side before
    kicking the client, ensuring zero cheated crops remain in the world.
  - **Zero Impact on Vanilla Area Harvest / Scythe Features**: Picking and area-harvesting (including Wonderland's vacuum
    engine and vanilla scythe multi-picking) remain completely untouched and uninhibited; enforcement strictly targets client placement.

## 0.9.3

### Fixed
- **Vanilla Bog Witch Persistent Event False Positive Kicks**: Resolved false positive non-vanilla client kicks
  for connecting vanilla players (`sent unauthorized RoutedRPC hash 0x93F71DF4 (-1812521484)`). Valheim's 1.0.15
  `PersistentEventSystem` hooks `ZRoutedRpc.m_onNewPeer` upon connecting and immediately requests active events with
  the wire string `"RequestActiveEventsList"`. The ModEnforcement whitelist previously registered this as
  `"RPC_RequestActiveEventsList"`.
- **Exhaustive 1.0.15 Wire RPC Audit**: Synchronized the static routed RPC whitelist with complete client and server
  1.0.15 decompilations:
  - Added missing persistent event RPCs: `"RequestActiveEventsList"`, `"RequestStartEvent"`, `"RequestStopEvent"`, `"UpdateClientEventsList"`.
  - Added missing console event triggers: `"startrandomevent"`, `"resetrandomevent"`.
  - Added missing `ZNetView` instance wire RPCs: `"UseEitr"`, `"discovered"`.
  - Added bidirectional alias coverage for all ZNetView instance RPCs that can be invoked with or without the `RPC_` prefix.

### Added
- **Dynamic Server & Instance RPC Resolution**: Upgraded `IsVanillaRoutedRpc(int methodHash, ZDOID targetZDO)` to
  dynamically inspect `ZRoutedRpc.instance.m_functions` and active entity `ZNetView.m_functions` on arrival. Any RPC
  method legitimately registered by the server engine or instantiated game objects is automatically validated, cached,
  and permitted through the wire filter, eliminating lifecycle timing mismatches.

## 0.9.2

### Added
- **Incompatible Version Client Disconnect Toggle (`ModEnforcementUseIncompatibleVersion`)**: Added a synced configuration
  setting in `[12 - Security]` (default: `true`). When enabled, disconnected clients receive Valheim's native
  **"Failed to connect: Incompatible version"** modal dialog (`ErrorVersion`), clearly signaling to the user that their
  client install/mods are not compatible with this pure vanilla server. When disabled (`false`), sends **"Kicked"** (`ErrorKicked`).
- **Safe Packet Flush & Drain**: Integrated with Valheim's `ZNet.PeersToDisconnectAfterKick` buffer, giving the client-bound
  disconnect packet a 1-second transmission window before socket disposal to ensure the modal dialog reliably displays on the client.

## 0.9.1

### Added
- **Mod Enforcement Security Log Detection Stamp**: All mod enforcement security logs now stamp the exact UTC timestamp
  and the exact detection trigger (`[STAMP: YYYY-MM-DD HH:MM:SS UTC] [DETECTED: <trigger>]`).
  - Stamped directly into `[SECURITY:ModEnforcement]` for both kicking events and admin bypass events.
  - Formats exact mod trap probes (`'ServerSync VersionCheck' (ServerSync probe)`, Jotunn, ValheimPlus, AzuAntiCheat),
    unauthorized ZRpc and RoutedRPC hashes in both hexadecimal and decimal (`0xXXXXXXXX (hash)`), custom injected player
    sync keys (`'key'`), and non-vanilla client handshake version strings.
  - Enables dedicated server administrators to instantly grep, audit, and trace what client mod triggered enforcement.

## 0.9.0

### Added
- **Server-Side Vanilla Mod Enforcement**: Pure vanilla dedicated servers can now actively enforce that connecting
  clients are strictly unmodified, vanilla Valheim installs. Operates entirely server-side without requiring any client
  installation. Any client attempting to connect with client mods or mod frameworks (ServerSync, Jotunn, ValheimPlus,
  etc.) is immediately caught, audited in `[SECURITY:ModEnforcement]`, and disconnected.
  - **Multi-Vector Protocol Interception**:
    - **Raw ZRpc Wire Whitelist (`ZRpc.HandlePackage`)**: Peeks every incoming ZRpc packet on the wire before dispatch.
      Non-admin clients sending unknown or modded RPC method hashes are immediately blocked, dropped, and kicked.
    - **Mod Framework Traps (Honeypot)**: Actively catches client mod framework probes (`ServerSync VersionCheck`,
      `RPC_Jotunn_ReceiveVersionData`, `Jotunn_VersionCheck`, `ValheimPlus_VersionCheck`, etc.), extracts the exact
      offending mod GUID/name for the server security log, and drops the connection.
    - **Handshake Version Validation (`ZNet.RPC_PeerInfo`)**: Inspects the client's `versionString` before login
      completes, strictly rejecting modded version tags (`@`, `vplus`, `bepinex`, `patch`, etc.) and enforcing clean
      platform regex compliance (`^(l|dw|dl|ms|sw2)?-?\d+\.\d+\.\d+$`).
    - **In-Game Routed RPC Whitelist (`ZRoutedRpc.HandleRoutedRPC`)**: Blocks clients from invoking unauthorized custom
      routed RPCs in-game (preventing client cheat menus, unauthorized spawners, and utility mod RPCs).
    - **Synced Player Data Whitelist (`ZNet.RPC_ServerSyncedPlayerData`)**: Enforces vanilla player data keys
      (`platformDisplayName`, `baseValue`, `possibleEvents`, `defeatedBosses`), rejecting custom injected mod dictionaries.
  - **Explicit Client Disconnect Notice**: Offending clients are kicked with the clear message:
    *"Vanilla enforcement enabled, connect fairly with vanilla only client"*. Delivered across multiple channels:
    an in-game center-screen fading banner (`Player.Message` Center), in-game chat broadcast, and wire console print
    (`RemotePrint`) alongside the native `ErrorKicked` status.
  - **Authoritative Server Admin Bypass**: Evaluated purely server-side via `ZNet.instance.IsAdmin(socket.GetHostName())`
    directly against the server's `adminlist.txt`. Any authenticated admin is 100% exempt from mod enforcement checks,
    allowing staff to connect with admin/dev tooling and client mods without being kicked.
  - **Configurable Controls (`12 - Security`)**: Six new synced settings: `ModEnforcementEnabled` (master toggle,
    default on), `ModEnforcementAdminBypass` (default on), `ModEnforcementKick` (default on; set false for audit-only
    logging), `ModEnforcementKickMessage` (customizable kick text), `ModEnforcementStrictVersion` (default on), and
    `ModEnforcementInspectRoutedRpc` (default on).
- **Valheim 1.0.15 Compatibility**: Rebuilt and calibrated against the latest Valheim 1.0.15 dedicated server and
  client reference assemblies (Build IDs `25390671` server / `25390630` client, Network Protocol Version `40`).

## 0.8.4

### Fixed
- **A stack could be duplicated when a player picked it up in the same instant a chest vacuumed it.** Vanilla decides a
  pickup on the player's own client and tells the server afterwards; if the vacuum moved that stack into a chest inside
  that round trip, the player kept theirs and the chest had a copy. At 0.8.2's half-minute cadence that was a fluke.
  0.8.3's near-player pass moved a drop within a second of landing - exactly when the player who dropped it, or killed
  for it, is standing on it with auto-pickup running - so it became likely. Now a stack a player owns is first claimed by
  the server and only moved a second later, and only if no player asked for it in between: a player who reaches for it
  wins every tie, a player who asked but could not carry it (full inventory) does not block the chest, and a stack the
  chest cannot take after all is handed straight back. Should a pickup still slip through under heavy lag, the player's
  own late "destroyed" message is recognised and the copy is taken back out of the chest, logged. Stacks the server
  itself created (an auto-harvest sweep's drops) still go into the chest the instant they land.
- **Swimming fish counted as ground items.** A live fish carries the same item component as a dropped one, so any fish
  within `VacuumNearPlayersRadius` kept every chest near the shore loading each pass, and a chest that held that fish
  type could pull a live fish out of the water. Fish are left alone everywhere now, the same exception the buoyancy
  feature already made.
- **A feast on a table, or an egg warming by the fire, could be vacuumed.** Both are item objects placed in the world;
  a chest within `VacuumRadius` holding one more of the same pulled them in - the half-eaten feast came back whole.
  Placed item-pieces and hatching eggs are never touched now (an egg dropped anywhere else still is loot).
- **One stack nobody wanted loaded every chest in reach, every pass.** A trophy on the floor, an excluded item, anything
  no nearby chest already holds: the near-player pass loaded and checked every container within `VacuumRadius` of it
  every `VacuumInterval`, forever - and adding an item to `VacuumExcludedItems` made it *more* expensive, not less. Now
  the pass remembers what each chest held the last time it was read and only opens a chest that has changed since or
  that already holds the item type lying near it; excluded items never qualify a chest; a stack every container in
  reach declined rests 5 seconds before it is looked at again; a stack already moved no longer qualifies the remaining
  containers of the same pass; and one pass loads at most 32 containers per player, continuing where it stopped on the
  next pass. With junk on the floor and nobody touching the chests, the steady state is zero chest loads.
- **"container full" was logged for every stack, every pass.** Once per chest and item type per minute now.
- Smaller: a container write that fails mid-pass can no longer destroy the ground stacks queued behind it, nor lose a
  sibling-overflow or cache-drain transfer that had already been committed; a malformed ground stack no longer stops a
  chest from being vacuumed at all; the splash effect plays at most once per chest per frame; a claimed stack whose
  chest never got its look is handed back within three seconds, so nothing is ever left frozen on the ground; a chest
  someone has open does not "judge" the stacks around it.

### Changed
- The `[AutoHarvest] ... swept N more within R m` line ends with `, N stacks into a chest` when the drops went in at
  once, and R now shows a decimal (`4.5 m`, not `5 m`).
- One `[Vacuum] tracking N container and M item prefab types.` line at world load.
- The `[WaterBuoyancy] Granted pickup ownership` line is verbose-only now: with the settle step it fires for every
  stack a player reaches for beside a chest that also wants it.
- Known residual, documented rather than hidden: a manual E press inside the ~100 ms before the claim reaches the client,
  by a player whose inventory can take only *part* of the stack, leaves that part duplicated (the client's reduced
  count is the one packet the claim has to discard). Auto-pickup is not affected. One E press can also be swallowed
  during the settle second - vanilla's own retry picks it up a moment later.

### Reference (the data behind the 0.8.4 entry)

**The settle step** (`Subsystems/ItemFlow/VacuumEngine.cs`: section note above `VacuumGroundItemsInto`,
`TryTakeOwnership`, `ProcessMaturedHolds`, `ReleaseHold`, `HandBack`, `ReleaseAllHolds`, `ShouldBlockOwnerChange`,
`WasMovedThisFrame`, `IsHeld`, `OnClientDestroy`, `TakeBack`; `Subsystems/ItemFlow/LatePickupPatch.cs` (new);
`Subsystems/ItemFlow/WaterBuoyancyEngine.cs`: `HandlePickupRequest`, `HasPickupGrace`, `ClaimedWithin`,
`ZdoSetOwnerPatch`; registered in `ItemFlowSubsystem.Initialize`)
- Decompile facts (1.0.12 server): `Humanoid.Pickup` (7397) requires `ItemDrop.CanPickup` (70558: owner, and 0.5 s past
  spawn), adds the stack to the inventory, then `ZNetScene.Destroy` -> `ZDOMan.DestroyZDO` (76929: owner only).
  `Player.AutoPickup` (11154) calls `ItemDrop.RequestOwn` (70481; retry 0.2 s doubling to 30 s) *before* it checks room
  or weight. `ZDOMan.RPC_ZDOData` applies a packet's owner field unconditionally when the packet's data revision is
  newer than the local copy, and only on a higher owner revision when it is not. `ZDOMan.SendZDOToPeers2` (76837)
  waits 50 ms and then serves one peer per frame; `SendZDOs` sends nothing to a peer whose socket queue is saturated.
  `ZDOMan.ReleaseNearbyZDOS` (every 2 s) gives a server-owned ZDO inside a player's active area to that player, and
  `ZDOMan.Update` sends ZDOs before `SendDestroyed`. `ItemDrop.AutoStackItems` (70322) only runs with more than 200
  item instances loaded on that client. `CreateNewZDO` stamps the server's session id into every ZDOID it mints.
- For a ground stack whose owner is not the server, and only once the chest has passed every check including
  `CanAddItem`: `SetOwner(server)`, `DataRevision += 4096` (an in-flight packet from the client arrives stale),
  `s_velHash` / `s_bodyVelHash` / `s_bodyAVelHash` zeroed, `ForceSendZDO(previousOwner, uid)` - the same writes
  WaterBuoyancy uses to hold an item at the surface - and an entry in `_holds` (`Until = now + HoldSeconds` = 1 s, the
  container's ZDOID). Not moved in this pass. Every frame `ProcessMaturedHolds` gives each matured hold's container one
  `ProcessContainer` (at most `MaxHoldContainersPerFrame` = 8 per frame); `TryTakeOwnership` then moves the stack only
  if the owner is still the server. Otherwise - a player asked for it with `RPC_RequestOwn` and `HandlePickupRequest`
  granted it, or a stale packet from a still-moving stack beat the claim - it is left alone (the next pass claims a
  still-present stack again, at rest). A stack still held after that look (chest full, chest open, item excluded
  meanwhile, container gone) goes to `ReleaseHold`: handed back to the nearest connected player within 96 m (else
  owner 0) and memoised as unwanted for 5 s. A hold whose container never got its look (per-frame budget) is released
  `HoldHardExpirySeconds` = 3 s after maturity. `VacuumEnabled = false` releases every hold at once.
- Fast path: a server-owned stack is moved in the same pass when its ZDOID was minted by this server session (a sweep's
  drop never had a client owner) or WaterBuoyancy took it from a client more than `HoldSeconds` ago
  (`WaterBuoyancyEngine.ClaimedWithin`); a fresher buoyancy claim gets the same settle time without a second claim.
- `HandlePickupRequest`: refuses to grant a stack in `_destroyedThisBatch` (`VacuumEngine.WasMovedThisFrame`, cleared
  at the top of `VacuumEngine.OnUpdate`, so the refusal holds in either Unity ordering of `ZNet.Update` and the plugin's
  update); refuses a stack the same peer was granted more than 3 s ago that still exists while
  `VacuumEngine.IsHeld` (the player could not take it - the client's own retry backoff outlasts the hold, the chest
  wins); when the requester already owns the stack according to the server, bumps the owner revision and re-sends it to
  that peer so a copy that disagrees learns (a stack nobody simulates otherwise). Grants are remembered 60 s.
- `ZdoSetOwnerPatch` (the existing `ZDO.SetOwner` prefix) now also asks `VacuumEngine.ShouldBlockOwnerChange`: a change
  away from the server is blocked for a stack moved this frame (destroy queued) and for a held stack unless
  `WaterBuoyancyEngine.HasPickupGrace` (the 3 s grace `HandlePickupRequest` sets before it calls `SetOwner`) or the
  hold is more than 3 s past maturity. Zero cost with nothing held or moved.
- `LatePickupPatch`: prefix on `ZDOMan.RPC_DestroyZDO`; for a sender other than the server it reads the batch (position
  restored) and, for every ZDOID in `_recentlyMoved` (stacks the vacuum moved within `RecentMoveSeconds` = 10 s, with
  container, item name and count), removes that many of that item from the container again (`Commit`, name cache
  refreshed) and logs `[ItemLedger] Vacuum rejected Nx <item>: '<player>' picked it up first - taken back out of the
  chest (N of N still there)`. A busy chest is retried every frame for `TakeBackRetrySeconds` = 30 s, then a warning.
  Moves that a failed container write rolled back are forgotten before the write's exception is logged.
- WaterBuoyancy's pickup grace, grant memory and claim times are purged on the sweep timer whether or not
  `AllItemsFloatEnabled` is on (the grace dictionary used to grow without bound with the feature off).
- Latency: a player-dropped stack is in its chest at most one `VacuumInterval` + 1 s after it lands (was one
  `VacuumInterval`); a sweep's drops are unchanged (same frame).

**The near-player pass** (`VacuumAround`, `IsCandidateGroundItem`, `IsPlacedOrHatching`, `RememberContainerNames`,
`RememberUnwanted`, `PruneUnwanted`, `PruneContainerNames`)
- A ground ZDO qualifies containers only if its prefab is in the item set, it is not placed (`ZDOVars.s_piece`, set by
  `ItemDrop.MakePiece` from `Player.PlacePiece`) or hatching (`ZDOVars.s_growStart` > 0, kept by
  `EggGrow.GrowUpdate` while the egg can grow), not in `_destroyedThisBatch`, not in `_holds`, not in `_unwantedUntil`
  (`UnwantedRetrySeconds` = 5 s) and not on `VacuumExcludedItems` (`ParsedList`: the comma list is split once per
  config string and kept as a case-insensitive set; prefab names come from a per-hash table, no `GetPrefab` in the
  pass). The same rule is applied per stack in `VacuumGroundItemsInto`.
- Container name cache: after each load (and after each save, so the revision on file is the keyed one)
  `RememberContainerNames` stores the set of shared item names the container holds together with its `DataRevision`.
  A container whose entry matches its current `DataRevision` is only loaded when a qualifying stack's item name is in
  that set; any write to the ZDO, by any client or by this server, raises the revision and invalidates the entry. At
  most `MaxContainerNameEntries` = 8192 entries, `ContainerNameTtlSeconds` = 600 s, pruned once a minute.
- Containers are collected first (container prefabs not yet visited this pass), then visited from a rotating start
  (`_nearRotation`, advanced by the number processed) with at most `MaxContainersPerVacuumAround` = 32 loaded per call.
  A container someone has open (`ZdoInventoryIO.IsBusy`) and a container beyond the cap do not judge: the stacks in
  their range are flagged truncated and are not memoised that pass. After the loop every stack that was not moved,
  not claimed and not truncated goes into `_unwantedUntil`, which holds at most `MaxUnwantedEntries` = 4096 entries.
- Item prefab set (`Initialize`): every `ZNetScene.m_namedPrefabs` entry with an `ItemDrop` whose shared name is set and
  no `Fish` component, keyed by the table's own `name.GetStableHashCode()`; two side tables give the prefab name and
  the shared item name per hash.
- `ProcessContainer` order is now: overflow guard, and if it moved anything an immediate commit (its sibling save and
  cache store happen inside the call, so this container's write follows at once); vacuum (each ground stack's
  deserialisation in its own try/catch - a malformed one is memoised and warned about, the rest of the chest proceeds);
  cache drain (commits inside the call, so it sits directly before the final write); commit. The whole sequence is in
  one try/catch: on an exception `_pendingGroundDestroy` is cleared without destroying anything, the moves it recorded
  are forgotten, the moved counter is restored and a rate-limited `[Vacuum] <prefab> at <pos> skipped this pass:
  <exception>` warning is logged (its own 30 s limiter, separate from the auto-harvest one). Ground copies are
  destroyed right after the commit, before the splash; `PlayVacuumEffect` at most once per container per frame.
- "container full" (`CanAddItem` false) is recorded once per container and item name per `FullLogIntervalSeconds` =
  60 s (`RecordFullOnce`).
- No new config keys; every setting the new code reads is read per use, so all of section 2 still hot-reloads.

## 0.8.3

### Changed
- **Drop-to-chest now starts from the player, not from a world-wide list.** Every vacuum pass first looks at the
  ground around each connected player and only opens the containers that actually have a loose item within
  `VacuumRadius` of them, so a stack dropped beside you is in its chest within a couple of seconds, and an
  auto-harvest sweep's loot is pulled in the same instant it lands. The world-wide round-robin still runs behind it
  for chests nobody is near. Until 0.8.2 that round-robin was the only pass: 64 container types, 25 chunks every 2
  seconds, across a 600,000-object world - any one chest got its turn roughly every half minute.
- **New setting `VacuumNearPlayersRadius`** (default 32 m, 8-64) in `2 - Vacuum & Auto-Harvest`: how far around each
  player the ground is checked. The `VacuumInterval` and `VacuumBatchSize` descriptions now say which pass they
  drive. Existing configs pick the new key up with its default on first load.
- **New default radii, and they say "radius" now.** `AutoHarvestRadius` defaults to 4.5 (was 8) and `VacuumRadius`
  to 15 (was 10). Both are radii, so the swept patch is 9 m across - one plot, not the farm - and a chest reaches
  30 m across; the descriptions spell that out. **Existing servers are migrated:** a radius still on its old
  default moves to the new one on first load (logged once); a value you had changed is left exactly as it was.

### Reference (the data behind the 0.8.3 entry)

**Two passes per `VacuumInterval`** (`Subsystems/ItemFlow/VacuumEngine.cs`)
- Order: `BeginVacuumPass()` (clears the visited-container set), `ProcessVacuumBatch()` (the round-robin -
  `VacuumBatchSize` chunks of the container-type scanner, skipping containers already visited this pass), then
  `ProcessVacuumNearPlayers()` (one `VacuumAround(character.Position, VacuumNearPlayersRadius, VacuumRadius)` per
  `ConnectedCharacter`).
- `VacuumAround(center, reach, vacuumRadius)`: `FindNear(center, reach)` filtered to item-drop prefabs (every
  ZNetScene prefab carrying an `ItemDrop`, hashed as `ZDO.GetPrefab` reports it) and not already moved this frame;
  none → return with no container I/O at all; otherwise `FindNear(center, reach + vacuumRadius)` filtered to
  container prefabs (`ContainerRegistry.PrefabNames`), and only a container with a loose item within `vacuumRadius`
  is marked visited and handed to `ProcessContainer` (unchanged: busy check, exclusions, load, overflow guard, cache
  drain, match-required vacuum, anchor, save, splash effect, ground destroy).
- Post-sweep: in `ProcessPendingHarvests`, a sweep that harvested at least one plant is followed by
  `BeginVacuumPass(); VacuumAround(trigger.Position, AutoHarvestRadius, VacuumRadius)`. `ItemDrop.DropItem`
  instantiates the item on the server, `ZNetView.Awake` creates its ZDO at that position and `Save()` writes the
  stack, so the drops are already in the sector index when the vacuum looks.
- `_destroyedThisBatch` (ground stacks already moved and queued for `DestroyZDO`) is cleared once per frame at the
  top of `OnUpdate`, never per pass: a queued destroy only leaves the sector index in `ZDOMan.Update`, so a mid-frame
  clear would let the post-sweep vacuum move a stack the regular pass had just moved.
- Cost at defaults: two 9-sector queries per player per pass (`ceil(32/64)` and `ceil(42/64)` both round to one
  ring) and zero container loads when nothing is on the ground; at the maxima (64 + 50 m) the second query is a
  25-sector ring.
- Defaults and migration: `AutoHarvestRadius` 8 → 4.5, `VacuumRadius` 10 → 15. `VacuumDefaultsStyle` (internal,
  section 2, `0` on any older file) stamps how far a file's radii have been rebased; `MigrateVacuumDefaults` moves an
  entry still holding its exact old default (`8` / `10`) to the new default and logs `[Config] N radius setting(s)
  were still on an earlier version's default and have been moved to the 0.8.3 defaults (AutoHarvestRadius 4.5,
  VacuumRadius 15 - each a radius, so twice that across) - set them back in section 2 if you preferred the old
  reach.`, then writes `VacuumDefaultsStyle = 1`. Same one-shot rule as the Discord template migration.
- Live tuning on 2026-09-16: the box already runs `AutoHarvestRadius = 4.5`, `VacuumRadius = 15` (so the migration
  moves nothing there), plus `VacuumInterval = 1`, `VacuumBatchSize = 60` set before 0.8.3; with the near-player pass
  the interval and batch can go back to 2 / 25.

## 0.8.2

### Fixed
- **Auto-harvest never reached a single farm crop.** The trigger watched vanilla's "picked" flag on the pickables
  around each player, and vanilla only writes that flag for a pickable that respawns or hides when it is picked - a
  berry bush, a mushroom, thistle, dandelion, a branch, flint, a core stand. Everything a farm grows is *destroyed*
  when it is picked and never sets the flag at all, so carrot, turnip, onion, barley, flax, the three seed plants,
  magecap and jotun puffs could never start a sweep: 47 of the game's 67 pickables were invisible to the feature.
  Pick one carrot in a patch now and the patch comes with it, each drop landing where its plant grew for a matching
  chest to vacuum home.
- **The brief hiccup when placing an item into an expanded chest.** Every Wonderland container write - vacuum, sort,
  overflow guard, cache drain, the Container Rows anchor, production supply's ore draw - first took ownership of the
  chest's ZDO for the server. The write never needed it (a ZDO field write is not owner-gated), but the claim knocked
  the player who had the chest open out of ownership: their client hides the container half of the inventory panel
  and cancels any drag until vanilla hands the chest back up to two seconds later, and while the server holds it the
  client can no longer mark the chest "in use", the flag every Wonderland sweep checks before touching a chest. Grown
  chests felt it most because the Container Rows sweep visits only grown chests, and often. Container writes no
  longer touch ownership at all.

### Changed
- **The trigger is the pick itself, not a search.** The server no longer scans the ground around every player each
  frame looking for something that changed; it reads the picking client's own "this one is picked" broadcast as it
  passes through the server, which happens once per pick for every pickable in the game, whether or not vanilla
  keeps the plant afterwards. It also costs one integer compare per routed message instead of a nine-sector scan per
  player per frame.
- **The sweep runs half a second after the pick** instead of in the same frame. One scythe swing cuts every plant in
  reach inside a single client frame, and that client's own delete batch for them only leaves on its next tick -
  sweeping instantly would re-harvest plants the player had already cut and pay out the same crop twice. After the
  half second the swing is over and only plants still standing are swept. It still reads as instant.
- **A pick only counts when a connected player is standing at the plant.** Any client can address that broadcast at
  any object, so a pick reported from more than 16 m away, or by something that is not a player on the server's own
  roster, is ignored and logged (at most one such warning every 30 seconds). The sweep is credited to the nearest
  player: the pick's sender is whoever owns the plant's ZDO, which on a shared base is often someone else. Queue
  guards cap what a single client can ask for: at most 16 picks waiting per player (a scythe swing exceeds that
  silently - the first sweep already covers the radius), 128 in total, at most 4 sweeps per frame and 40 plants per
  sweep pass (a dense field finishes over the next few frames).
- **Swept plants now give their extra drops** the way they do when you pick them by hand - magecap and jotun puffs
  hand over the two extra of themselves, vine ash, vine green and fiddlehead their extra berry. Only the main drop
  was given before.
- **Royal jelly is no longer swept.** It is the one pickable vanilla refuses to pick while it floats in tar, and that
  check lives in client physics the server cannot make, so the whole prefab is left alone rather than swept out from
  under a player who could not have picked it themselves.
- **`VacuumExcludedItems` matches either name** for auto-harvest now: the item (`Carrot`) or the plant it grows on
  (`Pickable_Carrot`). The item name alone still covers both the vacuum and the sweep.
- **One visit per chest per sweep.** Vanilla's sector walk re-scans the sector it stopped in on the next chunk, so a
  chest on that boundary was loaded (and, on any change, written) twice in one tick. The shared scanner now drops the
  overlap for every engine that uses it.
- **Container Rows keeps its place.** The eligible-container list is still re-checked every minute, but the sweep's
  round-robin is only restarted, and the log line only written, when the list actually changed. Up to 0.8.1 both
  happened every minute, so the head of the list was swept constantly while the tail could go unvisited, and the log
  claimed a config change sixty times an hour.
- **Setting descriptions rewritten** in `2 - Vacuum & Auto-Harvest`: `AutoHarvestEnabled` lists what is swept and what
  is never swept, `AutoHarvestRadius` says the sweep happens half a second after the pick, `VacuumExcludedItems` says
  which names it matches. No new settings, no changed defaults, and the section hot-reloads exactly as before.

### Reference (the data behind the 0.8.2 entry)

**Why crops never swept** (1.0.12 server decompile)
- `Pickable.SetPicked` (71058-71082) is the assembly's only write of `ZDOVars.s_picked`, and it writes it only when
  `m_respawnTimeMinutes > 0` or `m_hideWhenPicked != null`; otherwise it calls `m_nview.Destroy()`. The pre-0.8.2
  trigger polled that key around each connected player every frame (`ProcessHarvestTriggers` / `_lastPicked`, both
  removed), so the whole destroyed-on-pick class was unreachable by construction, not by a mistake in the scan.
- The split: 67 `Pickable` prefabs ship with the game. 20 keep their ZDO (berry bushes, mushrooms, thistle,
  dandelion, branches, flint, core stands, royal jelly) - only those ever swept. 47 are destroyed on pick: the 10
  `m_harvestable` ones the scythe cuts (carrot, turnip, onion, barley, flax, seed carrot, seed turnip, seed onion,
  magecap, jotun puffs) now sweep, and the other 37 (ores, tar, dungeon loot, crypt remains) say
  `m_harvestable = false` and stay out - their value usually sits in `m_extraDrops` or behind a pit that has to be
  drained first. Sweepable set: 20 - `Pickable_RoyalJelly` + 10 = 29 of 67.
- Sweepable is exactly `!m_tarPreventsPicking && (m_respawnTimeMinutes > 0f || m_hideWhenPicked != null ||
  m_harvestable)` (`VacuumEngine.IsSweepable`).

**The trigger** (`Subsystems/ItemFlow/HarvestTriggerPatch.cs`, new)
- Harmony prefix on `ZRoutedRpc.HandleRoutedRPC` (83646). `Pickable.RPC_Pick` (71024-71051) drops the items on the
  owning client and ends with `m_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true)` (71050); that reaches
  the server as `RPC_RoutedRPC` (83632) -> `HandleRoutedRPC`, which drops it without a trace because the server has
  no instance of that pickable (`VALHEIM-DEDICATED-SERVER-FACTS`: "a routed RPC aimed at a ZDO with no local
  instance is dropped, silently"). The prefix filters on `m_methodHash == "RPC_SetPicked".GetStableHashCode()` and a
  non-none `m_targetZDO` before anything else. A patch on `Pickable` itself could never fire: the server pins ZNet's
  reference position at (1e6, 0, 1e6) every physics tick and instantiates only around that point.
- The body never throws outward - an exception would unwind `RPC_RoutedRPC` before it relays the message to the
  other clients and leave a stale plant on their screens. Its own failures log at most once every 60 s.
- Deliberately a separate patch class from `WaterBuoyancyEngine`'s `RoutedRpcHandlerPatch` on the same method:
  `SafePatch` isolates each set's failure, and HarmonyX runs every prefix regardless of what another returns.
- `VacuumEngine.OnPickedRpc` then, in order: skips `data.m_senderPeerID == ZDOMan.GetSessionID()` - the server's own
  `RPC_SetPicked` from `HarvestPickable` comes straight back through this handler, because `InvokeRoutedRPC` to
  Everybody is handled locally and synchronously on the sender (83587-83590), and without this guard the sweep would
  re-enter itself without bound; reads the one bool from `m_parameters` at position 0 with a `GetPos`/`SetPos`
  save-restore (vanilla may still read the buffer after the prefix) and ignores `false`, which is a bush respawning
  (`Pickable.UpdateRespawn`); resolves the ZDO and requires a `Pickable` prefab that `IsSweepable` and is not
  excluded; requires the sender to be a connected peer (`ConnectedCharacters`, `Peer.m_uid == m_senderPeerID`) and a
  connected player within `MaxPickReachMeters` of the plant, who is the one credited; ledgers the picked plant in `_harvestedAt` so nothing queued behind it can take it again; queues a
  `HarvestTrigger` (uid, prefab hash, position, sender, picker name, `dueAt`).

**Guards and constants** (`Subsystems/ItemFlow/VacuumEngine.cs`)
- `HarvestSweepDelaySeconds` = 0.5 s - the deferral. A scythe swing sends one RPC per plant in one client frame while
  that client's `DestroyZDO` batch for them only leaves on its next `ZDOMan.Update` (76812-76821).
- `MaxSweepsPerFrame` = 4 - sweeps run from `OnUpdate`, never inside the network handler; the rest wait a frame.
- `MaxPendingTriggersPerPeer` = 16, `MaxPendingTriggers` = 128 - over either, the pick is ledgered but gets no bonus
  (only the global cap warns; the per-player one is what a scythe swing looks like).
- `MaxHarvestsPerSweep` = 40 plants per pass; a trigger that hits it is re-queued `HarvestSweepContinueSeconds` = 0.1 s
  later for the rest of the field.
- `MaxPickReachMeters` = 16 m - vanilla interact range is 5 m; the rest is headroom for a sprinting player's character
  ZDO lagging behind them. Measured from the nearest connected player, not the RPC sender: `Pickable.Interact` routes `RPC_Pick`
  to the plant ZDO's owner (82855-82857) and only the owner broadcasts `RPC_SetPicked`, and `ReleaseNearbyZDOS` leaves
  ownership with whichever player holds it anywhere inside their ~96 m active area. The sender still has to be a
  connected peer (the anti-forgery half).
- `TriggerWarningIntervalSeconds` = 30 s - one trigger warning per 30 s, whichever kind.
- `FailureLogIntervalSeconds` = 60 s (`HarvestTriggerPatch`) - one patch-failure warning per minute.
- Ledger: `MaxTrackedPickables` = 50000 and `LedgerPruneIntervalSeconds` = 60 s gate `PruneHarvestLedger`, which drops
  entries older than one game day or whose ZDO no longer exists (every swept crop, once its destroy has gone
  through). Aged out rather than cleared: dropping a still-fresh entry reopens the re-harvest window the ledger
  exists to close.
- `AutoHarvestEnabled` turned off between a pick and its sweep clears the queue - the pick already happened on the
  client, the bonus simply does not follow.

**What a sweep does** (`SweepBonusHarvest` / `HarvestPickable`)
- `ZdoSpatialQuery.FindNear(position, AutoHarvestRadius)` for the same prefab hash, skipping the trigger's own uid,
  anything with `s_picked` set, and anything the ledger says was harvested inside its own `m_respawnTimeMinutes`
  (a plant that never respawns is never swept twice). The exclusion list is re-checked at sweep time in case it was
  hot-edited in the half second since the pick.
- Order per plant, changed in 0.8.2: ledger + `SetOwner(ZDOMan.GetSessionID())` + (`s_picked` / `s_pickedTime` only
  for the keep-the-ZDO class) **first**; then `ItemDrop.DropItem` of `Game.ScaleDrops`'d `m_amount` (`m_dontScale`
  honoured, floor `m_minAmountScaled`) at the plant + 0.3 m, from a clone of the prefab's `ItemData` with
  `m_dropPrefab` set by hand (a prefab's own template never runs `ItemDrop.Awake`, and `DropItem` instantiates from
  exactly that field); then every `m_extraDrops` roll via `DropTable.GetDropListItems()` at + 0.5 m (new in 0.8.2);
  then `ZRoutedRpc.InvokeRoutedRPC(Everybody, uid, "RPC_SetPicked", true)` so loaded clients hide the model; then
  `ZDOMan.DestroyZDO` for the destroyed-on-pick class. The broadcast is last so nothing reacting to it can find the
  plant unharvested.
- Every drop is counted under the `AutoHarvest` ledger tag as before, so the BarrkBOT `vacuum_items_moved_*` counters
  are unchanged in shape.

**Log lines**
- `[AutoHarvest] Pickable_Carrot picked by 'Name' - swept 5 more within 8 m.` (info, only when at least one was
  swept; the radius is the live `AutoHarvestRadius`).
- `[AutoHarvest] ignored a pick of Pickable_Carrot reported by peer 139331814: not a connected player.` (warning)
- `[AutoHarvest] ignored a pick of Pickable_Carrot: no connected player within 16 m of it.` (warning)
- `[AutoHarvest] 128 picks are already waiting to sweep - 'Name' picked Pickable_Carrot and it gets no bonus.` (warning;
  the per-player cap is silent)
- `[AutoHarvest] sweep of Pickable_Carrot failed: <Exception>: <message>` (warning, once per 30 s, the deferred sweep)
- `[AutoHarvest] trigger failed: NullReferenceException: <message>` (warning, once per 60 s, patch body only)
- `[AutoHarvest] ledger pruned: 812 finished entries dropped, 50120 kept.` (info; "entry" when exactly one)
- `[ItemLedger] AutoHarvest moved 2x $item_carrot` - unchanged, one per drop.

**The `2x` in every ledger line is the world, not the mod**
- `[ItemLedger] AutoHarvest moved 2x $item_blueberries` for a plant whose `m_amount` is 1 is the world's own resource
  rate: `Game.ScaleDrops` (101052-101071) multiplies by `m_resourceRate` (the `resourcerate` global key) and rounds,
  so any rate from 150% to 249% turns 1 into 2. Every one of the 259 auto-harvest drops in the live log this boot -
  blueberries, thistle, common mushroom, flint, dandelion, yellow mushroom, raspberries, wood, surtling core - reads
  `2x` for that reason. A world at 100% logs `1x`. Wonderland doubles nothing; it calls the same scaler vanilla does.

**Deliberately not reproduced on a swept plant**
- The Farming-skill bonus yield `Pickable.RPC_Pick` rolls on the picking client: the server has no `Player` and no
  skills, so a swept plant pays the scaled base amount only.
- The theft aggro (`BaseAI.AggravateAllInArea(position, m_aggravateRange, AggravatedReason.Theif)`, 71048) and the
  pick effects, both of which need a live GameObject the server does not have.
- Tar-floating: vanilla's `m_tarPreventsPicking` test is client physics, so the affected prefab is excluded outright.
- None of this touches the pick the player actually made - that ran with full vanilla behaviour on their own client.
  Only the swept-in bonus is trimmed.

**The expanded-chest hiccup** (`Core/Data/ZdoInventoryIO.Save`)
- Cause: `Save` did `zdo.SetOwner(ZNet.GetUID())` before `zdo.Set(ZDOVars.s_items, …)`. `ZDO.Set(int, byte[])` →
  `IncreaseDataRevision` has no owner check, so the data landed and replicated regardless of the claim. The claim bumped
  `OwnerRevision`; `ZDOMan.RPC_ZDOData` applies a newer `OwnerRevision` even when it drops the data; the client's
  `InventoryGui.UpdateContainer` gates the container panel on `m_currentContainer.IsOwner()` and otherwise
  `SetActive(false)`s it and cancels the drag; `ZDOMan.ReleaseZDOS` (2 s timer) hands the ZDO back and the panel is
  re-enabled. `Container.SetInUse` / `UpdateUseVisual` write `s_inUse` only as owner, so a chest the server had taken
  could never be marked in use again and `IsBusy` failed open for the rest of the session.
- Callers, all through `ZdoInventoryIO.Save` and none changed: `VacuumEngine.ProcessContainer`, `SortEngine`,
  `GridGrowth` (sibling overflow), `ContainerRowsEngine.Visit`, `ItemIntegritySweep`, `ProductionSupplyEngine`.
  Pressure on the box that reported it: `ContainerRowsInterval = 2` / `ContainerRowsBatchSize = 125` (defaults 5 /
  25), `SortInterval = 15` / `SortBatchSize = 100` (defaults 30 / 10) - about 80 container visits a second across
  three scanners; 23 anchors in 7.7 h, one Karve anchored twice on adjacent lines (the duplicate-visit overlap).
- Ownership elsewhere is unchanged: the vacuum's ground-item destroy, buoyant items, spawn culling and swept
  pickables still claim their ZDOs because `DestroyZDO` and position writes are owner-gated. No container a player is
  using is claimed any more.
- Scanner: `ZdoSpatialQuery.PrefabSetScanner.Advance` remembers the previous chunk's ZDOIDs and drops them from the
  next; vanilla's `GetAllZDOsWithPrefabIterative` breaks on its 400-sector budget before advancing its index, so
  consecutive chunks overlap by one sector. `ContainerRowsEngine.Announce` compares the new eligible list to the last
  and only then replaces the scanner (which restarts at prefab 0 / sector 0) and logs `[ContainerRows] eligible set
  changed (N type(s)) - ContainerRowsExcludedContainers or the multiplier was edited; sweep restarted.` The per-minute
  `eligible set refreshed …` line is gone.
- If the hiccup is still seen on 0.8.2, the next lever is that live tuning: back at the defaults the write pressure
  drops about fifteen-fold, and the section hot-reloads.

## 0.8.1

### Added
- **Player platform everywhere a player is named.** The server now reads which platform each connection comes from
  and shows it wherever it lists players: the join / leave / death / first-join log lines (`'Rohan' (PC) connected.`),
  the heartbeat roster in the log and in Discord (`Rohan (PC), Cpt JD (Xbox), ColdMonkey (Switch 2)`), the `{players}`
  roster in every Discord template, a new `{platform}` placeholder for the player an announcement is about - the
  default join, leave and first-join headlines now carry it (`🟢 **Rohan** (PC) joined **VanillaBean01**`,
  `🔴 **Rohan** (PC) left **VanillaBean01**`, `🎉 Welcome **Rohan** (PC) to **VanillaBean01** — first time here!`) - and a
  `platform` field on each BarrkBOT player row. Labels are `PC`, `Xbox`, `PlayStation`, `Switch 2`, or blank when the
  game did not say. **Existing servers are migrated:** a join / leave / first-join template still on its 0.8.0 default
  is moved to the new one on first load (logged once); anything you had edited is left exactly as it was. It is the account platform, which is all the handshake carries: `Xbox` covers both the console and
  the Microsoft Store / Game Pass PC build, and `PC` is any Steam client (Windows, Linux, Steam Deck). The account id
  itself is still never shown or exported.

### Reference (the data behind the 0.8.1 entry)

**Player platform** (`Core/Data/PeerPlatform`)
- Source: the peer socket's host name, which is the client's `PlatformUserID` - `ZPlayFabSocket.GetHostName()` returns
  `m_platformPlayerId.ToString()` (`Steam_7656…`, `Xbox_2533…`, `PlayStation_…`, `Nintendo_…`) over `-crossplay`;
  `ZSteamSocket.GetHostName()` is the bare Steam64 and the peer is Steam by construction. Built exactly as
  `ZNet.UpdatePlayerList` builds the history id (Steamworks: `new PlatformUserID(m_steamPlatform, host)`; PlayFab:
  `PlatformUserID.TryParse(host)`), so the first-join check and the label can never disagree.
- Label map (`PlatformUserID.m_platform` → text): `Steam` → `PC`, `Xbox` → `Xbox`, `PlayStation` → `PlayStation`,
  `Nintendo` → `Switch 2` (`Version.Platforms` has no other Nintendo target), anything else → its own name, unparseable →
  `""`. Account platform only: `RPC_PeerInfo` sends `Version.CurrentVersion.ToString()` without
  `Version.GetPlatformPrefix()` (`l` / `dw` / `dl` / `ms` / `sw2`), and neither `SimulationDistance` nor `m_playfabId`
  identifies the device, so Xbox console and Microsoft Store / Game Pass PC are one label.
- Shown in: `[DiscordNotify] 'Name' (PC) connected. / disconnected. / died. / joined this world for the first time.`
  (the bracket is omitted when blank); `[Heartbeat] … N player(s) online: Name (PC), Name (Xbox)`; the Discord `{players}`
  roster (join / leave / boss / heartbeat defaults) and `{platform}`; BarrkBOT `players.<id>.platform`. `players_notes`
  in the export explains the label. The leave line reads the platform in the `ZNet.Disconnect` prefix while the socket
  is still attached (`GetHostName` reads a field, no network call).
- Defaults changed: join `🟢 **{player}** ({platform}) joined **{world}**\n-# {playercount} online · {players}`, leave
  `🔴 **{player}** ({platform}) left **{world}**\n-# {playercount} online · {players}`, first join `🎉 Welcome **{player}**
  ({platform}) to **{world}** — first time here! {mention}\n-# Say hi 👋`; death / boss / heartbeat / online / offline
  unchanged. When the platform is blank and the template contains `({platform})`, the empty bracket is removed
  before posting.
- Migration: `MigrateDiscordTemplates` is now staged - `DiscordTemplateStyle` 0 → step 1 (0.7.2 one-liners → current
  defaults) and step 2; 1 → step 2 only (the three 0.8.0 defaults above → current); 2 = done. Edited templates are
  never touched. Log: `[Config] N Discord message template(s) were still on an earlier version's default and have been
  moved to the 0.8.1 style - edit them in section 14 if you preferred the old wording.`, then `DiscordTemplateStyle = 2`.
- Shape/contract: BarrkBOT `players.<id>.platform` is additive (schema_version stays 3); `players_notes` gained the
  sentence explaining the label; `BARRKBOT_CONTRACT.md` and the `tools/barrkbot/` samples updated and re-run through the
  6.1.5 reader (a string scalar rides in the row and is not ranked). `ConnectedCharacter.Platform` / `NameWithPlatform`
  are the shared spelling. Every section 14 template description lists `{platform}`.

## 0.8.0

### Added
- **BarrkBOT export.** `BepInEx/config/Wonderland/barrkbot_wonderland.json`, rewritten every minute (`17 - BarrkBOT
  Export`) in the shape BarrkBOT 6.0.119 reads: a live server block (world, uptime, who is online and the peak, the
  world rates in force, the bosses defeated / remaining and the date of each defeat seen since 0.8.0, the server's
  `-name`), a `players` map keyed by the stable character id (name, online now, first/last seen, sessions,
  server-measured connected time, deaths, first-visit date), and lifetime automation counters (items fed to
  stations by item, vacuumed and auto-harvested, cached, starter kits, raids blocked, spawns culled, total
  security flags). Everything is server-observed - nothing is taken from a client's report - and every caveat
  ships as `_notes` guidance in the file. Sessions and connected time follow the connection, so dying and
  respawning is not a new session. A clean shutdown rewrites the file once more saying `online: false` with nobody
  on, so the bot never reports the last roster as still online; switching the export off at runtime removes the
  file. Counters persist across restarts in `Wonderland.BarrkBot.<world>.dat` (an unreadable one is moved aside,
  never overwritten); both files are written temp-then-rename. A player's platform id and per-player security-flag
  tally are kept in that registry for the admin but deliberately not exported. Serialized with Json.NET (the game
  ships 13.0.2 in `Managed/`; `ValheimModding-JsonDotNET` is declared as a dependency so mod managers install it
  too). `BARRKBOT_CONTRACT.md` documents the field names the bot depends on.
- **Discord: restyled messages.** Most announcements are now two lines of Discord markdown - a bold headline plus a
  small grey `-#` subtext (the live roster on join/leave/boss/heartbeat, the online count on death, the mod version on
  server-online; the offline post is one line) - e.g. `🟢 **Rohan** joined **VanillaBean01**` / `-# 3 online · Rohan,
  Cpt JD, ColdMonkey`, `⚔️ **Eikthyr** has fallen on **VanillaBean01**!`, `💚 **VanillaBean01** · up **2h15m** · **3**
  online`. Every template can now use every placeholder: `{player}` `{boss}` `{world}` `{uptime}` `{playercount}`
  `{players}` `{time}` (a live Discord timestamp - "5 minutes ago", in each reader's own timezone) `{version}`
  `{mention}`. Write `\n` in the .cfg for a new line. **Existing servers are migrated:** a template still on its
  0.7.2 default is moved to the new style on first load (logged once); anything you had edited is left exactly as it
  was. All of section 14 hot-reloads like the rest of the config.
- **Discord: three opt-in extras, all off by default and all through the same webhook.** `DiscordAvatarUrl` (empty =
  the webhook's own avatar), `DiscordMention` (what `{mention}` turns into - a role, a user, or @everyone/@here; the
  boss-defeat and first-join templates carry `{mention}` out of the box, and with it blank nothing is ever pinged),
  and `DiscordHeartbeatIntervalMinutes` (0 = share the log heartbeat's interval; set e.g. 60 for an hourly roster
  while the log keeps its 15-minute pulse). Mentions are whitelisted per post (`allowed_mentions`) and every
  player-supplied string is neutralised before insertion, so a character named "@everyone", "<@&role>" or even
  "{mention}" can never ping anyone through a join, leave, death or roster line.

### Fixed
- **Production Supply could make a hand-fed ore or fuel vanish ("the smelter ate my silver").** Every auto-feed
  used to take server ownership of the station ZDO first. While the server owned a smelter, a player standing at
  it who pressed E had their ore removed from their inventory and the game's own `RPC_AddOre` routed to the
  owner - the server - which has no live smelter instance and silently dropped it (same for hand-fed coal, and
  wood/resin into hearths and torches). Worse, the owning client's once-a-second tick could win the revision race
  and leave the two sides disagreeing about the owner, stalling the station and eating every hand-feed until the
  next auto-feed visit. The server now never claims a station: a station a nearby player's client is simulating
  is fed through vanilla's own owner-side `RPC_AddOre` / `RPC_AddFuel` (the write happens on the one machine
  running it) - and only once that client's own tick stamp on the station proves the instance is live, since a
  routed RPC to an owner still loading in is discarded unanswered, and only while that player is actually within
  range of it by their synced position (a stamp alone stays fresh for a second or two after a portal jump);
  anything unowned, owned by a gone session, or owned by a player who has since gone too far away to be simulating
  it (vanilla leaves a portalled-away owner holding the station until another player's area covers it) is written
  directly - and for that far-away owner the updated station is pushed to their client at once, because otherwise
  their own stale copy would come back with them and wipe everything fed while they were gone. A station whose
  owner is mid-handover or just released is skipped until the ownership has held still for a few seconds, and one
  whose nearby owner is not yet simulating it is skipped until they are (that case logs once at verbose). Fuel is only topped up when a
  whole unit fits (vanilla's own "it's full" threshold) - the old check spent a full coal on a fractional top-up on
  almost every visit of a burning smelter. Every feed now logs (VerboseLogging) the station, its position, which
  machine applied it and the before/after, so the next "where did it go" is answerable from the log. What remains:
  a departure that lands within about a tenth of a second of a visit can still cost that station one unit from
  the chest - nothing server-side can see a jump before the client reports it.
- **Discord: every veteran was welcomed as a first-time player.** The first-join detector keyed on a mod-private world
  key that only started existing in 0.7.2, so each existing player got the "joined for the first time - welcome!" post
  on their first login after the update (7 of 7 such posts on the live server were accounts with months of history).
  First join is now decided per **account** from Valheim's own persisted player history in the world file
  (`ZNet.World.m_playerHistory`, kept since the 1.0 world format) - checked in an `RPC_PeerInfo` prefix, because
  vanilla appends the account to that list inside the very same handshake. Anyone already in the history is never
  welcomed as new, and a veteran's new alt character isn't either. The stale `wonderland_discord_seen_*` keys the old
  tracker wrote are removed once on the next world load (logged when it happens).
- **Discord: "⚔️ Eikthyr has been defeated" posted on every server restart.** The already-defeated snapshot was taken
  at `ZNetScene.Awake`, before `ZNet.Start -> ServerLoadWorld -> ZoneSystem.Load` re-adds every saved global key -
  so the snapshot was always empty and the first re-added boss key was announced on each boot (4 of 4 boots in the
  live log). The snapshot now runs from a `ZNet.ServerLoadWorld` postfix, after every load path, and the boss watch
  refuses to announce anything until then.
- **Discord: "server is online" now really means it.** The post moved from `ZNetScene.Awake` (world not read yet) to
  after the world has loaded and, on a brand-new world, after location generation finishes - the same
  `GenerateLocationsCompleted` event vanilla uses to open the server. Skipped with a warning if the world load
  reported an error.
- **Discord: the log now says what's configured.** One `[DiscordNotify] webhook configured: ... | heartbeat: off
  (DiscordNotifyHeartbeat) | interval: 15 min (HeartbeatIntervalMinutes, shared with the log heartbeat) | ...` line at world load, so "why doesn't it post X" is answered by the log
  itself. (The Discord heartbeat is off by default - it was never a code fault - flip `DiscordNotifyHeartbeat` in
  section 14 to turn it on.)
- **Build:** csproj reference paths updated for the project's new location (`..\..\libs-Tools`).

### Reference (the data behind the 0.8.0 entries)

**Files this version reads or writes**
- `BepInEx/config/Wonderland/barrkbot_wonderland.json` - the BarrkBOT export, rewritten every `BarrkBotWriteSeconds`
  (default 60, floor 10) via temp-then-rename (`File.Replace`, delete+move fallback). Removed when `BarrkBotExportEnabled`
  is off (at boot or at runtime). Rewritten once more from `Plugin.OnDestroy` on a clean stop with `server.online = false`,
  `players_online = 0`, every `online_now = false` - a crash leaves the last periodic file and the reader's 60-minute age
  caveat is the signal.
- `BepInEx/config/Wonderland.BarrkBot.<world>.dat` - the persisted registry the export is built from (JSON, schema 1; not a
  `barrkbot_*` name on purpose so the bot never sees two files). Path pinned at load. An unreadable file is moved to
  `<file>.corrupt-<yyyyMMddHHmmss>` and a fresh registry started; if even the move fails the session runs in memory and
  writes nothing. Holds, per character id, what is deliberately **not** exported: `platform_id` (the account id vanilla's
  player history stores - `Steam_7656…`, `PlayStation_…`, `Xbox_…`) and the per-player `security_flags_count`.
- `wubarrk.wonderland.cfg` - new keys appended by Bind: section 14 `DiscordTemplateStyle` (internal migration marker,
  0 → 1), `DiscordAvatarUrl`, `DiscordMention`, `DiscordHeartbeatIntervalMinutes`; section 17 `BarrkBotExportEnabled`
  (default on), `BarrkBotWriteSeconds` (default 60). Everything in sections 14 and 17 is read at use time - edits to the
  running file take effect within the 5 s config poll, no restart.

**BarrkBOT export shape** (schema_version 3; the field names below are the interface - see `BARRKBOT_CONTRACT.md` in the repo
before renaming any)
- Top level: `schema_version`, `generated_at` (ISO 8601 Z), `source` ("Wonderland x.y.z"), `intervals.write_seconds` (the
  effective, clamped value), `session_started_at` (process start), `tracking_since` (registry start), `export_notes`,
  `server`, `server_notes`, `players`, `players_notes`, `players_not_achievements` (`deaths_alltime`, `sessions_alltime`),
  `lifetime`, `lifetime_notes`.
- `server`: `name` (the `-name` string, "" if unreadable), `world_name`, `online`, `world_day` (integer or null = not
  measured), `known_accounts` (vanilla player-history count), `uptime_seconds`, `players_online`, `players_online_names`,
  `peak_players_online`, `wonderland_version`, `carry_weight_multiplier`, `stamina_regen_multiplier`, `bosses_defeated`,
  `bosses_remaining` (the five classic bosses), `bosses_defeated_at` (defeats seen since 0.8.0 only), `stations_switched_off`.
  `players_online`, `online_now` and the peak all come from one set: ids whose connection is up as of the last 5 s sweep.
- `players` (keyed by the character's `s_playerID` as a decimal string; rows exist from the first sighting of a spawned
  character): `name`, `online_now`, `first_seen_at`, `last_seen_at`, `sessions_alltime` (one per connection - a death's
  8-18 s respawn gap is not a session; two live connections on one copied character keep the first binding), `connected_seconds_alltime`
  (connection time, credited every sweep and for the last partial interval at shutdown), `deaths_alltime`, `welcomed_at`
  (only for accounts first seen after 0.8.0).
- `lifetime`: `production_supply_items_fed_alltime`, `production_supply_items_fed_by_item`, `vacuum_items_moved_alltime`
  (ground pickups + auto-harvest), `vacuum_items_moved_by_item`, `item_cache_items_stored_alltime`,
  `item_cache_items_returned_alltime`, `starter_kit_items_granted_alltime`, `raids_blocked_alltime`, `spawns_culled_alltime`,
  `security_flags_total_alltime`.
- Verified by feeding generated samples through BarrkBOT's real reader (6.1.5 API, `tools/barrkbot/barrkbot-render-probe.mjs`):
  0 / 3 / 12 players render at 4,148 / 5,720 / 5,654 chars against the reader's 6,000-char reply cap; at 12 players the
  overview shows one detail row and ranks over all twelve.

**Discord** (section 14)
- Keys: `DiscordNotifyEnabled`, `DiscordWebhookUrl`, `DiscordUsername`, `DiscordNotifyServerStatus`, `DiscordNotifyLogins`,
  `DiscordNotifyDeaths`, `DiscordNotifyFirstJoin`, `DiscordNotifyBossDefeats`, `DiscordNotifyHeartbeat` (default **off**),
  `DiscordLifecycleInterval` (death sweep, 3 s), `DiscordAvatarUrl` (http/https or ignored with one warning),
  `DiscordMention` (`<@id>`, `<@&id>`, `@everyone`, `@here`; digits-only snowflakes; whitelisted per post through
  `allowed_mentions`), `DiscordHeartbeatIntervalMinutes` (0 = share the log heartbeat; 0-1440), `DiscordTemplateStyle`.
- Placeholders every template accepts: `{player}` `{boss}` `{world}` `{uptime}` `{playercount}` `{players}` `{time}`
  (`<t:…:R>`, a live relative Discord timestamp) `{version}` `{mention}`. `\n` in the .cfg is a new line; a line starting
  `-#` is Discord subtext. Substitution is single-pass and player-supplied text has every `@` neutralised, so no name can
  ping. Defaults: join `🟢 **{player}** joined **{world}**\n-# {playercount} online · {players}`, leave `🔴 **{player}** left
  **{world}**\n-# {playercount} online · {players}`, online `🟢 **{world}** is online · started {time}\n-# Wonderland
  {version}`, offline `🔴 **{world}** is offline · {time}`, death `💀 **{player}** died\n-# {playercount} online`, first
  join `🎉 Welcome **{player}** to **{world}** — first time here! {mention}\n-# Say hi 👋`, boss `⚔️ **{boss}** has fallen
  on **{world}**! {mention}\n-# {playercount} online: {players}`, heartbeat `💚 **{world}** · up **{uptime}** ·
  **{playercount}** online\n-# {players}`.
- Migration: on first load a template still equal to its pre-0.8.0 default is moved to the new default, once, then
  `DiscordTemplateStyle = 1` is written; edited templates are left alone. Log: `[Config] N Discord message template(s) were
  still on the pre-0.8.0 default and have been moved to the 0.8.0 style - edit them in section 14 if you preferred the old wording.`
- First join is decided per account from `ZNet.World.m_playerHistory` in an `RPC_PeerInfo` prefix; the
  `wonderland_discord_seen_*` global keys the old tracker wrote (one per character id it saw) are removed once (logged with the count). Boss snapshot and the online post
  run after `ZNet.ServerLoadWorld` (and, on a new world, after `GenerateLocationsCompleted`). Boot logs one
  `[DiscordNotify] webhook configured: … | server status: … | … | heartbeat: … | interval: … | avatar: … | mention: …` line.

**Production Supply delivery** (`ProductionSupplyEngine`)
- Per visit one of three: **OwnerRpc** - the owning client's own `RPC_AddOre` / `RPC_AddFuel`, used only when that
  client instantiates the station's zone (its own validated simulation distance around its character's synced position,
  corner zones excluded unless classic - the same set `ZNetScene.CreateDestroyObjects` uses; a dead/respawning character
  counts as no instance) **and** its tick stamp (`s_startTime` for smelters, `s_lastTime` for fireplaces, written by the
  owner every 1 s / 2 s in world time) is younger than 2.5 s / 4.5 s and newer than the current ownership record.
  **Direct** ZDO write - unowned (at once if never owned this uptime, otherwise once the release has held still for 3 s),
  owned by this server, owned by a departed session, or owned by a connected player with no instance and a stale stamp; in that last case the station is then force-sent to that
  player (`ZDOMan.ForceSendZDO`) so the copy they carry is current when they return. **Skip** - an ownership that has not
  held still for 3 s of world time (checked every engine interval, not only on visits), a fresh stamp with no instance,
  or a near owner not yet ticking (logs once per episode at verbose: `[ProductionSupply] <id> skipped: owner '…' is N
  zone(s) away but has not ticked it for … - retrying each cycle.`).
- Every feed at verbose: `[ProductionSupply] <prefab> <id> @ (x,y,z) <- 1x <item> via rpc sent to owner '…' | server copy
  …` / `… via direct write | fuel a -> b` / `… via direct write, copy pushed to far owner '…' | …`.
- Visit cadence is one scanner cycle - roughly 20-45 s per smelter-family station and 2-4 min per fireplace-family station
  on a lived-in world - which is also the first-feed delay after boot.
- Known residuals, one unit per station per coincidence: a departure inside the ~50-100 ms character-position sync lag
  of a visit; a fireplace hand-fed to full inside that same lag (its `RPC_AddFuel` re-checks the cap on the owner's copy);
  a second client that once owned the station and never received its release.

**Build**
- Compiled against the Valheim 1.0.12 dedicated-server assemblies (same binary as the live box). Json.NET: the game's own
  `Managed/Newtonsoft.Json.dll` 13.0.2 (AssemblyVersion 13.0.0.0) satisfies the reference; the declared
  `ValheimModding-JsonDotNET` package is not required at runtime.

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
