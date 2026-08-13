# ADR-0020: Melee-Based Movement

**Status:** Accepted — 2026-08-13 (wayfinder map #128, destination)
**Deciders:** @Binoui
**Amends:** ADR-0001 (gravity — §3, three-phase ramp → float-window-only)
**Related:** ADR-0015 (timing-based defense), ADR-0016 (keyboard-first input), ADR-0017 (facing), ADR-0018 (target lock), ADR-0019 (hit response), ADR-0021 (frame timing)

## Context

The map's movement arc ([#128](https://github.com/Binoui/SlopArena/issues/128)): ground movement [#136](https://github.com/Binoui/SlopArena/issues/136), air movement [#137](https://github.com/Binoui/SlopArena/issues/137), ledge options [#144](https://github.com/Binoui/SlopArena/issues/144), grounded by the movement audit [#135](https://github.com/Binoui/SlopArena/issues/135) and the netcode impact scan [#140](https://github.com/Binoui/SlopArena/issues/140). Keyboard-first digital 8-way input (WASD, `MoveX`/`MoveY`, SOCD-canceled −1/0/+1, ADR-0016): **no analog stick, no vertical input axes** — everything below is designed for that. Melee is the base; 3D + free camera means the spacing game differs (ADR-0015), so movement is timing- and commitment-driven.

## Decision

### 1. Terminology (locked)

- **SA Dash = the Shift-triggered invincible burst** — a *mechanic*, the shield substitute (SA has no shields): kept **in full** (burst 30-34, 15-tick i-frames, cooldown 44-60; Burst/ADR-0014 untouched). It is the defensive layer, not a locomotion tier.
- Melee's "dash" (locomotion tier between walk and run) is **NOT adopted under that name**. The new locomotion tier is called **Run**.
- Keeping the terms distinct in all docs/ADRs.

### 2. Ground movement ([#136])

- **Run cruise**: 12-tick sprint-hold + instant snap die → accel toward `RunSpeed` (soft start, `a·dir ± b` shape). Walk tier dies as a selectable speed (no analog on 8-way); the field survives only for air coupling → §3.
- **Dash ending changes**: 80 m/s² decel + hard stop → **friction coast** (the only dash-behavior change; everything else about the mechanic stays).
- **Pivot**: friction-through-zero (locomotion dash-dance); the standing turn keeps timed `TurnaroundTicks`.
- **Ground friction**: linear (`V -= sign·f·dt`, `VelocityDeadZone` snap, ~6-9 m/s²).
- **Dash-attack = attack-from-motion** (Q5=b): the attack gate opens to Dashing/Run with momentum preserved; dedicated dash-attack moves deferred past POC.

### 3. Air movement ([#137], completes the walk-death from §2)

- **Per-char `AirSpeedMax`**, decoupled from WalkSpeed: Manki 6.5 / FightGuy 7.5 / Kistu 8.5 / Nilus 7.0 (≈0.55-0.65 of run).
- **`AirAccelStick` + `AirAccelBase`** (base ≈ 0.2× stick); **linear friction** replaces multiplicative `AirDrag`; per-char `AirFriction` everywhere.
- **Fast fall = set-velocity**: per-char `FastFallSpeed` ≈ 1.15-1.25× `MaxFallSpeed`, `VY = -FastFallSpeed` on Down+airborne+falling (the SHFFL enabler); per-tick gate, no latch; `FastFallGravityMultiplier` deleted.
- **Jump model**: `ShortHopForce` own stat 0.58-0.62× `JumpForce` (release-window trigger kept); **double jump = weaker additive impulse** (`VY = JumpForce·AirJumpVMultiplier` ~0.8, `VX += dir·AirJumpHMultiplier` ~0.85).
- **Gravity — Option A (user choice)**: the float window survives **only** for recovery/post-hit states; `FallRampDuration` lerp machinery **deleted**; ADR-0001 amended (§3 ramp → float-window-only). ADR-0002 (jump-arc anim) untouched.

### 4. Ledge ([#144])

- **Ledge hang adopted, full kit deferred** (user: "not sure if we want full kit yet but we probably should have some sort of ledge hang").
- New **`LedgeHang` ActionState**: grab via the existing `TryLedgeSnap` geometry (0.8m cardinal search, ≤2.5m below the edge), **invincible 4-8 ticks on grab**, **full refresh on re-grab** (Melee-faithful; no anti-plank — small arenas make planking self-defeating; an Ultimate-style refresh cooldown is a balance-pass lever).
- **No auto-getup — hang until you act** (Melee; stalling is the edgeguarder's punish window).
- **Grab gate = actionable AND not flying** (`HitstunTicks == 0 && !HasKnockback`, unchanged): SA deliberately does NOT adopt Melee's tumble auto-grab ("lucky ledge grab") — in free-camera 3D a flight grab from an off-screen angle reads as random; recovery must let the flight decay (ADR-0019 §6) before the ledge is available.
- **The +5 auto-pop dies** — the hang replaces it (KV cleared, `VY = 0` on grab). Hang ≠ landing: excluded from landing-lag via the existing `VY <= 0` guard.
- **Escapes now** (all existing keys, zero new inputs): **S = drop, jump = ledge jump, W = stand**.
- **Getup attack + getup roll deferred** with the full-kit question → the landing/tech foundation (fog: missed-tech → knockdown → getup), the natural home for getup-state work.
- **Single-occupancy ledge (ledgehog) — assumed from Melee, flagged un-grilled**: one fighter per edge; a second grab fails and the would-be grabber falls past. `TryLedgeSnap` gains an occupancy check.

## Considered Options

- **Keep Melee's dash as a locomotion tier** — rejected by terminology lock: SA's Dash is a *mechanic*; Run takes the locomotion slot (#136).
- **Dash-end hard stop vs friction coast** — hard stop rejected: kills the run-out feel and dash-dancing; friction coast sanctioned explicitly (Q1=B) (#136).
- **Multiplicative AirDrag vs linear friction** — multiplicative rejected: asymptotic tail never settles; linear friction + dead-zone snap lands cleanly (#137).
- **Three-phase gravity ramp vs float-window-only** — the audit showed the ramp machinery mostly dead (recovery/post-hit only); Option A: float window for recovery/post-hit, ramp deleted (#137).
- **Full Melee ledge kit vs hang** — full kit not yet wanted; hang + basic escapes decided now, aggressive getups deferred (#144).

## Consequences

- **Cleanup**: remove `WalkSpeed`, `SprintThresholdTicks`, `IsSprinting`; add `ActionState.Run`. `TurnaroundTicks` + `DashDurationTicks` stay.
- **All §3 fields are MovementStats** — zero wire change (per-character static data).
- **`LedgeHang` = new ActionState value** — the state byte already exists on the wire (**0B netcode**); the rollback classifier marks it **Complex** (like AirDodging — never rebuilt from snapshots); invincibility is a server-side off-wire timer; landing-lag exclusion.
- **`TryLedgeSnap` → grab path** (state set, KV cleared, VY=0) replacing the pop, **+ occupancy check** (§4).
- Client: hang + drop/jump/stand anims + run pivot = asset work, deferred with the build.
- **Goldens regenerate** (movement snapshots shift).
- **Balance pass** (post-design): RunSpeed/accel, per-char air stats + friction curves, fast-fall/short-hop multipliers, weights.
- ADR-0001 amended (gravity); `docs/systems/combat-systems.md` updated.
