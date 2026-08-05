---
id: "nilus"
name: "Nilus"
title: "The Void Stalker"
status: "Implemented (sim) — art/anim pending"
archetype: "In-your-face controller. Denies the ground the opponent wants to retreat to, drags them back into it, and cashes out with ordinary knockback. Shortest reach on the roster; blinks are both its approach and its only recovery."
source_image: "TBD"
inspiration: "Kassadin (LoL) — Riftwalk blink as core mobility, void-blade melee-mage hybrid. Ruh Kaan (Battlerite) — clawed silhouette, the grasp that yanks a target to you. Void (Supervive) — rift-as-a-real-zone you fight around, cold violet VFX language."
palette:
  body: "near-black, matte"
  accents: "cold violet (~#7B4BD6) void energy"
  rim: "colder cyan rim on rift VFX — must never read as Manki's orange fire or Kistu's foxfire"
kit:
  - slot: "LMB"
    name: "Rift Claws"
    type: "melee"
    description: "3-hit claw chain (3/4/7). Hits 1-2 use deliberately low base knockback — 'sticky', the target stays in your space. 3rd hit launches."
  - slot: "Air LMB"
    name: "Void Rake"
    type: "melee"
    description: "2-hit air claw (3/5). Light then Launcher — juggle glue."
  - slot: "RMB"
    name: "Entropy Lance"
    type: "charge"
    description: "Chargeable void spear. Tap = 9 dmg Medium poke; charged = 15 dmg at Kill *magnitude* but a flatter angle (`Custom{15°, 18, 10}`, not the `Kill` profile's 20°). Single-target, not piercing. The kill move."
  - slot: "Air RMB"
    name: "Collapse"
    type: "charge"
    description: "Hold to charge a downward void slam. Tap = 10 dmg at 14 m/s descent; charged = 14 dmg at 18 m/s. Spike knockback. Edgeguard finisher."
  - slot: "Q"
    name: "Void Rift"
    type: "zone"
    description: "SIGNATURE. Lobbed void seed that passes straight through bodies and grounds itself, then leaves a lingering 3m rift for 4s dealing 3 dmg every 0.5s. No drag — see Resolution rules."
  - slot: "E"
    name: "Riftwalk"
    type: "mobility"
    description: "2-charge 6m blink, works airborne. 4 dmg burst on arrival. Primary recovery AND primary approach — the central tension."
  - slot: "R"
    name: "Nether Grasp"
    type: "cc"
    description: "Aimed claw, 8m capsule. On hit: 8 dmg, 12t stun (spec asks 20t; ApplyKnockback caps it), and knockback aimed at Nilus pulls the target ~4.1m in. The combo engine."
  - slot: "F"
    name: "Event Horizon"
    type: "ult"
    description: "1.2s telegraphed rift, drags every enemy within 6m for 60t (3 dmg per 10t pulse = 18 over the drag), then detonates for 18 dmg with Kill-class knockback (`Custom{40°, 16, 9}`). Pulses and detonation damage every target in radius. Dodgeable by dashing out."
---

# Nilus — The Void Stalker

> Status: **Implemented in sim** with placeholder art (FightGuy prefab, capsule hurtboxes). Art, animation and tuning are separate passes; see Ship Order.
> Inspired by: **Kassadin** (Riftwalk, void melee-mage) × **Ruh Kaan** (claws, the grasp) × **Void / Supervive** (rift as a real zone).

## Concept

A void-touched stalker — hooded, clawed, half-phased. It moves *through* space rather than across it.

Where **Manki** throws objects into space and **Kistu** contests space with a blade, Nilus **opens holes in space**: a rift that hurts to stand in, a claw that drags you out of position, and blinks that make its approach unreadable.

**The honest tension:** claws and a hood read *assassin*, so players will expect dive-and-burst. Nilus is not that — burst-execute is FightGuy's lane. The resolution is that **the claws are the glue and the void is the control**: claw strings keep you attached to the target, the rift punishes the ground they want to retreat to, and the kill comes from a charged void lance or the ult on ordinary Kill knockback.

## Archetype

**In-your-face controller.** It wins neutral by making retreat expensive, not by out-ranging or one-shotting.

| | Range | Wins neutral by | Kills with |
|---|---|---|---|
| Manki | mid/long | throwing threats at you | explosives, Overclock windows |
| FightGuy | close | mark → pursuit execute | Dragon's Kick on a marked target |
| Kistu | mid | disjointed reach, juggles | charged spin / spike |
| **Nilus** | close | denying the retreat, dragging you back | charged Entropy Lance, Event Horizon |

## Design Pillars

### Void kills with knockback, like everyone else

**No 0% abilities.** Every slot deals real damage on the existing anchors (light hits 3-7, charged heavy 15-16, ult finisher 18). Pull, drag and teleport are **setup and glue** — they buy the hit, they never replace it.

This was an explicit design correction: an earlier draft made displacement the win condition (kill by shoving opponents into the blast zone instead of by knockback). That would have forked the game's combat model for one character. Rejected. It also removes the hardest tuning risk — drag forces no longer have to be balanced against knockback and hitstun, because drag is deliberately weak (1.5 m/s, a dash always beats it).

### Predictable knockback, emergent combos

Inherited from Kistu and the game as a whole. Every ability has consistent, learnable knockback. No ability is wired to feed into another — the Grasp → Rift → claw-string → launch loop emerges because the values are readable, not because it is scripted.

### Mobility is the weakness

Riftwalk is both the approach and the only recovery. This is deliberate, and is the character's defining flaw (Kassadin's own tension): spend both charges getting in, and the first off-stage hit kills you.

## Kit

| Slot | Name | Dmg | Knockback | CD | Notes |
|---|---|---|---|---|---|
| **LMB** | Rift Claws | 3 / 4 / 7 | `Custom{12°, 1.5, 1}` x2, **Launcher** | 0 | 3-hit claw chain, 28/28/38t, stun ladder 16/18/28t. Hits 1-2 sit *below* the `Light` profile's base 2 — that is the stickiness |
| **AirLMB** | Void Rake | 3 / 5 | Light, Launcher | 0 | 2-hit air claw, 24/30t, stun ladder 16/26t. Juggle glue |
| **RMB** | Entropy Lance | 9 tap / **15** charged | Medium / Kill *magnitude* at a flatter angle: `Custom{15°, 18, 10}` | 60t | `ChargeAttack`, `ChargeHoldTicks=50`, stage 0 is a 300t hold safety net, then 30t tap / 44t charged. Stun 22 tap / 40 charged. Long thin capsule, 2.2 m, **single-target** — `RehitIntervalTicks` is unset on the HitboxEvent, so `SpellResolver.cs:248-251` deactivates it and breaks after its first victim. **Kill move** |
| **AirRMB** | Collapse | 10 tap / **14** charged | Spike (−45°) | 0 | `ChargeAttack`, `ChargeHoldTicks=45`, 60t hold stage, then 36t tap / 36t charged. Tap `Stages[1].MoveY = -14`, charged `ChargedStages[0].MoveY = -18` — `AirChargeAttack` drives Nilus down the whole attack, the only class that honours the field. Edgeguard finisher |
| **Q** | Void Rift | 3 per 0.5s | `{15°, base 2, growth 1}` | 600t | **Signature.** Lobbed void seed (ignores bodies) → grounds → lingering r=3m rift, 4s (240t), damage tick every 30t, 6t stun per tick. 24 total on someone who stands in all of it, *in isolation* — a pulse landing on the same tick as another Nilus hitbox is dropped for a full 30t, so the effective total inside the advertised claw string is below 24 (see Resolution rules). No drag (see Resolution rules) |
| **E** | Riftwalk | 4 on arrival | Light, r=1.6m, 12t stun | 2 charges, 300t regen | 6m blink, works airborne. Runs its authored **8t** in full: the duration is cached from `Stages[0]` at `OnStart` rather than compared against the `AnimLockTicks` down-counter (that idiom ends an ability at `ceil(N/2)` — `KistuRisingSlash` and `KistuCounter` still carry it and are out of scope here). `burst_tick` 4 therefore sits mid-window, not on the last tick. **Primary recovery** |
| **R** | Nether Grasp | 8 | **inward** + 12t stun | 480t | 8m claw capsule (`AttackRange` 9m), **34t** commitment run in full (same cached-duration fix as E). Knockback aimed *at Nilus* pulls the target ~4.1m in. `pull_stun_ticks` asks 20 but `ApplyKnockback` caps hitstun at 12 for this magnitude; the HitboxEvent's own `StunTicks = 20` is inert too, because its knockback magnitude is 0 and the ability's own `ApplyKnockback` call decides. Combo engine |
| **F** | Event Horizon | 3/pulse + **18** | drag (`drag_force` 3), then **Kill**-class `Custom{40°, 16, 9}`, `detonation_stun_ticks` 40 | 540t | 1.2s telegraph (72t), then a 60t drag pulsing every 10t for 3 (6 pulses = 18), then detonates outward-up on tick 132. Pulses (`RehitIntervalTicks = 1`) and detonation (`= 5`) each damage **every** target inside `drag_radius`, not one arbitrary victim. 36 total on a target that never leaves |

Damage and knockback conventions follow `KistuData.cs` (light chain 3/3/4/6, charged kill 16 with `Custom{15°, 18, 10}`, ult 540t cooldown, charge pools via `Params["max_charges"]` / `["charge_regen_ticks"]`).

### Resolution rules

Four cases that the kit table leaves open, resolved explicitly so implementation has no judgement call to make. All are server-authoritative.

**Riftwalk vs terrain.** The blink does **not** phase through arena geometry: it traces the path from the origin toward the full-distance destination in 0.25 m increments and **stops at the last valid position**. A candidate is invalid when the surface under it would put Nilus inside the geometry (`surfaceY + capsuleHalf > PY + Simulation.PlatformSnapTolerance`) — the same 0.5 m tolerance ground resolution uses, so steps and ramps the sim *would* snap him onto stay traversable and only rises it would not snap stop the blink. This is required, not belt-and-braces: there is **no** force-snap-up outside hitstun. The one at `Simulation.cs:350-357` sits inside the `ActionState.Hitstun` branch, and a blinking character is `Attacking`, so it takes `Simulation.cs:363` and snaps only within `PlatformSnapTolerance`; anything deeper falls to `IsGrounded = false` and gravity carries it *through* the stage to the blast zone. Verified empirically: blinking 6 m into a 3 m rise landed at `PY=0.825, IsGrounded=false` and was at `PY=-2.181, VY=-13.883` sixty ticks later. Blinking past the stage edge samples `float.MinValue`, which is **valid** — `IsGrounded = false` and Nilus falls; that is the risk that makes Riftwalk-as-approach a real commitment. Either way the charge is spent and the arrival burst fires, at the **final** position rather than the intended one. The ability reads the heightmap through `ServerAbility.Arena`, injected by `ServerSimulation.ActivateAbility` (`ServerSimulation.cs:72`) — that one line is the whole terrain rule's load-bearing dependency, and with it absent `TraceDistance` returns the untraced full distance and every blink phases silently. See `## Files`.

**Consequence — Riftwalk recovers horizontally, not vertically.** The validity test is evaluated once against the *caster's* height, so once Nilus has fallen more than `PlatformSnapTolerance` below the stage plane every on-heightmap candidate is invalid and the blink stops at the stage boundary instead of carrying him back in. Recovering from below is therefore a double-jump first (`MaxJumps` 2, `FloatWindowTicks` 40 — the longest float window on the roster), *then* a blink in at stage level. This is not a bug to fix: the alternative is letting the blink resolve against the destination's own ground, which is exactly the phasing this rule forbids and would hand him a free recovery from anywhere under the stage. Flag it at playtest — if he proves unrecoverable, the lever is the float window or a vertical component on the blink, not the terrain rule.

**Nether Grasp is knockback, not a velocity write.** The yank is implemented as `Simulation.ApplyKnockback` (`Simulation.cs:906`) with the direction pointing from the target **toward Nilus**, a small positive angle, and a magnitude (`pull_force` 9.5) that travels ~4.1m under `KnockbackDrag` — measured, from a 6m grab on flat ground against a Nilus-sized target. The distance depends on the *target's* capsule: the shorter test dummy (`TestHelpers.CombatDef`, **1.5m** — it clones `MankiDef`, `MankiData.cs:33`; the `1.3m` in the comment on `TestHelpers.CombatGroundPY` is stale, and every golden settles its NPC at `PY = 0.75`) travels 4.55m from the same grab, because `pull_angle` lifts it clear of ground friction for longer. The force→distance table in R's `Params` comment (`NilusData.cs`) was measured against that same 1.5 m dummy and does not say so — re-measure against the target height you care about before trusting it. A plain `target.VX` write would be silently erased: `ProcessHitstun` overwrites `VX`/`VZ` from `KVX`/`KVZ` every tick (`Simulation.cs:470-471`), so knockback velocity is the only channel that survives. Consequence: the yank *is* the hit — one `ApplyKnockback` call delivers pull, stun and hitstun together, and it applies identically to grounded and airborne targets (`pull_angle` is **+8°**, so the target is pulled **up**-and-in — it spends the drag inside its own float window, so yanking someone toward a ledge takes them over it airborne; that is the intended anti-air answer). Pulling a recovering opponent stageward is accepted counterplay: the reward for the read is the follow-up, not the stock.

**Event Horizon locks the caster.** Nilus is locked in place for the entire ability (`VX = VZ = 0` every tick), exactly like `FightGuyTempest`. This is what makes a 6m drag into a Kill detonation fair — the telegraph is the commitment. It is also what makes the drag *implementable*: because Nilus stays in `ActionState.Attacking` for the whole ult, the ability instance survives to run its own per-tick loop over `SimulationStates`. A target that dashes out during the drag keeps the tick damage already dealt and takes nothing else — note that this is purely because it left `drag_radius`, not because dash grants invulnerability: `CharacterState.InvincibilityTicks` is set by dash (`Simulation.cs:880`) and decremented (`:404`) but is **never consulted in hit resolution**, engine-wide. The lock is against *dash* only: `AnimLockTicks` is set to `windup + drag + 1 = 133` and `Simulation.cs:252` refuses a dash while it is non-zero, but jump detection (`Simulation.cs:220`) is gated only on hitstun and `JumpsLeft`, so a jump on any tick — including 132 — drops the ability instance without `OnEnd` and still charges the full 540t cooldown. That hole is engine-wide (`FightGuyTempest` has it too) and is **out of scope for this branch**; it is recorded here so nobody reads "the telegraph is the commitment" as a guarantee. Fix it by adding `s.AnimLockTicks == 0` to the jump condition, which changes jump-cancel feel for every character and needs its own pass.

**The Void Rift seed does not interact with bodies.** The seed hitbox is spawned with `IgnoresEntities`, so `SpellResolver` skips the entity scan for it entirely: it cannot be deactivated by clipping a player in flight, and it produces no `HitResult`. Both halves matter. Without it, a mid-flight body contact deactivated the seed and the expiry path queued its `ProjectileExplosion` at the **pre-move mid-air position**, so the 3 m rift hung in the air for its full 240 ticks instead of reaching `CheckGroundCollision` — Q's whole premise is that it denies *ground*, so an airborne rift is not a variant, it is a different ability. And the seed's `HitResult` carried `Damage = 0`, zero knockback and `StunTicks = 0`, which drives `ApplyKnockback` down its else branch to `State = ActionState.Idle`, discarding whatever ability the victim was running while still charging its cooldown — a free ability-cancel on a projectile documented as inert. With the seed inert in the literal sense, the rift always lands at ground level and the only thing Q ever does to a body is the rift's own pulses.

## Stats

Positioned between Manki (9/12/30) and Kistu (11/15/34). Deliberately not the fastest runner — its mobility is the blink. Floatier than the rest of the roster, which also makes the airborne Riftwalk recovery read as intentional.

```csharp
WalkSpeed = 10f,  SprintSpeed = 13f,  DashSpeed = 32f,
AirAcceleration = 17f,  JumpForce = 12f,  Gravity = 34f,   // floatier than Kistu's 36
AirFloatGravity = 0f,
DashDurationTicks = 15,  DashCooldownTicks = 48,
GroundFriction = 15f,  AirFriction = 0.45f,  MaxFallSpeed = 46f,
MaxJumps = 2,  JumpSquatTicks = 5,
FloatWindowTicks = 40,  FallRampDuration = 12,             // longest float window on the roster
```

Body: `CapsuleRadius = 0.33f`, `CapsuleHeight = 1.65f`, `HipHeight = 0.8f`, `HurtboxRadius = 1f`.

## Gameplan

Rift the ground they want to stand on → **R** yanks them into it → claw string keeps them sticky inside the rift's damage ticks → 3rd claw launches → Void Rake juggles → cash out with charged **Entropy Lance**, or spike with **Collapse**. **F** when they are cornered near a ledge.

## Weaknesses

- **Shortest reach on the roster** — shorter than Kistu's blade. You must spend a Riftwalk charge to get in.
- **Riftwalk is offense and recovery** — spend both charges attacking and the first off-stage hit kills you.
- **The Rift is single-instance and loudly telegraphed** — a patient opponent walks around it. You have to *force* them into it with R rather than camp it.
- **No counter, no armor, no reflect.** Kistu holds the roster's one counter slot.
- **Setup-dependent damage** — without a landed Grasp or a cornered opponent, the kit does chip damage.

## Engineering

Three costs. Half the kit is existing infrastructure. The new sim surface is small but it is not "a single field": two new `Hitbox` fields (`RehitIntervalTicks`, `IgnoresEntities`) plus `RehitIntervalTicks` on `ProjectileExplosion`, and the arena-injection plumbing Riftwalk's terrain rule depends on (`ServerAbility.Arena`, one assignment in `ServerSimulation.ActivateAbility`, and `Simulation.PlatformSnapTolerance` promoted to `public`). All of it is additive and inert for Manki, FightGuy and Kistu; see `## Files` for the full list.

| Item | Approach |
|---|---|
| **Lingering rift (Q)** | Needs **two new sim fields**: `ushort RehitIntervalTicks` on `Hitbox` and on `ProjectileExplosion`, plus `bool IgnoresEntities` on `Hitbox` for the seed. The rehit implementation is a **stateless per-hitbox age gate**, not per-target bookkeeping: `bool pulse = !isZone \|\| (hb.AgeTicks % hb.RehitIntervalTicks == 0)` (`SpellResolver.cs:196`), with no per-target map anywhere. `0` preserves today's one-hit-then-die behaviour (`SpellResolver.cs:195-196`) so nothing existing changes. The simplicity is the right call, but it has a property a per-target design would not: `hitThisTick` is shared across **every** hitbox in the tick and hitboxes are walked newest-to-oldest, so a rift pulse whose target was already claimed by another of Nilus' hitboxes is lost for a full `RehitIntervalTicks` (30) rather than retried next tick. Q then reuses the whole `MankiRoundBomb` chain unmodified: lobbed seed (a raw gravity-carrying `Hitbox`, exactly as `MankiRoundBomb` builds — the `ProjectileConfig` struct in `AttackData.cs` has zero usages repo-wide) → `CheckGroundCollision` grounds it → its `ProjectileExplosion` (which already has `DurationTicks`, `AttackData.cs:122`) becomes the rift |
| **Enemy yank (R)** | `Simulation.ApplyKnockback` (`Simulation.cs:906`) with the direction inverted to point at Nilus. **Not** a velocity write — `ProcessHitstun` overwrites `VX`/`VZ` from `KVX`/`KVZ` every tick (`Simulation.cs:470-471`), so only knockback velocity survives. Zero new mechanics |
| **Zone visibility in PvP** | **Deferred, known gap.** `NetworkSimulationBridge.cs:51` returns a null resolver, so a remote client cannot see the rift. Local/training renders fine via `ProjectileVFXManager`. The 600t cooldown exceeding the 4s duration means at most one rift ever exists, which keeps the eventual sync to a single zone record |

**Why the rift cannot live inside the ability:** `ServerSimulation.cs:143` discards an ability instance the moment `state.State != ActionState.Attacking`. `FightGuyTempest`'s per-tick `SimulationStates` loop only works because Tempest locks the caster in `Attacking` for its whole duration. A placed rift must outlive the cast, so it has to live in the hitbox layer — which is already detached from ability lifetime and already aged centrally by `SpellResolver.Tick`. F's drag *does* use the Tempest pattern, because F locks the caster.

New ability classes: **4** (`NilusVoidRift`, `NilusRiftwalk`, `NilusNetherGrasp`, `NilusEventHorizon`).
Reused: LMB → `LmbCombo`, AirLMB → `AirLmbCombo`, AirRMB → `AirRmbAttack`. The first two needed zero new code. `AirRmbAttack` needed one: Collapse is the first stage in the game to declare `AttackStage.MoveX/MoveY/MoveZ`, and nothing read that field, so the slam silently did not descend. `AirRmbAttack.Tick` now writes the non-zero components every tick (a single write at start is eaten — `ServerSimulation.ActivateAbility` zeroes downward `VY` on activation and gravity re-integrates each tick). Behaviour-neutral for Manki, FightGuy and Kistu: none of them declares the field.
RMB reuses the hold-to-charge lifecycle, but `ChargeAttackAbility` is **abstract** (`ChargeAttackAbility.cs:21`) — its only concrete subclass was `KistuChargeAttack`, a 2-line class whose entire body applies `stage.LungeForce`. That behaviour is not Kistu-specific, so **`KistuChargeAttack.cs` is renamed to `LungeChargeAttack.cs`** and shared by both characters rather than copied. `ChargeAttackAbility.cs` itself is untouched by this branch. Nilus' RMB then needs no new class.

**Ability lifecycle (E, R):** both cache `Stages[0].DurationTicks` in a field at `OnStart` and end on `_ticks >= _duration`, **not** on `_ticks >= s.AnimLockTicks`. `Simulation.TickTimers` decrements `AnimLockTicks` every tick *before* `TickAbilities` runs, so an up-counter and that down-counter cross at `ceil(N/2)` — the idiom silently halves an ability's authored duration (E would end on tick 4 of 8, R on tick 17 of 34). `KistuUltFlurry` and `AirRmbAttack` already demonstrate the correct form in-repo; `KistuRisingSlash` and `KistuCounter` still carry the halving form and are deliberately **not** touched here, because they are shipped behaviour on `main`. The reason this matters beyond duration: `EndAbility` sets `State = Idle` without clearing `AnimLockTicks`, and `ProcessNormalMovement` has no `AnimLockTicks` guard, so an ability that ends early hands the caster free movement for the rest of its own lock — which is what used to let Nilus walk out of the second half of a 34-tick grab and regain air control mid-blink.

**Charge-pool caveat (E):** `ChargeStockSpent` is a single per-entity counter shared by all slots. Riftwalk is the only Nilus slot that uses it, so there is no conflict — but a future Nilus ability must not add a second charge pool without splitting that field.

## Ship Order

Copy the Kistu pattern exactly. First pass sets `ModelResourcePath = "Characters/FightGuy"` and `BakedDataPath = ""` (capsule hurtbox fallback), so the entire kit is playable and tunable in sim **before any art exists**. Art is an independent second pass.

## Art Direction

Hooded, masked humanoid with clawed forearms and a tattered lower silhouette. No cape, no floating props (geometry rule — see `docs/contributing/conventions.md`).

**All void energy is VFX, never geometry.** The model stays a clean Mixamo-compatible humanoid so the existing rig and bake pipeline apply unchanged.

## Animation Needs (soft constraint)

Every clip is a stock humanoid motion; nothing exotic.

| Clip | Motion |
|---|---|
| `spell_lmb_1/2/3` | 3 claw swipes — right, left, rising double-swipe |
| `spell_lmb_air_1/2` | 2 air claw rakes |
| `spell_rmb_loop` / `spell_rmb_attack` | charge hold stance → forward two-hand thrust |
| `spell_rmb_air` | downward two-hand slam |
| `spell_q` | one-hand cast pointed at the ground |
| `spell_e` | brief crouch-vanish (only 8t, can be minimal) |
| `spell_r` | reach-and-grab, arm extended (Mixamo "reaching" / "throw" retargets fine) |
| `spell_f` | arms-up channel → outward burst |

Plus the standard locomotion set every character needs: `idle`, `run`, `jump_up`, `fall`, `dash`, `hit_small/medium/hard`.

## Open Decisions (implementation-time only)

Kit is fully specified. Remaining items are tuning and art, not design.

1. **Numbers** — all values above are first-pass. Measured as shipped, for the tuning pass to argue with:
   - One rift, target standing in it the whole time and hit by nothing else: **24%** (8 pulses x 3 at a 30t interval over 240t). Inside the gameplan this document actually advertises — claw string on a target standing in the rift — the effective total is **below 24%**: `hitThisTick` is shared across every hitbox in a tick and hitboxes are walked newest-to-oldest, so a claw landing on the same tick as a pulse voids that pulse for a full 30t. Nothing pins the combined figure; measure it before tuning against 24.
   - Grasp from 6 m: target ends **4.08 m** closer (Nilus-sized 1.65 m target; **4.55 m** against the shorter 1.5 m `TestHelpers.CombatDef` dummy — the distance is target-height dependent, and so is the force→distance table in R's `Params` comment, which was measured against that same 1.5 m dummy without saying so). The force-to-distance curve is steep and nonlinear because `pull_angle` lifts the target off the ground, so friction never brakes the tail.
   - Event Horizon on a target that never leaves the radius: **36%** (18 drag + 18 detonation), launched ~13.7 m up and ~18.8 m out.
   - The drag itself is weak on purpose: it closes only ~1 m (5.00 m → 4.03 m) over its 60 ticks. A dash always beats it.
2. **`pull_stun_ticks` is inert above 12.** `ApplyKnockback` caps hitstun at `min(8 + kbMagnitude * 0.5, stunTicks)`, which is 12 at `pull_force` 9.5. Lowering the spec value below 12 shortens the grab; raising it does nothing.
3. **Sticky claws** — hits 1-2 achieve stickiness with `Custom{12 deg, base 1.5, growth 1}`, below the `Light` profile's base 2 (zero new mechanics). If that feels insufficient in play, upgrade to a genuine small inward pull via `SimulationStates`.
4. **Palette** — exact violet/cyan values, and whether the mask has a visible face.

## Files

| File | Change |
|---|---|
| `src/Shared/Hitbox.cs` | add `RehitIntervalTicks` and `IgnoresEntities` |
| `src/Shared/AttackData.cs` | add `RehitIntervalTicks` to `ProjectileExplosion` |
| `src/Shared/SpellResolver.cs` | stateless per-hitbox pulse gate on `AgeTicks` (**not** per-target bookkeeping) + skip the entity scan for `IgnoresEntities`; `0` / `false` keep current behaviour |
| `src/Shared/Abilities/ServerAbility.cs` | add `ArenaDefinition? Arena` — a net-new nullable property on **every** ability in the game, documented `MAY be null`. Riftwalk's terrain rule is the only consumer today |
| `src/Shared/ServerSimulation.cs` | `ability.Arena = _arena` in `ActivateAbility` (`:72`). Without this single line `NilusRiftwalk.TraceDistance` takes its no-arena fallback and every blink phases through geometry, silently and with no test failure |
| `src/Shared/Simulation.cs` | `PlatformSnapTolerance` `private const` → `public const` — the blink's validity test deliberately reuses the sim's own 0.5 m tolerance instead of guessing a second one |
| `src/Shared/CharacterDefinition.cs` | `CharacterClass` enum entry (append after `Kistu` — ordinals are positional) + `BuildRegistry` arm at index 4 |
| `src/Shared/Characters/NilusData.cs` | new — stats + all 8 `AbilitySpec`s |
| `src/Shared/Abilities/NilusVoidRift.cs` | Q — lobbed seed (marked `IgnoresEntities`, so it never touches a body) whose explosion is the lingering rift |
| `src/Shared/Abilities/NilusRiftwalk.cs` | E — charge-pool blink + arrival burst |
| `src/Shared/Abilities/NilusNetherGrasp.cs` | R — aimed claw, inward knockback |
| `src/Shared/Abilities/NilusEventHorizon.cs` | F — telegraph → drag → Kill detonation; drag pulses and blast carry `RehitIntervalTicks` so they damage every target in radius |
| `src/Shared/Abilities/AbilityFactory.cs` | one `CharacterClass` arm + one private `(slot, airborne)` method |
| `src/Shared/Abilities/KistuChargeAttack.cs` → `LungeChargeAttack.cs` | `ChargeAttackAbility` (untouched, still abstract) had one lunge-only concrete subclass; it is renamed and shared instead of copied, so RMB needs no new class |
| `src/Shared/Abilities/AirRmbAttack.cs` | honour `AttackStage.MoveX/MoveY/MoveZ` per tick — Collapse's slam is the only declarer |
| `tests/Shared.Tests/NilusAbilityTests.cs` | 31 behaviour tests across all eight slots |
| `tests/Shared.Tests/RehitZoneTests.cs` | new — 6 tests, the only direct coverage of the branch's new sim primitive (zero-interval legacy path, pulse cadence, expiry, multi-entity pulse, interval > duration, interval 1) |
| `tests/Shared.Tests/NilusKitRegressionTests.cs` + `Golden/Nilus_*.json` | 8 golden snapshots — **nothing enumerates `CharacterClass`**, so no harness adopts Nilus automatically |
| `tests/Shared.Tests/TestHelpers.cs`, `KitScenarioTests.cs` | `NilusDef` / `NilusGpy` accessors, the hand registration the harness needs |
| *build step* | `dotnet build src/Shared/ --nologo` — Unity consumes `src/Shared` as a prebuilt DLL, so the client cannot see Nilus until this runs |

**No client UI edit needed.** Character select is enum-driven via `Enum.GetValues` (`CharSelectController.cs:23-32`), so Nilus appears automatically. The `ClassSelectUI` named in older docs does not exist.

**Not selectable in dedicated-server PvP yet.** `ServerApp/Program.cs:47` and `src/Server/MatchInstance.cs:79-80` hardcode `CharacterClass.Manki`, and `CharacterClass` is not carried in any packet. Out of scope here; Nilus is playable in local/training.

See `docs/characters/character-kit-design-principles.md` for design patterns and `docs/systems/combat-systems.md` for universal combat mechanics.
