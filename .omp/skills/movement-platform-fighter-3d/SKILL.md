---
name: movement-platform-fighter-3d
description: >-
  SlopArena — Smash/DKO-style 3D platform fighter movement & combat.
  ACTIVE: Unity 6 (6000.0.78f1) C#. Camera-relative 8-direction movement,
  Smash-style % system, dash with invincibility, double jump.
category: game-dev
tags:
  - unity
  - csharp
  - platform-fighter
  - class-system
  - open-source-gamedev
  - arena-system
  - smash-bros
  - dko
---

# SlopArena — Smash/DKO-Style 3D Platform Fighter (Unity C#)

**Repo:** https://github.com/Binoui/SlopArena
**Active branch:** `main` — Unity 6 (6000.0.78f1) C#
**Design ref:** Super Smash Bros (%, knockback scaling) + Divine KO (void arenas, 3D platform fighter format)

## Core Mechanics

### Camera-Relative Movement

Movement is RELATIVE to the camera orbit direction, not world-space:

| Input | Movement |
|-------|----------|
| W | Forward (in camera direction) |
| S | Backward |
| A | Strafe left |
| D | Strafe right |

- Input snaps to **8 directions** (45° increments): `BuildInputState()` builds a raw camera-relative direction from WASD (`camForward`/`camRight` from the camera), then rounds to the nearest cardinal or diagonal.
- The camera is a **world sibling**, not a child of the character; its yaw is ABSOLUTE and mouse-only (LMB/RMB are attack buttons, not camera toggles — the camera always orbits). See `docs/architecture-overview.md`.

### Double Jump

- `MaxJumps = 2` for all classes (`MovementStats`), jumps reset on ground contact and on landing.
- Ground jump → `JumpSquat` state for `JumpSquatTicks`, then `VY = JumpForce` on expiry (horizontal momentum carries through the squat).
- Air jump (double jump) sets `VY = JumpForce` immediately and snaps horizontal velocity to the input direction at `WalkSpeed`.

### Smash-Style % System (No HP)

```csharp
public ushort DamagePercent;  // 0-999
```

- Taking damage increases `DamagePercent` (capped at 999). No death by HP depletion — only arena void/kill height kills.
- **Knockback formula** (`Simulation.ApplyKnockback`):
  ```
  magnitude = baseKB + growthKB * (DamagePercent * 0.01)
  // 0% → baseKB, 100% → baseKB + growthKB, 200% → baseKB + 2*growthKB
  ```
  A fixed launch angle (`sbyte angleDeg`, -90 to 90) splits the magnitude into KVX/KVY/KVZ.
- Respawn resets `DamagePercent = 0`.

## Input

`InputController.Poll()` (`client/Unity/Assets/Scripts/Runtime/Input/InputController.cs`) reads `Keyboard.current` / `Mouse.current` each frame → `BuildInputState()` produces the `InputState` that crosses the wire:

```csharp
public struct InputState {
    public float MoveX, MoveY;      // camera-relative analog movement
    public bool Up, Down, Left, Right;
    public bool Jump, Dash, Crouch, IsAiming;
    public byte ActiveSlot;         // 0=none, 1=LMB, 2=RMB, 3=Q, 4=E, 5=R, 6=F
    public short FacingYaw;         // degrees × 100 (informational — sim overwrites)
    public short AimYaw;            // degrees × 100, combat-facing (camera)
    public short AimPitch;          // degrees × 100, camera vertical aim
    public ushort AimDistance;      // cm (0-6500), target distance for throws
    public byte TargetEntityId;     // soft-lock target (0 = none)
}
```

NPCs bypass keyboard input: `InjectAI(InputState)` switches the controller to AI mode (or `TrainingMatch.BuildNpcInput` builds NPC input directly).

## Movement Processing (server)

All movement runs in `ServerSimulation.SimulateTick` (`src/Shared/Simulation.cs`) — **the client never simulates movement in Phase 1**; it renders the latest server state via `PlayerRenderer.ApplyServerState`. Pipeline order per `Simulation.cs`:

1. Tick timers (cooldowns, dash duration, invincibility, anim lock, hitstun)
2. Hitstun (DI window — knockback applied immediately, `HitstunLevel` tier drives the anim)
3. Knockback (overrides everything; dash invincibility still applies)
4. Warp (velocity override toward warp target; otherwise skip to 5)
5. State processing: `Dashing` → decaying speed; `AirDodging` → air-dodge; `Attacking` is driven by `ServerSimulation.TickAbilities` (lunge force per stage)
6. Buffered slot consumption (attack lock just expired)
7. Jump detection (ground squat / air double jump)
8. Input-driven actions: dash (`input.Dash`) then attack (`input.ActiveSlot`)
9. Normal movement: `ProcessNormalMovement` (ground — friction, instant speed toward `WalkSpeed`/`SprintSpeed`) or `ProcessAirMovement` (air — `AirAcceleration` toward `WalkSpeed`), then `ApplyGravity` (three-phase, below) and ground/ledge collision.

## Dash

Per-character, data-driven via `MovementStats` (`src/Shared/Characters/<Name>Data.cs`):

| Character | DashDurationTicks | DashCooldownTicks |
|-----------|-------------------|-------------------|
| Manki | 15 | 60 |
| FightGuy | 18 | 48 |
| Kistu | 16 | 44 |
| Nilus | 15 | 48 |

- **Full-duration invincibility**: `Simulation.DashInvincibilityTicks = 15` (const) applied in `StartDash` — `s.InvincibilityTicks = DashInvincibilityTicks`, "invincible for full dash". While invincible the hurtbox ignores incoming hits.
- **Ground or air** — Shift starts a dash in both; the old air-dodge system is gone (dash replaces it). `VY = max(VY, 0)` on ground, `VY = 0` in air (stops vertical momentum).
- **Direction** — locked on activation from camera-relative input (8-dir); no input → dash toward `FacingYaw`.
- **Decaying speed** — dash velocity starts at `DashSpeed` and decays smoothly each tick (no slide at end: velocity zeroed on expiry).
- **Interrupts attacks** — `StartDash` clears the attack slot / anim lock and the sim deactivates the active `ServerAbility`; dash is blocked while in hitstun, already invincible, or carrying knockback.
- **No momentum carryover** — when the dash timer expires, `VX = VZ = 0` explicitly.

## Gravity & Knockback

### Three-phase gravity

`ApplyGravity` in `Simulation.cs`, driven by `MovementStats` fields on `CharacterDefinition`:

1. **Float** — while `AirTimeTicks < FloatWindowTicks`: gravity = `AirFloatGravity` (0 = float).
2. **Ramp** — while `AirTimeTicks < FloatWindowTicks + FallRampDuration`: lerp `AirFloatGravity → Gravity` by ramp progress.
3. **Full** — after the ramp: gravity = `Gravity`.

Per-character float windows give each class its fall profile (Manki floaty, others snappier).

### Knockback

- **Linear drag** — `Simulation.KnockbackDrag = 24f` fixed deceleration per tick (not exponential decay): fast initial launch, heavy slowdown approaching max range (Smash/DKO feel). Minimal gravity during flight: `KnockbackMinGravity = 2.0f`.
- **Launch** — `ApplyKnockback` uses the Smash-style magnitude above with a fixed angle; `KVY > 0` breaks ground contact so ground snap can't eat vertical launch.
- **Hitstun tiers** — `HitstunLevel` set at hit time from raw damage: `< 5` → 0 (light), `5-14` → 1 (medium), `≥ 15` → 2 (hard). Maps to `hit_light`/`hit_medium`/`hit_hard` clips and drives DI (`DIX`/`DIY` stored during hitstun, applied on expiry). See `docs/systems/hitstun-di.md`.
- Landing naturally clears knockback; `AirDodgesLeft` refreshes on land.

### Facing & warp

- **Facing is input-only** — `FacingYaw = Atan2(dirX, dirZ)` set from the input direction in `ProcessNormalMovement`/`ProcessAirMovement`; no snap-to-target, the client's `FacingYaw` field is informational.
- **Warp requires a cone check** — server-side warp (`Simulation.ProcessWarp`) only triggers for targets inside the facing cone (`ServerSimulation.WarpConeHalfAngleRad` = 60° half-cone); out-of-cone targets fall through to a normal attack.
