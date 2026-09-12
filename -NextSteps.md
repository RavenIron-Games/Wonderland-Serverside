# Wonderland — Future Roadmap & Next Steps

## 1. Server-Side Portal Control System — moved out of scope

Relocated to `TortalPortal/TORTALPORTAL-LITE.md` on 2026-09-11: a full portal-network control system is beyond
Wonderland's scope and belongs with the portal mod, not bolted onto a general-purpose one. The 13-agent
decompile audit (option catalog, feasibility/effort tags, ruled-out list, and the central finding that
`TeleportWorld.Teleport()` never executes on a dedicated server) lives there now.

---

## RESOLVED in v0.6.4 — Security: Portal Speed False Positive

**Symptom:** Every portal use generated a `[SECURITY:PositionWatch]` audit flag. Root cause: the 3-second
position-sample window spans the entire map during portal transit, producing apparent speeds of ~110 m/s
against the 40 m/s ceiling.

**Fix delivered:**
- `IsLegitimateTransit()` checks three categories before any flag fires: portal proximity
  (`ZDOMan.instance.GetPortals()` — live dictionary, zero alloc), interior/dungeon transitions
  (`Character.InInterior` boundary crossing, Δy > 1 000 m), and admin teleport.
- Respawn suppression via `ZDOVars.s_dead` tracking — the tick after a death→spawn is silently skipped.
- Fly/noclip check gated to `pos.y < 2 000 m` so dungeon interiors don't compare against terrain below.

---

## 2. Item Water Buoyancy — Status & Fine-Tuning Backlog

### 2.1 Accomplishments in v0.6.3
- **Headless Liquid Detection:** Replaced non-existent collider checks with a three-tier procedural height resolution (`Heightmap.GetHeight` $\rightarrow$ `ZoneSystem.instance.GetGroundHeight` $\rightarrow$ `WorldGenerator.instance.GetHeight`). Outdoor water is accurately recognized across the entire map on dedicated servers.
- **Valheim 1.0 Client Physics Lock:** Server claims ownership of submerged ZDOs, zeroing linear and angular body velocities. Vanilla clients running `ZSyncTransform.CustomFixedUpdate` automatically set `m_body.useGravity = false; m_body.Sleep();`, locking items to the server's target height with zero local gravity sink.
- **Pickup Handoff Window:** A 3-second ownership lease triggered by `RPC_RequestOwn` prevents the server from reclaiming ownership while a player's client executes `ItemDrop.Pickup()`.
- **Waterline Recalibration:** Lowered `FloatSurfaceOffset` from `+0.15m` to `-0.25m` ($29.75\text{m}$ equilibrium), stopping items from rising $0.4\text{m}$ into the air above the water.

### 2.2 In-Game Test Feedback
> *"Works much better, not perfect"*
- Items now reliably stay afloat, bob with the water surface, and can be retrieved by players.
- Some minor visual height discrepancies remain depending on sea conditions and item mesh geometry.

### 2.3 Polish Backlog for "Perfect Floating"

#### A. Dynamic Wave Tracking vs Static Waterline
- **Current Behavior:** The server holds waterborne items at a steady target elevation ($y = 29.75\text{m}$). During calm seas, this sits directly in the waterline. However, during high winds and ocean storms, Valheim's ocean surface mesh undulates vertically via shader displacement and `WaterVolume.CalcWave()`.
- **Observation:** In large waves, wave crests briefly wash over the item, and wave troughs make the item look like it sits slightly above the dip.
- **Improvement Option:** Server can evaluate `WaterVolume.CalcWave(point, depth, waterTime, waveFactor, waveFactorBig)` using `EnvMan.instance.GetWind()` and `ZNet.instance.GetTimeSeconds()` to broadcast a gentle sinusoidal wave-riding coordinate that matches the client's ocean swell in real time.

#### B. Per-Item Mesh Pivot Offsets
- **Current Behavior:** All 1,164 tracked item prefabs share the global `FloatSurfaceOffset` of `-0.25m`.
- **Observation:** Different item 3D meshes have differing pivot centers:
  - Heavy, compact items (iron ore, copper, scrap metal) have low center-of-mass pivots and look natural at `-0.25m`.
  - Long weapons (spears, polearms, bows) and flat armor pieces can sit slightly higher or lower relative to their model bounds.
- **Improvement Option:** Group item prefabs into categories (Ores/Metals, Weapons, Armor, Small Drops/Coins) with fine-tuned surface offsets, or query the prefab's `ItemDrop.m_itemData.m_shared.m_hoverOffset` to compute an adaptive waterline offset per item type.

#### C. Micro-Drift & Flotsam Grouping
- Add an optional subtle ocean current drift so floating ship debris or slain serpent loot naturally clusters together into flotsam groups rather than staying rigidly frozen at the drop coordinate.

#### D. Swimming Suction Assist
- Slightly expand auto-pickup suction radius specifically when a player is swimming (`player.IsSwimming() == true`) so players don't have to precisely bump into floating items in rough seas.

---

## 3. Status Effects, Combat/Movement Speed & Food — Server-Side Feasibility Audit

**Executive summary.** Three questions were asked; here are the honest answers. **(1) Permanent status effects: natively feasible, no client mod.** `SEMan.AddStatusEffect`'s non-owner branch already routes through a targeted `ZRoutedRpc` call (`RPC_AddStatusEffect`) — the same wire pattern this repo's `PlayerNotify.Toast()` already ships — so the server can grant a real vanilla status effect to a specific connected player using only that player's ZDOID/owner peer id, no live GameObject required. Effective permanence is achieved by re-sending that RPC with `resetTime:true` on a repeating timer shorter than the effect's `m_ttl`, copying vanilla's own idiom for `Wet`/`Rested`. The one gap: there is no RPC to force an effect off early — only its own `ttl` (or the client's own logic) ends it. **(2) Attack speed, bow draw speed, run speed: mostly not feasible, and melee attack speed is flatly impossible.** All three are recomputed every frame from client-local inputs (skill level, equipment, animation timers) on the owning client, the same trap class as max HP — a server-side write is stomped within a frame. Melee swing speed is worse than "trapped": there is no numeric lever anywhere in the compiled game logic at all (the sole `ZSyncAnimation.SetSpeed` call in the entire assembly is hardcoded to `1f`). Run speed and bow draw speed at least have a real runtime float, but no `GlobalKeys` member touches any of the three, and `GlobalKeys.EnemySpeedSize` is explicitly enemy-only. The only real server-side levers are indirect and gradual: `SkillGainRate`/`SkillReductionRate` (levels up Run/Bow skill faster, raising their speed bonuses over time) and `MoveStaminaRate`/`StaminaRegenRate` (lets sprint be sustained longer, without changing its speed). **(3) Food duration: natively feasible today (`FoodRate`); food amount: not feasible server-side.** `GlobalKeys.FoodRate` is a genuine, already-provable-pattern lever (same mechanism class as the shipped `CarryWeightRate`) that scales how fast a food's internal timer depletes — durably stretches or shrinks buff duration on stock clients, zero install. It has **zero** effect on the HP/stamina/eitr *amount* a food grants; that number is baked into the item asset and computed client-side, with no `GlobalKeys` member or network path that reaches it — a server-side patch to those fields would be a no-op for connected clients, the same failure mode as the already-ruled-out stack-size case.

---

### 3.1 Status Effects (including permanent duration)

**Ownership baseline.** `SEMan`/`StatusEffect` ticking is entirely client-owned: `Character.CustomFixedUpdate` only calls `m_seman.Update(zdo, dt)` inside the `zdo.IsOwner()` branch (`Character.CustomFixedUpdate`, ~982–1009), and environment-driven effects (`Wet`/`Encumbered`/`Rested`) only run inside `Player.FixedUpdate` behind `!m_nview.IsOwner()` early-return plus `m_localPlayer == this` (~10194–10225). Neither is ever true for a remote player on a dedicated server — same class as the max-HP trap. This rules out any direct ZDO field write (e.g. `ZDOVars.s_seAttrib`) — it would be recomputed and overwritten by the owning client on its very next tick.

**Option catalog:**

- **Remote-add via routed RPC — native, zero client mod. Feasibility: confirmed. Effort: S.**
  `SEMan.AddStatusEffect(int nameHash, ...)` (`SEMan.AddStatusEffect`, ~28809–28821) has an explicit non-owner branch that calls `m_nview.InvokeRPC("RPC_AddStatusEffect", nameHash, resetTime, itemLevel, skillLevel, (int)variant)`. `ZNetView.InvokeRPC` (~82850–82858) is a thin wrapper around `ZRoutedRpc.instance.InvokeRoutedRPC(m_zdo.GetOwner(), m_zdo.m_uid, method, parameters)` (`ZRoutedRpc.InvokeRoutedRPC`, ~83577). The server needs no live GameObject — `ZDOMan.GetZDO(zdoid)` (~76754) and `ZDO.GetOwner()` (~74608) are pure server-side data — and can build the identical call directly, exactly like the live-verified `PlayerNotify.Toast()` (`Core/Data/PlayerNotify.cs:24`) already does for the `"Message"` RPC. The receiving client's own owned `SEMan` runs `RPC_AddStatusEffect` → `Internal_AddStatusEffect` fully locally: real effect, real icon, real gameplay modifier. `nameHash` is a plain stable string hash — no `ObjectDB`/asset access needed server-side. Registered at `SEMan` constructor, `m_nview.Register<int,bool,int,float,int>("RPC_AddStatusEffect", RPC_AddStatusEffect)` (~28724–28729).

- **De facto permanence via `resetTime:true` re-ping — native idiom, borrowed from vanilla. Feasibility: confirmed. Effort: S (a repeating timer on top of the above).**
  `StatusEffect.IsDone()` (~31173–31180) returns true only when `m_ttl > 0 && m_time > m_ttl`; `ResetTime()` (~31182–31185) sets `m_time = 0`. Vanilla's own `Wet` and `Rested` effects are kept alive exactly this way — re-applied every owner tick with `resetTime: true` (call sites ~3669, ~11644, ~16000), and `Internal_AddStatusEffect`'s resetTime branch calls `ResetTime()`/`SetLevel()` on the existing instance **without** re-running `CanAdd()` (~28852–28864). The server can reproduce this over the network: fire `RPC_AddStatusEffect` with `resetTime=true` on a timer shorter than the target SE's `ttl`, and `IsDone()` never trips. This is not a ZDO write the client would stomp — it's a routed RPC the client executes with its own owned code, so it is exempt from the ownership trap.

- **Asset-baked `m_ttl <= 0` as "never expires" — real code-level concept, unconfirmed which vanilla SE assets use it. Feasibility: uncertain. Effort: S if a suitable asset is found.**
  `StatusEffect.m_ttl` (~31043) is a plain public float, and `IsDone()` treats `<= 0` as infinite. Serialized asset values aren't visible in the decompile, so this is a real one-call shortcut *if* a matching asset is identified live in-game — not required, since the resetTime re-ping above already achieves permanence without it.

- **Food-granted stat bonuses bypass SEMan entirely — separate mechanism, separate lever.** Eaten-food bonuses live in `Humanoid.m_foods` (`Food.m_time`/`m_health`/`m_stamina`/`m_eitr`), not SEMan, and are covered fully in §3.3 via `GlobalKeys.FoodRate`.

**Ruled out:**
- **Direct ZDO write to `ZDOVars.s_seAttrib`** — `SEMan.Update` recomputes and rewrites this field from its own live list every owner tick (~28802–28806); a server write is silently stomped, and it only encodes a bitmask of `StatusAttribute` flags, not a visible effect.
- **Forced early removal via routed RPC** — `SEMan.RemoveStatusEffect(int, bool)` (~28882–28909) has no non-owner branch and no registered `RPC_RemoveStatusEffect` anywhere; every call site (~8339, 11576, 11652, 11660, 11669, 11674–11689, 14002) is local, owner-only. Add is remotely reachable; remove is not.
- **Injecting a custom `ttl`/duration via the RPC parameters** — `RPC_AddStatusEffect`'s signature (`nameHash, resetTime, itemLevel, skillLevel, variant`) has no duration field; `ttl` is intrinsic to the asset already loaded client-side.
- **`GlobalKeys`-driven status-effect trigger** — directly checked `GlobalKeys.PlayerEvents` (~107145, 107227): gates random-event eligibility only. No `GlobalKeys` member appears anywhere in the `SEMan`/`StatusEffect` source region.
- **Server instantiating a live `SEMan`/`Character` to call `AddStatusEffect()` as a plain method** — a dedicated server never instantiates player/monster GameObjects near real gameplay; the routed-RPC path exists precisely because no such live object is available.

---

### 3.2 Attack Speed / Bow Draw Speed / Run Speed

**Ownership baseline.** All three final values are recomputed every frame from client-local inputs on the owning client's process — same trap class as max HP. A server-side write to any backing field is cosmetic at best, stomped at worst.

**Run speed.**
`Character.UpdateMovement` computes `speed = m_runSpeed * GetRunSpeedFactor()` (line 1799) when running; `m_runSpeed` is declared at line 305. `Player.GetRunSpeedFactor()` override (16513–16517): `(1 + RunSkillFactor*0.25) * (1 + EquipmentMovementModifier*1.5)`. `RunSkillFactor` reads the client-local `Skills.m_skillData` dict (`Skills.GetSkillFactor`, 18568–18575); `EquipmentMovementModifier` reads client-local equipment-modifier aggregation (`Player.GetEquipmentModifier(0)` / `s_equipmentModifierSources[0] = "m_movementModifier"`, 16414–16419, 9537–9541). Zero `Game.m_*` references anywhere in this chain.

**Melee attack speed — ruled out entirely, not just trapped.**
The only `ZSyncAnimation.SetSpeed(float)` call in the entire assembly (verified by an exhaustive grep across the whole ~168k-line file) is hardcoded `m_zanim.SetSpeed(1f)` (line 2850; definition at 87347). `Attack.m_speedFactor`/`m_speedFactorRotation` (22540–22542) govern movement/turn retention mid-swing via `GetAttackSpeedFactorMovement`/`Rotation` (7307–7327), not animation playback rate. There is no attacks-per-second field anywhere on `Attack` or `SharedData` — swing cadence is a fixed Animator clip length baked into the client build. **No lever exists, client-side or server-side.**

**Bow draw speed.**
`Humanoid.GetAttackDrawPercentage` (7150–7164): `skillFactor = GetSkillFactor(bow skill)`, then `Mathf.Lerp(m_drawDurationMin, m_drawDurationMin * 0.2f, skillFactor)`, against `m_attackDrawTime` (a private client-local timer). `Attack.m_drawDurationMin` (22604) is a fixed per-weapon-asset float. Same client-local dict, same trap class as run speed.

**Option catalog (indirect, server-authoritative levers):**

- **`GlobalKeys.EnemySpeedSize` — confirmed enemy-only, ruled out for players.** `Character.UpdateMovement` multiplies by `Game.m_enemySpeedSize` only inside an explicit `if (!IsPlayer())` block (1809–1816); also scales non-player visual/hitbox scale (899–901). Populated at `Game.UpdateWorldRates`, ~101143.
- **`MoveStaminaRate` / `StaminaRegenRate` — sustain duration, not top speed. Feasibility: confirmed, trivial to add.** `UseStamina(dt * num2 * Game.m_moveStaminaRate)` for run/jump/swim (11943, 12039, 12052); regen tick `m_stamina += num2*dt*Game.m_staminaRegenRate` (11527). `MoveStaminaRate` compounds multiplicatively with `StaminaRate`, since `Character.UseStamina` itself also applies `v *= Game.m_staminaRate` (14057) on top. Lets the server make sprint effectively unlimited without touching the m/s number. Parsed at 101139–101140.
- **`SkillGainRate` / `SkillReductionRate` — gradual, real lever on both Run-speed and Bow-draw bonuses. Feasibility: confirmed, same mechanism class as the working `CarryWeightRate` precedent. Effort: S.** The per-use skill-XP increment is computed in the nested `Skill.Raise(float factor)` method as `m_info.m_increseStep * factor * Game.m_skillGainRate` (line 18476), reached via the public `Skills.RaiseSkill(SkillType, float factor = 1f)` (line 18686); death skill loss uses `m_DeathLowerFactor * Game.m_skillReductionRate` (18739). Parsed at 101141–101142. Higher `SkillGainRate` levels Run/Bow (and every other skill) faster server-wide, which over time raises the run-speed bonus (16515) and shrinks average bow draw time (7155–7156) for everyone — a real, live, low-effort addition to `WorldRatesEngine`, but should be framed as "faster skill leveling," not an instant speed change.
- **Forced status-effect injection carrying `SE_Stats.m_speedModifier` — plausible, medium confidence, needs live verification.** `SEMan.AddStatusEffect`'s non-owner RPC fallback (28809–28829, same mechanism as §3.1) is not a ZDO write and is not subject to the ownership trap. `SE_Stats.m_speedModifier` (class 30182, field 30298) feeds `ModifySpeed` (`speed += baseSpeed * m_speedModifier`, 30612–30621), called every frame from `SEMan.ApplyStatusEffectSpeedMods` (28741–28748) inside the real movement-speed calc (`Character.UpdateMovement`, line 1846) — this genuinely changes simulated speed, not just a tooltip. Blocking gap: no candidate vanilla `StatusEffect` asset with a known nonzero `m_speedModifier` was identifiable from static decompile alone (serialized asset values aren't in the C#); would need `ObjectDB.instance.GetStatusEffect(...)` probed live. Only affects run/movement speed — `ApplyStatusEffectSpeedMods` is never called from the attack or bow-draw paths.

**Ruled out:**
- **`GlobalKeys.EnemySpeedSize` as a player run-speed control** — explicit `!IsPlayer()` guard, confirmed above.
- **Direct ZDO/field write to raise run/attack/draw speed instantly** — all three are recomputed every frame from client-local inputs; a server write is cosmetic-at-best, stomped-at-worst.
- **A numeric "attack speed multiplier" field anywhere in `Attack`/`SharedData`** — no such field exists; the sole `SetSpeed` call is hardcoded to `1f`.
- **A `GlobalKeys` member for attack speed or draw speed** — the full 57-member enum (re-verified at 101270–101326) has no such member.

---

### 3.3 Food Duration & Amount

**`FoodRate` scales DURATION only — native, zero client mod, same mechanism class as `CarryWeightRate`. Feasibility: confirmed. Effort: trivial (add a key to the existing `WorldRatesEngine`/`WorldRatesPatches` pattern).**
`Player.UpdateFood(float dt, bool forceUpdate)` (11825–11855) advances `m_foodUpdateTimer += dt * Game.m_foodRate`, decrementing every active `Food.m_time` by one tick per rollover. Higher `FoodRate` → faster depletion → shorter effective duration; lower → longer. Corroborated by the inverse relationship in the HUD readout, `food.m_time / Game.m_foodRate` (~48294–48330, exact division at 48319). `Game.m_foodRate` is populated by the same `Game.UpdateWorldRates`/`trySetScalarKey(GlobalKeys.FoodRate, out m_foodRate)` pipeline as `CarryWeightRate` (~101128–101137) — every process (client and server) independently re-derives it from its own `ZoneSystem` global keys, so this is **not** a server-to-ZDO write race; it's a shared multiplier every vanilla client's own unmodified loop already applies to its own locally-ticked food timer. A side effect worth noting: because the granted-amount curve (`Pow(Clamp01(remaining/burnTime), 0.3)`) is driven by *remaining-time fraction*, stretching duration (`FoodRate` < 100%) also stretches how long the near-peak amount is sustained — total food value delivered over a session rises even though peak numbers never change.

**Food AMOUNT (HP/stamina/eitr granted) — not feasible server-side at all. No native lever; a server-side patch is a no-op.**
The instantaneous grant is `food.m_health = food.m_item.m_shared.m_food * f` (and stamina/eitr analogues), where `f = Mathf.Pow(Mathf.Clamp01(food.m_time / m_foodBurnTime), 0.3f)` (11835–11839) — no `Game.m_*` scalar appears anywhere in this computation, and the full `GlobalKeys` enum has no `FoodAmount`/`FoodValue` member. Per-item food stat fields (`m_food`, `m_foodStamina`, `m_foodEitr`, `m_foodBurnTime`, `m_foodRegen` on `ItemDrop.ItemData.SharedData`, ~69057–69065) are static values baked into each process's own local `ObjectDB`/prefab copy and never travel over the wire in any RPC/ZDO path found — a server-side Harmony patch to these fields changes only the server's own irrelevant local copy and does nothing for a connected vanilla client. Same failure mode as the already-ruled-out stack-size case.

**Ownership baseline.** The entire eating pipeline (`EatFood`, `UpdateFood`, `GetTotalFoodValue`, `SetMaxHealth/Stamina/Eitr`, the 10s food-regen `Heal()` tick) runs only inside `Player.FixedUpdate`'s `m_nview.IsOwner() && m_localPlayer == this` branch (10194–10225; `UpdateStats` → `UpdateFood` at 11500–11508). Both `EatFood` call sites are hardcoded `Player.m_localPlayer.EatFood(...)` (70550, 124517) — `m_localPlayer` is always null on a dedicated server, so none of this ever runs server-side. Same class as the confirmed max-HP correction.

**Adjacent, food-relevant levers (out of strict scope for "amount," but real and cheap to add):**
- **`StaminaRate` / `EitrRate` — action *cost*, not food grant.** `Character.UseStamina`/`UseEitr` do `v *= Game.m_staminaRate` / `Game.m_eitrRate` before subtracting from the pool (14051–14067, 14011–14027). Setting these below 100% makes stamina/eitr-costing actions cheaper — stretches how far a food-granted pool goes, without touching what food itself restores.
- **`StaminaRegenRate` — passive regen only, not food's own regen tick.** `m_stamina += num2*dt*Game.m_staminaRegenRate` (11527) is ambient regen; food's own periodic HP-regen tick (`m_foodRegen` items) applies `Heal(num2)` every 10s with **no** rate multiplier at all (11860–11877), only a `regenMultiplier` from `SEMan.ModifyHealthRegen` (a status-effect hook, unrelated to `GlobalKeys`).

**Ruled out:**
- **Harmony patch to `SharedData` food fields** — static per-process local data, never networked; no-op for connected clients (same as the stack-size correction).
- **A dedicated `GlobalKeys` member for food amount/value** — does not exist; only `FoodRate` touches the food domain, and it is proven duration-only.
- **Server writing HP/stamina/eitr pool ZDO fields to simulate a bigger grant** — owning client recomputes and overwrites these every tick from `UpdateFood`/`GetTotalFoodValue`; same trap as max HP.

---

### 3.4 Mechanism Reference — `GlobalKeys` Round-Trip & Scalar-Key Table

**Round-trip mechanism (applies to every "native lever" claim above).** `ZoneSystem` holds `HashSet<string> m_globalKeys` (113305) and `Dictionary<string,string> m_globalKeysValues` (113309), both written atomically by `GlobalKeyAdd(string keyStr, ...)` (113483–113512), which parses `"key value"` via `GetKeyValue` (113560–113579) and synchronously calls `UpdateWorldRates()` → `Game.UpdateWorldRates(m_globalKeys, m_globalKeysValues)` (101128). Distribution is `SendGlobalKeys(long peer)` (113466–113471), which broadcasts the full key list via `ZRoutedRpc.instance.InvokeRoutedRPC(peer, "GlobalKeys", list)` (`peer=0L` = broadcast to everyone already connected). The receiving client's `RPC_GlobalKeys` (113473–113481) does `ClearGlobalKeys()` then re-adds every key — a full resync that re-runs `Game.UpdateWorldRates` **on the client**, independently, from its own copy of the data. RPC registration (`Register("SetGlobalKey"/"RemoveGlobalKey")` on the server vs. `Register("GlobalKeys", RPC_GlobalKeys)` on the client) happens in `ZoneSystem.Start()` (113436–113456) — **not** `Awake()`, which only builds location/biome lists (113419) and registers nothing; `Start()` also calls `UpdateWorldRates()` directly on its first line. `SendGlobalKeys` is called both from `OnNewPeer` (113609–113616, per newly-joining peer) and from `SetStartingGlobalKeys`/`ResetGlobalKeys` (115888–115929, broadcast to everyone) — this repo's existing `WorldRatesEngine.cs`/`WorldRatesPatches.cs` hooks already cover both paths for `CarryWeightRate`, and the same hook shape works for any key below.

**Because this is a broadcast-and-reparse channel, not a per-tick ZDO field write, none of the `serverAuthoritative: true` levers below are subject to the client-ownership/stomp trap** — every client independently recomputes its own `Game.m_*` static from the same shared key list.

`GlobalKeys.Preset` is a pure cosmetic/label key: `Game.UpdateWorldRates` never reads it (confirmed by reading the full body, 101128–101195). The actual preset→rates bundle is Unity scene-serialized data (`KeyButton.m_keys`/`KeySlider.SliderSetting.m_keys`, bare inspector-populated fields, ~52498–52565, ~52973–52976) applied only through the menu-only `ServerOptionsGUI`, which self-disables once `ZNet.instance` exists (`ServerOptionsGUI.Update`, 59605–59611). It adds no capability beyond setting the underlying scalar keys directly.

| GlobalKeys member | Scales | Consumption site |
|---|---|---|
| `PlayerDamage` | Damage dealt by players | `hit.ApplyModifier(Game.m_playerDamageRate)` — line 2591 |
| `EnemyDamage` | Damage dealt by non-players | line 2445 |
| `WorldLevel` (int, 0–10) | Enemy scale/speed (899–905, 1810–1814), enemy armor (2549–2552), enemy max-HP (3249–3252), enemy level-up chance (110627–110631, 111260–111264), mineable-object HP (123506/134252/146059), `WearNTear` piece HP (149434–149436) | multiple, see refs |
| `EventRate` | Random-event interval/roll chance | 106845–106872 |
| `ResourceRate` | Resource yield fraction, item stack cap | 67617–67623, 71280 |
| `StaminaRate` | ALL stamina costs (universal multiplier) | `Character.UseStamina`, line 14057 |
| `MoveStaminaRate` | Run/jump/swim stamina cost (compounds with `StaminaRate`) | 11943, 12039, 12052 |
| `StaminaRegenRate` | Passive stamina regen (not food regen) | 11527 |
| `AdrenalineRate` | Adrenaline gained per trigger | 13948 |
| `EitrRate` | Eitr action cost | `Character.UseEitr`, line 14017 |
| `DurabilityRate` | Durability loss per use (weapons/tools/blockers) | 8 sites: 7247, 8595, 10760, 10831, 13155, 23213, 23463, 23510, 23912 |
| `FoodRate` | Food buff **duration/decay speed** (not amount) | `Player.UpdateFood`, 11824–11828; HUD inverse at 48319 |
| `SkillGainRate` | Skill-XP increment per raise | `Skill.Raise`, line 18476 (public entry `Skills.RaiseSkill`, line 18686) |
| `SkillReductionRate` | On-death skill-lowering factor | line 18739 |
| `EnemySpeedSize` | Enemy move speed + visual/hitbox scale — **player-excluded** via `if (!IsPlayer())` | 899–901, 1809–1816 |
| `EnemyLevelUpRate` | Enemy level-up chance multiplier | 110631, 111264 |
| `CarryWeightRate` | Max carry weight (already-shipped precedent) | `Player.GetMaxCarryWeight`, line 14449 |
| `Preset` | Cosmetic/UI label only | never read by `Game.UpdateWorldRates` |

Not reachable via `GlobalKeys` at all: `Game.m_localDamgeTakenRate` is set from a **per-player unique key** (`PlayerKeys.DamageTaken` via `Player.m_localPlayer.GetUniqueKeys()`), not from `ZoneSystem`'s world-key system — since `Player.m_localPlayer` is always null on a dedicated server (established `GetAllPlayers()`-empty correction), this field always resolves to its 1.0 default server-side regardless of any key the server sets.

**Confirmed absent (no native lever, for any of the four stats asked about in §3.2):** the full `Game` rate-field block (99973–100010, 18 fields) contains no attack-speed, bow-draw-speed, or status-effect-duration member, and the player movement-speed calculation (`Character.UpdateMovement`, 1780–1815; `Player.GetRunSpeedFactor`, 16513–16517) references zero `Game.m_*` fields. Any future claim of a `GlobalKeys` mechanism for those four stats should be treated as almost certainly wrong without new evidence.

### 3.5 Follow-Up: Flat Food Boost & Custom Status Effects

**Q1 — "Can we stop food decay effects but just keep the timer, i.e. full food boost until timer hits zero?" Infeasible as literally asked; a cheap felt-experience approximation exists.** The 0.3-power falloff curve is a bare literal inside `Player.UpdateFood` with zero server-reachable input, and food has no RPC surface at all (unlike status effects) — there is no lever, no ZDO field, and no re-feed trick that reaches it. `FoodRate` (already a real `GlobalKeys` lever) can stretch the real-time duration arbitrarily long so a food buff *reads* as full for the whole play session, but it does not change the underlying flat-then-cliff shape the user is picturing — see §3.5.1 for the exact reason and the math.

**Q2 — "Can we use the SE mechanism for a custom SE, and what are the limits?" Partial: existing vanilla buffs, yes; a genuinely new Wonderland-authored SE, no.** The server can grant any vanilla `StatusEffect` asset that's already compiled into every connected client (by name-hash), which does cover three of the user's four wishlist items — run speed, stamina regen, and reduced (not eliminated) swim-stamina cost all have real, confirmed-live vanilla assets. Attack speed remains flatly impossible (no lever exists anywhere in the compiled game, confirmed again in this pass). Critically, the server can only *pick which existing asset to fire* — it cannot invent a new one, and it cannot dial an existing asset's magnitude up or down (`itemLevel`/`skillLevel` are ignored by every stat-buff class); see §3.5.2 for the full mechanism.

---

#### 3.5.1 Flat Full-Value Food Boost

**Why `FoodRate` can't reshape the curve — it can only stretch the time axis.** `Player.UpdateFood` (`Player.UpdateFood`, ~11825–11855) does exactly this each tick-rollover: `food.m_time -= 1`, then `f = Mathf.Clamp01(food.m_time / m_foodBurnTime); f = Mathf.Pow(f, 0.3f); food.m_health = m_food * f` (stamina/eitr identical, same three lines, ~11834–11839). `Game.m_foodRate` appears exactly once in the method (~11828, `m_foodUpdateTimer += dt * Game.m_foodRate`) and only scales how fast real seconds convert into `m_time` ticks — it never touches `m_foodBurnTime` or the `0.3f` exponent. Substituting the elapsed-time fraction τ = t/(m_foodBurnTime/FoodRate) shows FoodRate cancels out of the formula entirely: granted-fraction = (1−τ)^0.3, independent of the rate. At the halfway point of *any* configured duration the grant is always ≈81.2% of peak — `FoodRate` only decides how long that halfway point takes to arrive in real seconds, it never flattens the curve itself.

**Is the `0.3f` exponent truly unreachable?** Yes. It is a bare literal with zero `Game.m_*` or `SharedData` input anywhere in `UpdateFood`. The only tunable fields on `ItemDrop.ItemData.SharedData`'s food block (`m_food`, `m_foodStamina`, `m_foodEitr`, `m_foodBurnTime`, `m_foodRegen`, `m_foodEatAnimTime`, ~69057–69067) are linear magnitude/duration/regen-rate scalars — none of them is a curve-shape parameter, and the full `GlobalKeys` enum has no food-amount member (already established in §3.3).

**Is there an RPC-based "keep re-feeding" trick, the way `resetTime:true` works for status effects (§3.1)?** No — food has no RPC surface at all. `EatFood` (`Humanoid.EatFood`, def ~11756) has exactly 3 call sites (~15475, 70550, 124517), every one reachable only through the client-local `ConsumeItem`/`UseItem` pipeline (`Player.ConsumeItem`, ~15462–15478, calls `EatFood` as a plain local virtual method, no `ZNetView`/RPC involved anywhere). A whole-assembly grep for `.Register(` (65 hits total) and separately for any food-mentioning RPC registration returns zero matches — there is no `RPC_*Food*` handler anywhere to re-trigger. The only other place `m_foods` appears is `Player.Save`/`Load`'s ZPackage blob (~14091–14371), which is character-profile persistence (the `.fch` save), not a live network channel — rewriting that offline blob only affects the *next login's* starting state, not decay during a live session.

**Verdict: infeasible as literally specified.** No lever reaches either the `0.3f` exponent or the amount computation, and food has zero RPC surface to exploit for a re-feed trick (unlike status effects). **The one honest partial win:** add `FoodRate` to `WorldRatesEngine`/`WorldRatesPatches` (same `trySetScalarKey(GlobalKeys.FoodRate, out m_foodRate)` mechanism already used for `CarryWeightRate`, ~101137) and set it well below 100%. This stretches real-time duration arbitrarily long, so within any bounded play session the food value reads as "basically full the whole time" — a felt-experience win, not a formula change. Present it to the user as exactly that, not as the literal flat-then-cliff behavior asked for.

---

#### 3.5.2 Custom Status Effects — What's Actually Possible

**Can Wonderland invent a novel SE (new icon/behavior)? No.** `RPC_AddStatusEffect` carries only `(int nameHash, bool resetTime, int itemLevel, float skillLevel, int variant)` (`SEMan` constructor registration, ~28728) — no asset payload of any kind crosses the wire. The receiving client's `Internal_AddStatusEffect` (~28831–28850) resolves `nameHash` purely against its own already-loaded `ObjectDB.instance.m_StatusEffects` (a plain `public List<StatusEffect>`, ~103623), populated *only* via `ObjectDB.CopyOtherDB` (~103655–103662) from a build-time Unity Inspector prefab reference (`FejdStartup.SetupObjectDB`, ~97559–97564) — the same "compiled in per process, never networked" trap already documented for `ObjectDB` item data. A whole-file grep confirms there is zero runtime `.Add()`/`.Insert()` onto that list anywhere in the ~168k-line assembly. If a chosen `nameHash` doesn't match anything already shipped client-side, `Internal_AddStatusEffect` just returns `null` silently — no log, no exception, no signal back to the server. **So Wonderland can only re-trigger status effects that already exist in every vanilla client's own build** — a genuinely new, Wonderland-authored SE is impossible over this channel without shipping a client-side asset/mod, which breaks the zero-install rule. **Not possible server-side-only, full stop.**

**What do `itemLevel`/`skillLevel`/`variant` actually let the server adjust? Nothing, for the stat-buff family.** A whole-assembly grep for `override void SetLevel` finds exactly two overrides in the entire file: `SE_React` (~29933, a reflect/thorns effect scaling `m_ttl` and later damage by `itemLevel`) and `SE_Shield` (~30096, an eitr-barrier effect scaling `m_totalAbsorbDamage` by `skillLevel`). Neither is a stat-buff class. `SE_Stats` — the class housing every field the user is asking about (speed, stamina, armor, damage mods) — does **not** override `SetLevel` anywhere in its body (~30182–30955 grepped clean, and its subclasses `SE_Rested`/`SE_Cozy`/`SE_Puke` were independently checked too, none override it either), so it falls through to base `StatusEffect.SetLevel(int, float)` (~31187–31189), whose body is empty: `{ }`. Every one of `SE_Stats`'s `Modify*` methods reads only its own fixed serialized field values, never `itemLevel`/`skillLevel`. **Net effect: the RPC's numeric parameters are decorative for every buff/debuff use case — the server's only real dial is *which existing asset to fire*, not how strong to make it.**

**Is there any way to turn a granted effect off early?** No — re-verified independently, matching §3.1's existing finding. `SEMan.RemoveStatusEffect` (~28887–28909) and `RemoveAllStatusEffects` (~28911–28924) are both pure local list loops with zero `m_nview` reference anywhere in their bodies. A whole-assembly registration sweep for any status-effect-related `Register("RPC_...")` finds only `RPC_AddStatusEffect` (~28728) — no `RPC_RemoveStatusEffect`/`RPC_ClearStatusEffects` exists anywhere. The one non-owner-gated call site of `RemoveAllStatusEffects`, inside `Player.OnDeath()` (~12846), is itself part of the client-owned death pipeline and not server-invocable as a discrete RPC. **Practical implication:** any Wonderland-granted SE should be given a short natural `ttl` and kept "on" by repeatedly re-sending `RPC_AddStatusEffect` with `resetTime:true` faster than that `ttl` (the permanence idiom from §3.1) — never grant a long/infinite baked-in `ttl`, because there is no way to cut it short later if the effect needs to end sooner than its own duration.

---

#### 3.5.3 SE_Stats Field Catalog

`SE_Stats : StatusEffect` (class def ~30182–30955) is the generic vanilla stat-buff subclass; all ~61 of its fields are plain `public` (no serialization restriction), so a one-time server-side reflective walk of `ObjectDB.instance.m_StatusEffects` (same access pattern already used by `ObjectDB.instance.m_items` in `WaterBuoyancyEngine.cs:86`) can enumerate every loaded asset's name, hash, and exact field values with zero hardcoded name list. Confirmed fields and their consumption sites:

| Field | Modifies | Consumption site | Direction |
|---|---|---|---|
| `m_speedModifier` | Run/movement speed | `ModifySpeed` (~30612–30621) → `SEMan.ApplyStatusEffectSpeedMods` (~28741–28748) → `Character.UpdateMovement` (~1846) | Buff to holder |
| `m_swimSpeedModifier` | Swim speed | Same `ModifySpeed` chain, swim branch | Buff to holder |
| `m_swimStaminaUseModifier` | Swim stamina cost | `ModifySwimStaminaUsage` (~30588–30591) → `SEMan.ModifySwimStaminaUsage` (~29179) → `Player.OnSwimming` (~12043–12059, `UseStamina(dt*num*Game.m_moveStaminaRate)`) | Cost reduction to holder |
| `m_staminaRegenMultiplier` | Passive stamina regen rate | `SEMan.ModifyStaminaRegen` (~29047) → `Player.UpdateStats` (~11500–11527) | Buff to holder |
| `m_healthRegenMultiplier` | Passive health regen rate | Same `Modify*`/`SEMan` pattern as stamina regen | Buff to holder |
| `m_eitrRegenMultiplier` | Passive eitr regen rate | Same pattern | Buff to holder |
| `m_healthUpFront` / `m_healthOverTime` | Instant / over-time HP grant | Field-block ~30184–30326 | Grant to holder |
| `m_addMaxCarryWeight` | Additive max carry weight | `ModifyMaxCarryWeight` (~30526–30533) → `SEMan.ModifyMaxCarryWeight` (~29071) → `Player.GetMaxCarryWeight` (~14445–14449) | Buff to holder |
| `m_mods` (`List<DamageModPair>`) | Per-damage-type **resistance** (Resistant/VeryResistant/SlightlyResistant/Weak) | `ModifyDamageMods` (~30480–30483) → `SEMan.ApplyDamageMods` (~28759–28765) → `Character.GetDamageModifiers`/`Character.Damage` (~2541, applied to the **defender**) | Defensive — genuinely reduces/increases incoming damage |
| `m_percentigeDamageModifiers` | Per-damage-type **outgoing** damage multiplier | `SE_Stats.ModifyAttack` (~30531–30538, `hitData.m_damage.Modify(...)`) → `DamageTypes.Modify` (~130282–130295) → call site `Attack.cs` (~23426, `m_character.GetSEMan().ModifyAttack(...)`, `m_character` = attacker) | **Offensive** — boosts the holder's own outgoing damage of that type, NOT a resistance (corrects an earlier misreading in raw research — see §3.5.4 notes on `GP_TheElder`/`GP_Yagluth`) |
| `m_attackStaminaUseModifier` / `m_blockStaminaUseModifier` (+flat) / `m_dodgeStaminaUseModifier` | Per-action stamina cost | Field block ~30184–30326, each a separate multiplier (not one blanket cost field) | Cost reduction to holder |
| `m_runStaminaDrainModifier` / `m_jumpStaminaUseModifier` / `m_sneakStaminaUseModifier` | Run/jump/sneak stamina cost | Same field block | Cost reduction to holder |
| `m_jumpModifier` (Vector3) | Jump height/distance | ~30302 | Buff to holder |
| `m_maxMaxFallSpeed` / `m_fallDamageModifier` | Fall-speed cap / fall damage | ~30305 / ~30307 | Buff to holder |
| `m_windMovementModifier` / `m_windRunStaminaModifier` | Wind movement penalty / wind-run stamina penalty | ~30310 / ~30312 | Negates a debuff |
| `m_staggerModifier` | Stagger resistance | Field block | Buff to holder |
| `m_adrenalineModifier` | Adrenaline gain rate | Field block | Buff to holder |
| `m_raiseSkill` / `m_raiseSkillModifier` | Skill-XP gain rate (by `SkillType` or `All`) | `ModifyRaiseSkill` → `SEMan`/`Character.RaiseSkill` (~11494) | Buff to holder |
| `m_skillLevel` / `m_skillLevelModifier` | Additive effective skill level | `ModifySkillLevel` → `Skills` effective-level computation (~18584) | Buff to holder |
| `m_pheromoneFlee` / `m_pheromoneTarget` | Monster flee/aggro toward the holder | `MonsterAI.PheromoneFleeCheck` (~28128–28145, real AI behavior, not cosmetic); `Character.UpdatePheromones` (~1077–1103, calls `monsterAI.Alert()`) | Niche AI lever, not a pure stat buff — `UpdatePheromones`'s aggro half gates through `Player.GetAllPlayers()` (~1089), always empty server-side per the established correction, so only `PheromoneFleeCheck` is reliably reachable |
| — | Additive/percent armor | Mentioned in the field block but exact consumption site not traced in this pass — treat as unconfirmed until independently verified | — |

**Blocking caveat that applies to every row above:** the mechanism is confirmed real, but **magnitude is 100% asset-baked** — per §3.5.2, `SE_Stats` never reads `itemLevel`/`skillLevel`, so the server cannot dial any of these fields; it can only choose which existing vanilla asset (with whatever number that asset happens to ship with) to fire. See §3.5.4 for which vanilla assets carry a usable nonzero value on each field.

**User's four wishlist items:**

| Wishlist item | Verdict | Field | Note |
|---|---|---|---|
| Boosted run speed | **Possible** | `m_speedModifier` | Resolves §3.2's "blocking gap" — confirmed nonzero live on `GP_Moder` (0.1) and `Potion_hasty` (0.15, the highest found) |
| Boosted attack speed | **Not Possible** | none exists | Reconfirmed in this pass: no `SE_Stats` field or `Modify*` hook touches `ZSyncAnimation.SetSpeed` (hardcoded `1f`, line 2850) or `Attack.m_speedFactor`/`m_speedFactorRotation` (~7307–7327, which only scale movement/turn retention mid-swing, not animation rate). Same dead end as §3.2, now doubly confirmed. |
| No stamina use while swimming | **Possible (partial — reduction, not elimination, from a confirmed asset)** | `m_swimStaminaUseModifier` | `GP_Eikthyr` confirmed live at −0.6 (60% cheaper, not free); the mechanism supports a full −1.0 (free) value in principle, but no vanilla asset with exactly −1.0 on this specific field was found in the live dump — see §3.5.4 |
| Stamina regen boost | **Possible** | `m_staminaRegenMultiplier` | Confirmed live: `Rested` (×2), `Warm` (×2), `Potion_stamina_lingering` (×1.25) |

---

#### 3.5.4 Concrete Candidate Assets

Grounded in a live WubarrksEye reflection dump taken from an actual running dedicated-server process (`build23105022`, clean vanilla) that enumerates `ObjectDB`'s full `StatusEffects` list with every serialized field value — this closes §3.2's "no candidate asset identifiable from static decompile alone" gap with real numbers, not guesses. All entries below are `confirmed live` unless noted.

| Asset (`nameHash` source string) | Real effect (confirmed live values) | Confidence |
|---|---|---|
| `Rested` | `m_healthRegenMultiplier`=1.5, `m_staminaRegenMultiplier`=2, `m_eitrRegenMultiplier`=2, `m_raiseSkillModifier`=0.5 (All skills) | Confirmed. Best general-purpose "buff everything" candidate — a normal state every player already reaches passively, so no balance red flag. |
| `GP_Eikthyr` | `m_runStaminaDrainModifier`/`m_jumpStaminaUseModifier`/`m_swimStaminaUseModifier` all −0.6 (60% cheaper run/jump/swim stamina); `m_ttl`=300s | Confirmed. Best available match for "reduced stamina while swimming," though not free. |
| `GP_Moder` | `m_addMaxCarryWeight`=300, `m_speedModifier`=0.1 (+10% move speed), Frost Resistant (`m_mods`), `SailingPower` attribute | Confirmed. Best run-speed candidate tied to a boss power. |
| `Potion_hasty` (Tonic of Ratatosk) | `m_speedModifier`=0.15 (+15% speed, highest of any asset found), +10 effective Run skill level; `m_ttl`=600s | Confirmed. Strongest pure run-speed candidate overall. |
| `Potion_stamina_lingering` | `m_staminaRegenMultiplier`=1.25; `m_ttl`=300s | Confirmed. Clean, no side effects. |
| `Warm` | `m_staminaRegenMultiplier`=2, `m_eitrRegenMultiplier`=2 | Confirmed. Clean, no side effects. |
| `GP_Bonemass` | `m_blockStaminaUseModifier`=−1 (free blocking), Blunt/Slash/Pierce all SlightlyResistant (`m_mods`, genuine incoming-damage resistance) | Confirmed. |
| `GP_TheElder` (**correction**) | `m_healthRegenMultiplier`=1.3 (+30% regen) **plus +60% more damage dealt** via Chop/Pickaxe hit types on the holder's own attacks (`m_percentigeDamageModifiers`, an offense buff — `m_mods` is empty, so it grants zero incoming-damage resistance) | Confirmed, direction corrected. Earlier raw research had this backwards as a defensive buff. |
| `GP_Yagluth` (**correction**) | +25 effective Farming skill level; **+10% more damage dealt across all types** on the holder's own attacks (`m_percentigeDamageModifiers`, offense — not a resistance); Lightning Resistant is real (`m_mods`) | Confirmed, direction corrected. |
| `GP_Queen` | `m_sneakStaminaUseModifier`=−1 (free sneaking), `m_eitrRegenMultiplier`=2, Poison Resistant | Confirmed. |
| `WindRun` (standalone item effect, not a boss power) | `m_windMovementModifier`=0.25, `m_windRunStaminaModifier`=−1 (negates wind penalty entirely) | Confirmed. |
| `SlowFall` (standalone item effect) | `m_maxMaxFallSpeed`=5, `m_fallDamageModifier`=−1 (fully negates fall damage) | Confirmed. |
| `Potion_bzerker` (Berserkir Mead) | `m_attackStaminaUseModifier`/`m_blockStaminaUseModifier`/`m_dodgeStaminaUseModifier` all −0.8; **but** Slash/Blunt/Pierce all Weak (`m_mods`, real vulnerability — takes MORE physical damage); `m_ttl`=20s | Confirmed. Flag the downside to the user before considering it — granting it "for free" via RPC also grants the vulnerability. |
| `Potion_strength` (Mead of Troll Endurance) | `m_addMaxCarryWeight`=250 | Confirmed. Per-player alternative/complement to the world-wide `CarryWeightRate` lever already shipped. |
| `Potion_frostresist` / `Potion_poisonresist` / `Potion_barleywine` | Single-element resistance (Frost/Poison/Fire → Resistant/VeryResistant, `m_mods`) | Confirmed. Simple, safe, single-purpose. |
| `GP_Fader` | `m_staggerModifier`=−0.5, Fire Resistant — exists live but has **no switch-case anywhere in `Player.cs`** referencing it | Confirmed to exist, purpose/trigger unconfirmed — likely unused/leftover content. Not recommended as a stable grant target. |

**Enumeration mechanism (so this catalog stays current across game updates):** `ObjectDB.m_StatusEffects` (`public List<StatusEffect>`, ~103623) is populated at boot on every process, dedicated server included, the same lifecycle already exploited for `ObjectDB.instance.m_items` in `WaterBuoyancyEngine.cs:86`. A one-time server-start `foreach (var se in ObjectDB.instance.m_StatusEffects)` with a `se is SE_Stats stats` cast and ordinary public-field reads builds a live, self-updating catalog of every stat-buff asset and its exact numbers — no hardcoded name list, no reflection library needed, immune to future asset renames.

---

**Ruled out (do not re-propose):**

- **Exact flat-then-cliff food value** — the `0.3f` exponent in `Player.UpdateFood` is a bare literal with zero server-reachable input, and food has zero RPC surface anywhere in the assembly (`EatFood`'s 3 call sites are all client-local `ConsumeItem` calls).
- **Rewriting the `.fch` save-blob's `m_foods` list to fake a live flat boost** — that's character-persistence data, only affects next login's starting state, not live in-session decay.
- **A dedicated `GlobalKeys` member for food amount** — does not exist; only `FoodRate` touches the food domain, and it's duration-only (§3.3, reconfirmed here).
- **Server injecting a brand-new `StatusEffect` ScriptableObject via `RPC_AddStatusEffect`** — the RPC carries only a `nameHash` int; the client resolves it purely against its own already-loaded local `ObjectDB.m_StatusEffects`, populated only from a build-time Unity prefab reference. No asset data ever crosses the wire.
- **Harmony-patching `ObjectDB.m_StatusEffects` server-side to add a custom SE definition** — same per-process-local trap as `ObjectDB` item stats; only affects the server's own irrelevant local copy.
- **Using `itemLevel`/`skillLevel` RPC params to scale `SE_Stats`-based buffs up or down** — `SE_Stats` never overrides `StatusEffect.SetLevel` (empty no-op by default); only `SE_React` and `SE_Shield` (neither a stat-buff class) consume those parameters at all.
- **Forcing early removal of a granted status effect via any RPC** — no `RPC_RemoveStatusEffect`/`RPC_ClearStatusEffects` is registered anywhere; `RemoveStatusEffect`/`RemoveAllStatusEffects` are pure local methods with zero `m_nview` usage (reconfirms §3.1's existing finding).
- **Injecting a custom `ttl`/duration directly via RPC parameters** — `RPC_AddStatusEffect`'s signature has no duration field at all.
- **No additive max-HP or max-stamina field exists on `SE_Stats`** — only up-front/over-time grants and *regen-rate* multipliers exist; there is no field that raises the ceiling of health or stamina, so the project's established max-HP caution isn't implicated by anything in this class besides carry weight.
- **`GP_Ashlands` / `GP_DeepNorth` as usable buffs today** — real literal strings in `Player.cs`'s guardian-power switch statements and console-command text, but resolve to no loaded `StatusEffect` asset in the live dump; granting either would silently no-op.
- **Any hand-authored Mead/Potion C# class** — confirmed there is no bespoke subclass for any mead/potion; every one is a plain `SE_Stats` asset instance distinguished only by field values, identical grant-by-hash mechanism to the boss powers.
- **The wiki-lore assumption that `GP_Moder` = windwalk/no-fall-damage and `GP_Yagluth` = carry weight/cold armor** — wrong per live data; those effects (`WindRun`, `SlowFall`) are separate standalone assets unconnected to any boss power, and `GP_Moder`/`GP_Yagluth`'s real effects are listed correctly in §3.5.4.
- **Treating `GP_TheElder`/`GP_Yagluth`'s `m_percentigeDamageModifiers` as incoming-damage resistance** — corrected in §3.5.3/§3.5.4: this field boosts the holder's own *outgoing* damage, traced through `SE_Stats.ModifyAttack` → `DamageTypes.Modify` → the attacker-side `Attack.cs` call site (~23426).

---

## 4. v0.7.0 Plan — Buff Roster Engine, World-Rate Defaults, Config Infrastructure

Follow-up mechanics check (2026-09-11, direct decompile + live `WubarrksEye_Dumps` query, not a full agent audit) resolved
three things the section-3 audits left open: whether distinct SEs actually stack, whether vanilla's "one potion per
category" rule applies to a server-triggered grant, and the real numeric ceiling available from existing assets. All
three change the shape of this plan versus the raw ask, most importantly for run speed.

### 4.1 Corrected mechanics (read this before the feature list)

- **Distinct SEs stack additively, off the same base value.** `SEMan.ApplyStatusEffectSpeedMods` (~28741) captures
  `baseSpeed` once, then loops every active `StatusEffect.ModifySpeed` against that same base — so N active
  speed-buff SEs sum to `base * (1 + Σm_speedModifier)`, not a max() or a diminishing-returns curve.
  `SE_Stats.ModifyStaminaRegen` (~30456) does the equivalent for regen: `if (m_staminaRegenMultiplier > 1) staminaRegen
  += m_staminaRegenMultiplier - 1` — buffs stack additively above 1, confirmed by reading the method body directly.
- **Vanilla's "one potion effect per category" rule does NOT apply to `RPC_AddStatusEffect`.** The only place
  `SEMan.HaveStatusEffectCategory` is ever consulted in the whole assembly is one call site inside the player's own
  item-consumption path (~15453, gates *eating a new item*). `SE_Stats` has no `CanAdd` override (the only
  `override bool CanAdd` in the entire file belongs to the unrelated `SE_Smoke`, ~30126) and `SEMan.AddStatusEffect`/
  `Internal_AddStatusEffect` never call `HaveStatusEffectCategory` themselves. **A server-triggered grant bypasses the
  normal "you already have a potion effect active" restriction entirely** — this is what makes stacking multiple
  same-category vanilla buffs (e.g. two different Mead effects) actually work as a deliberate Wonderland feature,
  something a real player drinking potions could never do.
- **Native `GlobalKeys.StaminaRegenRate` and the SE-stacked multiplier COMPOUND, not replace each other.** Read
  `Player.UpdateStats` directly (~11500–11527): `float staminaMultiplier = 1f; m_seman.ModifyStaminaRegen(ref
  staminaMultiplier); num2 *= staminaMultiplier; ... m_stamina += num2 * dt * Game.m_staminaRegenRate;` — the
  world-wide native rate and the per-player SE bonus are two independent multipliers on the same final tick. Setting
  `StaminaRegenRate` to 300% (native world dial) *and* granting `Rested`+`Warm` (SE-stacked, +100%+100% = x3 via the
  additive rule above) at the same time compounds to **x9**, not x3+x3. Pick one mechanism as the primary dial per
  stat, or deliberately design the combination and document it — don't ship both defaulted to "aggressive" without
  accounting for this.
- **Run speed has NO native world-rate lever (confirmed again) — only existing-asset stacking, and the ceiling is
  low.** Queried the full live `SE_Stats` roster (74 loaded assets, `WubarrksEye_Dumps/2026-09-08_..._clean-VANILLA/
  Values_Dump.json`) for every nonzero `m_speedModifier`/`m_swimSpeedModifier`. Every positive (non-debuff) land-speed
  asset that exists in vanilla: `GP_Moder` +0.10, `Potion_hasty` +0.15, `TrinketIronStamina` +0.15. **Stacking every
  single one of them (all three simultaneously) tops out at +40% (1.4x), not 2x.** (Debuff assets — `Immobilized`
  family at -1000, `Puke`/`Slimed`/`Tared` at -0.5 — confirm the field is real and used elsewhere, they're not a path
  to a bigger positive number.) Swim speed has its own separate uncapped-looking asset, `TrinketChitinSwim`
  (`m_swimSpeedModifier` +0.5), unrelated to land run speed.
- **A genuinely isolated swim-stamina-only reduction asset exists (better than §3.5.4's candidate).** The live query
  found `Potion_swimmer` (`m_swimStaminaUseModifier` -0.5, run/jump untouched) and `TrinketChitinSwim`
  (-0.8, run/jump untouched) — both cleaner than the previously-listed `GP_Eikthyr` (-0.6, but bundles run+jump+swim
  together). Use `Potion_swimmer` or `TrinketChitinSwim` for a feature specifically scoped to "reduce swim stamina
  use," not `GP_Eikthyr`.

**Decision needed on run speed:** "2x default" is not reachable through the SE-grant mechanism as asked — the real
ceiling from every existing vanilla asset combined is ~1.4x. Recommendation: ship the roster system fully
config-driven (below) so the achievable ~+40% is the honest default, framed as "every vanilla speed buff stacked at
once," and leave room to raise it later without a code change if a future game update adds a bigger-magnitude asset.
Proceeding on this assumption; flag if a different number or approach is wanted.

### 4.2 Feature → mechanism map

| Ask | Mechanism | Ceiling / notes |
|---|---|---|
| Run speed multiplier | SE roster (§4.3), stack `GP_Moder` + `Potion_hasty` + `TrinketIronStamina` | ~+40% max, asset-fixed, not a continuous dial |
| Stamina regen multiplier | **Native `GlobalKeys.StaminaRegenRate`** (same class as shipped `CarryWeightRate`) as the primary world-wide dial; SE roster (`Rested`/`Warm`/`Potion_tasty`, each x2 → +100% each, additively stackable) as an optional secondary per-player layer | Native lever is a true arbitrary multiplier (x3 exactly = trivial); remember the two compound if both used |
| Swim stamina on/off | SE roster, `Potion_swimmer` (-50%) or `TrinketChitinSwim` (-80%), isolated from run/jump cost | Binary grant/revoke via the roster's per-SE toggle, not a numeric dial |
| "Map each SE to its own on/off, including potions, stack for a Wonderland effect" | SE roster (§4.3) — this IS the roster's whole design | Any of the 74 loaded `SE_Stats` assets can be added to the roster by name, each with its own config toggle |
| Default carry weight 2x | Already-shipped `WorldRatesEngine`/`CarryWeightRate` — just change the config default from 1.0 to 2.0 | Trivial, no new code |

### 4.3 New subsystem: the SE Roster (Buff Roster Engine)

New folder `Subsystems/StatusEffects/`, following the existing subsystem-registry pattern
(`Subsystems/WorldGovernor/`, `Subsystems/ItemFlow/`, etc.), registered in `WonderlandPlugin.RegisterSubsystems()`:

- **`StatusEffectRegistry.cs`** — on `OnWorldReady()`, walks `ObjectDB.instance.m_StatusEffects` once (same lifecycle
  already exploited for `ObjectDB.instance.m_items` in `WaterBuoyancyEngine.cs:86`) and builds a `name -> (nameHash,
  StatusEffect)` lookup, so the roster is configured by human-readable asset name (`"Rested"`, `"GP_Moder"`,
  `"Potion_hasty"`, `"Potion_swimmer"`, ...) rather than a magic int. Logs a warning and skips (not crashes) any
  configured name that doesn't resolve, so a typo or a renamed asset in a future game patch degrades gracefully.
- **`BuffRosterEngine.cs`** — holds the configured roster (each entry: asset name + enabled bool), and on a periodic
  tick (own interval config, independent of other subsystems' tick rates) iterates every connected player's character
  ZDO (`Core/Data/ConnectedCharacters.cs`, already exists) and calls the same `RPC_AddStatusEffect`-equivalent routed
  RPC with `resetTime:true` for every *enabled* roster entry — the permanence idiom from §3.1, since there is no
  removal RPC (§3.5.2, reconfirmed above): the tick interval must stay comfortably shorter than the shortest `m_ttl`
  among all roster-eligible assets, or a disabled-but-recently-granted effect could still be running out its own
  natural duration rather than actually being "off." Turning a roster entry off simply stops re-sending it; it fades
  out on its own `ttl` rather than being forcibly cleared (same limitation noted in §3.5.2 - a Wonderland toggle here
  is "stop renewing," not "instantly revoke").
- Config surface: one config entry per roster slot (`BuffRoster_Rested_Enabled`, `BuffRoster_GP_Moder_Enabled`,
  `BuffRoster_Potion_hasty_Enabled`, `BuffRoster_Potion_swimmer_Enabled`, etc.), all under a section master toggle
  (`BuffRosterEnabled`) per §4.4, so admins can flip individual buffs without touching code — directly answers
  "map each SE to its own always on/off."

### 4.4 Config infrastructure

- **Master on/off per section — extend the existing convention, don't invent one.** `WonderlandConfig.Bind()` already
  numbers sections (`"1 - General"`, `"2 - Vacuum & Auto-Harvest"`, ...) and every existing subsystem already has its
  own top-level `*Enabled` entry (`VacuumEnabled`, `ContainerRowsEnabled`, `StructureUpkeepEnabled`, etc.) gating the
  whole feature. The ask is already this project's standing pattern — just make sure every *new* 0.7.0 section
  (`BuffRosterEnabled`, and split `WorldRatesEnabled` out from the single `CarryWeightMultiplier` entry now that it's
  growing a `StaminaRegenRate` sibling) follows it too.
- **Config migration — already exists, just needs new entries.** `WonderlandConfig.MigrateLegacyConfig` +
  `TryMigrate<T>` (`Core/WonderlandConfig.cs:224-340`) already carries renamed keys across via BepInEx's
  `ConfigFile.OrphanedEntries` reflection, proven working across the 0.1.0→0.2.0 and pre-rebuild migrations. Nothing
  new to build here for 0.7.0 - just add a `TryMigrate` line if any 0.7.0 key renames a 0.6.x one (none currently
  planned to).
- **Hot-swap/refresh on file-change — genuinely new, confirmed not already provided by BepInEx.** Checked
  `libs-Tools/BepInEx.dll` directly (`strings` for `FileSystemWatcher`/`Reload`): `BepInEx.Configuration.ConfigFile`
  exposes a `ConfigReloaded` event and a manual `Reload()` method, but has **no built-in file-watching** — nothing
  calls `Reload()` on its own when the `.cfg` is hand-edited. Needs: a `System.IO.FileSystemWatcher` on
  `Config.ConfigFilePath` (set up once in `WonderlandPlugin.Awake()`, alongside where `Config` is already available),
  debounced ~300-500ms (editors/sync tools often fire multiple change events per save), calling `Config.Reload()` on
  settle. This requires zero per-setting plumbing beyond that: every existing engine that already subscribes to
  `SettingChanged` (e.g. `WorldRatesEngine.Initialize`, `Core/WonderlandConfig.cs`) fires automatically off
  `Reload()`, since `Reload()` re-parses the file and raises `SettingChanged` for every entry whose value actually
  changed - the "no server restart needed" property comes for free once the watcher exists. Anything that reads a
  config value directly instead of subscribing (audit before shipping) needs a `SettingChanged` handler added the
  same way `WorldRatesEngine.Initialize` already does it.

### 4.5 Concrete default-value proposal for 0.7.0

| Setting | Default | Mechanism |
|---|---|---|
| `CarryWeightMultiplier` | 2.0 (was 1.0) | Native `GlobalKeys.CarryWeightRate`, already shipped |
| `StaminaRegenRateMultiplier` (new) | 3.0 | Native `GlobalKeys.StaminaRegenRate` |
| `BuffRosterEnabled` (new, master) | true | — |
| `BuffRoster_GP_Moder_Enabled` | true | SE roster, +10% run speed |
| `BuffRoster_Potion_hasty_Enabled` | true | SE roster, +15% run speed |
| `BuffRoster_TrinketIronStamina_Enabled` | true | SE roster, +15% run speed (all three together = +40%, the honest ceiling — see §4.1) |
| `BuffRoster_Potion_swimmer_Enabled` | false | SE roster, -50% swim stamina cost, isolated — off by default since it's the specific "toggle" feature asked for, admin opts in |
| `BuffRoster_Rested_Enabled` / `Warm` / etc. | false | Available in the roster but off by default — granting free `Rested`/`Warm` server-wide is a bigger balance call than the movement/swim asks, left for the admin to opt into deliberately given the compounding note in §4.1 |

**Shipped in v0.7.0** (2026-09-11): the roster engine, native `StaminaRegenRateMultiplier`, the `.cfg` hot-reload
poll, and the three Discord lifecycle announcements are all implemented and boot-check verified on
`~/valheim-testbed` (profile `v1012-wonderland`) — clean init, all 8 roster assets resolved from
`ObjectDB.instance.m_StatusEffects` (97 assets indexed), zero exceptions through an idle sweep window, and a live
on-disk config edit was picked up within one 5s poll and correctly re-applied (`StaminaRegenRateMultiplier`
3.00x → 4.00x, re-broadcast via `SendGlobalKeys` with no restart). One real bug was caught and fixed by this
boot-check: `ObjectDB.instance` is frequently still `null` at the `ZNetScene.Awake` postfix this codebase treats as
"world ready" — `BuffRosterEngine` now retries lazily from `OnUpdate` each tick until `ObjectDB.instance` actually
exists, rather than depending on that one hook firing after `ObjectDB.Awake()` happens to have already run (a race,
not a fixed order — `WaterBuoyancyEngine`'s own `ObjectDB.instance` read at that same hook is masked by its
ZNetScene-prefab-scan fallback finding every item anyway, so this race was already latent there too, just never
surfaced). **Not yet exercised:** a real connected client actually receiving/seeing a granted effect — the RPC send
path and the roster's own bookkeeping are verified; a live client is needed to see the effect land, same "boot-check
clean, live client not yet exercised" gap this project always calls out explicitly rather than assuming.

---

## 5. Other Deep-Dive Candidates (unexplored native levers)

Surfaced while cataloging the `GlobalKeys` enum in section 3.4 - these are members of the SAME native, already-proven,
zero-client-install mechanism class as `CarryWeightRate`/`FoodRate`/`StaminaRegenRate`, just never investigated for
what Wonderland could build on top of them:

- **Death penalty governor** - `GlobalKeys.DeathKeepEquip` / `DeathDeleteItems` / `DeathDeleteUnequipped` /
  `DeathSkillsReset` / `DeathKeepInventory` are all native boolean world keys (same `GlobalKeyAdd` mechanism), entirely
  unexploited by Wonderland today. A config-driven "death penalty profile" subsystem is likely a cheap, high-value
  0.x release - needs a decompile pass to confirm exactly what each toggle does at the boundary (e.g. does
  `DeathKeepEquip` interact with the vanilla "skull icon, reclaim your stuff" run, or bypass it entirely).
- **Progression/build-unlock governor** - `NoBuildCost`, `NoCraftCost`, `AllPiecesUnlocked`, `NoWorkbench`,
  `AllRecipesUnlocked`, `WorldLevelLockedTools` - all native boolean keys, useful for a "creative weekend" or
  "streamlined building" server profile toggle, unexploited.
- **World level control** - `GlobalKeys.WorldLevel` (int 0-10) drives enemy scale/speed/armor/HP and mineable-object
  HP (full consumption-site list already in §3.4's table). Valheim normally advances this only on boss kills; whether
  it can be set/held by the server independent of boss-kill state (e.g. a "New Game+ " or "hardcore start" mode) is
  unexplored.
- **Environment/weather control for sailing** - `WaterVolume.CalcWave`/`EnvMan.instance.GetWind()` are already touched
  by the buoyancy work (section 2); a genuine deep dive into forcing specific weather/wind states server-side
  (calm-seas-on-demand, or a "storm event" admin command) hasn't been done.
- **Raid/event governor beyond boolean blocking** - `RaidBlockEnabled` already exists as an on/off; `GlobalKeys.
  EventRate` (already in the section 3.4 table) is a continuous native dial on top of that - a combined "raid
  intensity" config (frequency dial + biome/event blocklist together) is a natural v-next for `WorldGovernorSubsystem`.
- **Per-skill-type control** - `SkillGainRate`/`SkillReductionRate` are global (all skills at once, per §3.2); whether
  there's a way to target a SPECIFIC skill type server-side (vs. the blanket rate) is unexplored and would need its
  own decompile pass through the `Skills`/`SkillType` machinery.
- **Ship/sailing mechanics** - rudder turn rate, sail area effectiveness, ship top speed - likely the same
  client-owned-Character-adjacent trap as run speed (ships are `Ship`/`Rigidbody`-driven, probably client-computed
  like Character movement), but never actually checked; could turn up its own SE-stackable or GlobalKeys lever the
  way food/stamina did.
- **Structure/piece durability beyond the existing no-decay toggle** - `GlobalKeys.DurabilityRate` (8 confirmed
  consumption sites in §3.4) already covers weapon/tool durability loss; whether `WearNTear` piece health/decay has
  its own separate lever beyond the existing `StructureUpkeepEnabled` no-decay feature is unexplored.
