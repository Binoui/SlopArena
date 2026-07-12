---
name: movement-platform-fighter-3d
description: >-
  SlopArena — Smash/DKO-style 3D platform fighter movement & combat.
  ACTIVE: Unity 2022.3 C# (main branch). Camera-relative 8-direction movement,
  Smash-style % system, 1s dash with invincibility, double jump.
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
 **Active branch:** `main` — Unity 2022.3 C#
**Design ref:** Super Smash Bros (%, knockback scaling) + Divine KO (void arenas, 3D platform fighter format)

## Core Player Mechanics (June 2026 rewrite)

### Camera-Relative Movement

Movement is RELATIVE to the camera orbit direction, not world-space:

| Input | Movement |
|-------|----------|
| Z | Forward (in camera direction) |
| S | Backward (character turns toward camera) |
| Q | Strafe left |
| D | Strafe right |

- Input snaps to **8 directions** (45° increments) — raw input is rounded to the nearest cardinal or diagonal by computing angle from camera-relative forward/right and rounding to `floor(angle / 45°) * 45°`.
### Ground Arrow Indicator

A semi-transparent white triangle mesh at the character's feet shows the snapped camera-relative input direction. Created via `SurfaceTool` in `PlayerController.CreateGroundArrow()`:

```csharp
var st = new SurfaceTool();
st.Begin(Mesh.PrimitiveType.Triangles);
st.AddVertex(new Vector3(0f, 0f, 0.8f));      // tip
st.AddVertex(new Vector3(-0.4f, 0f, 0f));      // left base
st.AddVertex(new Vector3(0.4f, 0f, 0f));       // right base
st.GenerateNormals();
arrow.Mesh = st.Commit();
arrow.MaterialOverride = new StandardMaterial3D {
    AlbedoColor = new Color(1, 1, 1, 0.6f),
    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    EmissionEnabled = true,
};
```

- Positioned at `GlobalPosition.Y = 0.05f` (just above ground).
- Rotation set to `Atan2(snappedInputDir.X, snappedInputDir.Y)`.
- Visible only when movement input is active; hidden for NPCs.
- Updates every physics frame via `UpdateGroundArrow()`.

- CameraMount is a **world sibling** (instantiated by Main.cs), not a child of PlayerController. Camera yaw is ABSOLUTE — mouse-controlled only, never follows player facing. See `references/camera-relative-movement.md` for full architecture (sibling design, CameraMode system, GetForwardDirection pitfall history).
- Camera is mouse-driven (LMB/RMB are attack buttons, not camera toggles — camera always orbits).

### Double Jump

- `MaxJumps = 2` for all classes (defined in MovementStats).
- Jumps reset on ground contact.
- Jump arc: JumpForce=14-18, Gravity=35-42 per class (creates ~4-unit-high arc in 60x60 arenas).

### Dash (Configurable Duration, Invincibility, Ground + Air)

- **Duration:** Configurable via `Simulation.DashDurationTicks` (initial 60 = 1s, user dialed down to 15 = 0.25s at 60Hz).
- **Cooldown:** per-class via `MovementStats.DashCooldownTicks` (~1s for most classes).
- **Invincibility:** `InvincibilityTicks = DashDurationTicks` (full dash duration). While invincible, hurtbox ignores all incoming hits.
- **Ground or air:** Shift (or any mapped dash key) starts a dash on ground AND in air. Old air-dodge system removed — dash replaces it.
- **Mechanics:** Dash direction is locked on activation (snapped to 8-dir or facing). Dash velocity = `dir * DashSpeed`. VY = max(VY, 0) during dash (stops all vertical momentum in both ground and air).
- **No slide at end:** When dash timer expires, `VX = VZ = 0` is set explicitly. Character stops instantly — no momentum carryover.
- **Hurtbox guard:** The `_hurtbox.OnHit` handler returns early if `_movementComponent.IsInvincible`.

### Smash-Style % System (No HP)

CharacterState was changed to remove HP/MaxHP and use a damage percentage system:

```csharp
public ushort DamagePercent;  // 0-999
public ushort InvincibilityTicks;
```

- **Taking damage** increases `DamagePercent` (capped at 999).
- **No death by HP depletion** — only arena void/kill height kills.
- **Knockback formula** (in `Simulation.ApplyKnockback`):
  ```
  kbScale = 1 + (DamagePercent * 0.01)
  // 0% → 1x, 100% → 2x, 200% → 3x, etc.
  ```
- **Respawn** resets `DamagePercent = 0`.
- **UnitFrames** shows `42%` instead of `HP: 75/100`. Bar fills up as % increases (green→yellow→red).
- **NPCs** still use their own int HP system (separate from the player's %).

### Knockback Scaling Rules

- Base knockback comes from `AttackStage.KnockbackForce`.
- Scaled by target's `DamagePercent` in `Simulation.ApplyKnockback` (before KVX/KVY/KVZ decay).
- Early LMB combo hits have low KB (3, 5) → scales gently. Final hit has high KB (15, 20+) → sends far at high %.
- Move KB can be set to 0 for moves that shouldn't launch.
- Knockback direction remains attacker→target (from SpellResolver, horizontal component).
- Knockback decay: `KV -= KV * 0.1333` per tick (8/s at 60Hz).
- Minimal gravity during knockback (`KnockbackMinGravity = 9.8`).
- Landing naturally clears knockback.

## How to Add Camera-Relative 8-Dir Input (recipe)

In `PlayerController.BuildInputState()`:

```csharp
Vector3 camForward = _camera?.GetForwardDirection() ?? GetPlayerForward();
Vector3 camRight = _camera?.GetRightDirection() ?? Vector3.Right;

Vector3 rawDir = Vector3.Zero;
if (Input.IsActionPressed("move_forward"))  rawDir += camForward;
if (Input.IsActionPressed("move_back"))     rawDir -= camForward;
if (Input.IsActionPressed("move_left"))     rawDir -= camRight;
if (Input.IsActionPressed("move_right"))    rawDir += camRight;

// Snap to 8 directions via camera-relative angle
float rawForward = rawDir.Dot(camForward);
float rawRight = rawDir.Dot(camRight);
float angle = Mathf.Atan2(rawRight, rawForward);
float snappedAngle = MathF.Round(angle / (MathF.PI / 4f)) * (MathF.PI / 4f);
float fwd = MathF.Cos(snappedAngle);
float rgt = MathF.Sin(snappedAngle);

_moveDirection = (camForward * fwd + camRight * rgt).Normalized();
input.MoveX = _moveDirection.X;
input.MoveY = _moveDirection.Z;
```

The `GetForwardDirection()` / `GetRightDirection()` on CameraMount return camera-relative vectors with Y flattened.

## Server-Authoritative Architecture (July 2026)

SlopArena now runs a **pure C# `ServerSimulation`** as the single authority, even in local play. The client (PlayerController) is a stateless renderer that receives authoritative `CharacterState` each tick.

### Flow

```
MatchManager._PhysicsProcess (60Hz fixed)
  ├─ PlayerController._PhysicsProcess
  │     BuildInputState() — reads input only, does NOT simulate
  │     ExecuteSlot() — spawns hitboxes into SpellResolver
  ├─ MatchManager._PhysicsProcess
  │     Collect InputState from all entities
  │     Bridge.Tick(inputs) → ServerSimulation.Tick():
  │         SimulateTick → SpellResolver.Tick → damage → void death
  │     ApplyServerState() — sets GlobalPosition/Velocity from server state
```

Key difference from the old architecture:
- **No double simulation** — `_movementComponent.Tick()` is commented out client-side
- **Server is the only authority** — position, velocity, damage, knockback all from `ServerSimulation`
- **Client only renders** — reads server state and applies to Godot body
- **`ServerSimulation.cs` is pure C#** — compiles on any .NET project, no Godot dependency

See `sloparena-entity-system` skill for the full architecture (ServerSimulation, LocalServerBridge, HurtboxCapsule definitions).

### Platform-Aware Ground Collision (PlatformDef[] + GetGroundSurfaceY)

The server no longer clamps to a single floor height. Each arena defines `PlatformDef[]` — rectangular walkable surfaces at specific Y heights. Ground collision checks ALL platforms and picks the highest surface under the character's feet.

**Data model (Shared/ArenaDefinition.cs):**

```csharp
public struct PlatformDef
{
    public float CenterX, CenterZ;  // center of the rectangular surface
    public float HalfSizeX, HalfSizeZ;  // half-dimensions (CSGBox3D convention)
    public float SurfaceY;  // Y of the walkable top surface
}
```

Each arena has a `FloorHeight` (main fallback surface) AND a `PlatformDef[]` (raised platforms, ramps, overhangs). Platform data is derived directly from CSGBox3D nodes in the `.tscn` scene files:

```
Walkable surface Y = CSGBox.position.Y + (CSGBox.size.Y / 2)
XZ rectangle = [center ± halfSize] from position and size
```

**Simulation ground check (Shared/Simulation.cs):**

```csharp
private static float GetGroundSurfaceY(float px, float pz, float py, float capsuleHalf, ArenaDefinition arena)
{
    float bestSurfaceY = arena.FloorHeight;  // fallback
    if (arena.Platforms == null || arena.Platforms.Length == 0)
        return bestSurfaceY;

    foreach (var plat in arena.Platforms)
    {
        // XZ bounds check
        if (MathF.Abs(px - plat.CenterX) > plat.HalfSizeX) continue;
        if (MathF.Abs(pz - plat.CenterZ) > plat.HalfSizeZ) continue;

        float standCenterY = plat.SurfaceY + capsuleHalf;
        // Within snap window — not way above (prevents upward snap), not way below
        if (py <= standCenterY + PlatformLandTolerance &&
            py >= standCenterY - PlatformSnapTolerance)
        {
            if (plat.SurfaceY > bestSurfaceY)
                bestSurfaceY = plat.SurfaceY;
        }
    }
    return bestSurfaceY;
}
```

Constants: `PlatformSnapTolerance = 0.5f` (prevents snapping up to platforms from below), `PlatformLandTolerance = 0.2f` (generous enough for falling speeds up to ~120u/s at 60Hz).

Used in BOTH `SimulateTick` and `ProcessKnockback`. The old `Simulation.FloorHeight = 0f` constant was removed — all floor data is now per-arena.

**How it works:**
- After gravity + position update, `GetGroundSurfaceY` iterates ALL platform surfaces
- XZ bounds check first (is the character within this platform's rectangle?)
- Then height check: is the character's capsule center within tolerance of the standing Y on that platform?
- Highest matching surface wins (so you stand on the raised platform, not the floor below it)
- If no platform matches, falls back to `FloorHeight` (main floor) — character falls off the edge
- The lower bounds check (`py >= standCenterY - PlatformSnapTolerance`) prevents snapping UP to a platform from below it

**To add a platform to an arena** — add a `PlatformDef` entry to the arena's `Platforms[]` array in `ArenaRegistry.BuildAll()`. Derive values from the CSGBox3D in the `.tscn`:
```
SurfaceY = transform.position.Y + (size.Y / 2)
CenterX = transform.position.X
CenterZ = transform.position.Z
HalfSizeX = size.X / 2
HalfSizeZ = size.Z / 2
```

See `references/arena-platform-definitions.md` for the full per-arena platform data.

### Model Y Offset (CharacterDefinition.ModelYOffset)

The visual model is a child of the CharacterBody3D at `(0, 0, 0)` relative position. But the model's skeleton proportions may not match the `CapsuleShape3D.Height` exactly. Instead of hardcoding a Y offset in `LoadPlayerModel()`, use `CharacterDefinition.ModelYOffset`:

```csharp
// Shared/CharacterDefinition.cs
public float ModelYOffset;  // visual offset from capsule center to model origin

// BuildManki():
ModelYOffset = -0.25f;  // tune empirically — model feet should touch capsule bottom

// PlayerController.LoadPlayerModel():
position = new Vector3(0, _charDef.ModelYOffset, 0);
```

The old hardcoded `-0.6f` in `LoadPlayerModel()` was a workaround for the incorrect server floor clamp (was clamping to `FloorHeight = 0` instead of `FloorHeight + CapsuleHeight/2`). After fixing the server, the offset is much smaller (typically -0.15 to -0.35). Tune by checking if the model's feet visually touch the collision capsule bottom.

## Common Pitfalls & Fixes

### FSM Grounded Detection: Use Movement.IsGrounded, NOT Player.IsOnFloor()

**Problem:** `CharacterBody3D.IsOnFloor()` returns `false` when the character is at the correct floor height with VY=0, because `MoveAndSlide()` only detects floor contact when there's downward velocity. The FSM stays stuck in "air" forever.

**Fix (the REAL fix):** Replace ALL `Player.IsOnFloor()` checks in FSM states with `Movement.IsGrounded` (which reads the server's authoritative `CharacterState.IsGrounded`):

| State | File | Change |
|-------|------|--------|
| FallState | `FallState.cs:58` | `Player.IsOnFloor() && vy <= 0f` → `Movement.IsGrounded && vy <= 0f` |
| IdleState | `IdleState.cs:33` | `!Player.IsOnFloor() ...` → `!Movement.IsGrounded ...` |
| IdleState | `IdleState.cs:41` | `... && Player.IsOnFloor()` → `... && Movement.IsGrounded` |
| RunState | `RunState.cs:32` | `!Player.IsOnFloor() ...` → `!Movement.IsGrounded ...` |
| AttackState | `AttackState.cs:119` | `Player.IsOnFloor()` → `Movement.IsGrounded` |
| DashState | `DashState.cs:47` | `Player.IsOnFloor()` → `Movement.IsGrounded` |
| HitReactionState | `HitReactionState.cs:54` | `Player.IsOnFloor()` → `Movement.IsGrounded` |

Also in `PlayerController`, all `IsOnFloor()` calls used for the `airborne` parameter of `ExecuteSlot()` must use `_movementComponent.IsGrounded`:
- `!IsOnFloor()` → `!_movementComponent.IsGrounded` (lines 493, 528, 570, 584, 1203)

A VY nudge (`Velocity.Y = -0.33`) alone does NOT fix this — the nudge only triggers `IsOnFloor()` in one frame, not reliably.

### Camera Forward Direction: Read from _h, Not CameraMount Root

`CameraMount.GetForwardDirection()` returns `-_h.GlobalTransform.Basis.Z` (NOT `this.GlobalTransform.Basis.Z`). The yaw is stored on the `_h` child node; CameraMount root itself has no rotation. Always read from `_h.GlobalTransform`:

```csharp
// CameraMount.GetForwardDirection()
if (_h == null) return -Vector3.Forward;
Vector3 forward = -_h.GlobalTransform.Basis.Z;  // camera's look direction
```

Rule: `camForward = -Basis.Z` (camera look direction) for Z=forward. `camForward = +Basis.Z` would make Z move away from camera.

### Dash Re-entry Prevention

With the server-authoritative architecture, `ApplyServerState` preserves `DashDurationTicks`. The client's `_PhysicsProcess` (runs AFTER `MatchManager._PhysicsProcess` due to parent-child order) must NOT re-start a dash when the player is already dashing.

**Fix:** Add `DashDurationTicks == 0` to the dash condition in `PlayerController._PhysicsProcess`:

```csharp
// Dash (ground OR air) — only if not already dashing
if (input.Dash && _movementComponent.State.AnimLockTicks <= 0
    && _movementComponent.State.DashDurationTicks == 0)
```

Without this check, every frame during a dash re-calls `_fsm?.TransitionTo("dash")`, which re-enters `DashState.Enter()` → `StartDash()` → resets `DashDurationTicks` to 15+ and the dash never ends.

### 1-Frame Delay First Dash/Attack

Due to the parent-child processing order:
- `MatchManager._PhysicsProcess` runs first: `ApplyServerState` overwrites state
- `Player._PhysicsProcess` runs second: detects input, calls `StartDash()` / `SetPendingResolve()`
- But `ApplyServerState` already wrote Idle with zero timers

Result: the first frame of dash/attack is "lost" — it applies on frame+1. On localhost (16.6ms) this is imperceptible. The fix is to move attack/dash execution into `SimulateTick` (server-side), which removes the need for `ApplyServerState` preservation entirely.

See `sloparena-entity-system` skill → `ApplyServerState` section for the preservation hack details.

### Grounded Detection Bug

**NEVER** use `State.IsGrounded = godotGrounded || State.IsGrounded`. The OR assignment makes grounded irreversible — once grounded, walking off a platform doesn't trigger gravity. Always use `State.IsGrounded = godotGrounded;` (trust Godot's floor detection directly).

### Lunge Direction & Multiplier

- **No `* 3f`** multiplier on LungeForce — made lunges 3x too fast (LungeForce 12 → 36 u/s). Use raw LungeForce directly.
- **Direction:** Use camera-relative input direction (`_moveDirection`) for lunge, not character facing (`-Transform.Basis.Z`). Falls back to facing when no input. Prevents backward lunges during air attacks.
- **Air upboost:** `upBoost = airborne && slotIndex == 1 ? -8f : Velocity.Y + 2f` — RMB in air gives a slight downward boost, otherwise inherits current VY + small upward.

### Attack Movement (Server-Authoritative Lunge + Per-Stage Velocity)

Attack movement is purely server-side — the sim sets velocity, the client renders via `ApplyServerState`. Two systems exist per `AttackStage`:

**LungeForce (initial burst):** Applied once at attack start via `ServerAbility.OnStart`. A forward burst in the character's facing direction. Set per stage (e.g. `LungeForce = 8f` for LMB covers ~4m over a 52-tick attack). Re-applied for the first N ticks (`_lungeDuration`) via `ServerAbility.Tick` to maintain speed.

**MoveX/Y/Z (per-tick velocity):** Applied every tick of the stage in `ServerAbility.Tick`. World-space velocity that persists for the full stage duration. Used for complex trajectories (backflips, jump arcs).

**Velocity persistence:** Normal movement processing (friction + input) does NOT run during the `Attacking` state (`Simulation.cs` section 6 skips `ProcessNormalMovement` for non-Idle states). This lets lunge/velocity carry the character without friction decay during the attack animation.

See `sloparena-combat-engine` skill for full architecture details.

### Float System (Aerial Gravity During Attacks/Dashes)

During attack startup/active frames and dashes in the air, gravity is heavily reduced to let the character "float":

```csharp
if (s.AnimLockTicks > 0 || s.State == ActionState.Dashing || s.State == ActionState.Attacking)
{
    // Gentle drift instead of full gravity
    if (s.VY > -3f)
        s.VY -= 6f * TickDt;  // ~6 m/s² (vs full gravity 35-42)
}
else
{
    s.VY -= stats.Gravity * TickDt;  // full gravity
}
```

After the float ends (attack animation finishes or dash expires), gravity resumes from near-zero VY → character starts falling slowly and accelerates — the "starts slow" feel.

### Jump Must Be Edge-Triggered for Double Jump

`input.Jump` MUST use `Input.IsActionJustPressed("jump")` not `Input.IsActionPressed`. Continuous jump input combined with air jump support would consume all jumps in one physics frame. Edge-triggered ensures exactly one jump per keypress.

Ground jump handled in `Simulation.ProcessGroundMovement()`; air jump (double jump) handled in `Simulation.ProcessAirMovement()`:
```csharp
if (input.Jump && s.JumpsLeft > 0) {
    s.VY = stats.JumpForce;
    s.JumpsLeft--;
}
```

### Air Dash Vertical Momentum

During all dashes (ground + air), set `VY = Math.Max(VY, 0f)` to stop vertical momentum. Old split logic left air dash VY unchanged, causing continued falling during air dodge.

### AirState Shows Jump Animation After Knockback (JumpState/FallState Split)

**Problem:** AirState uses a BlendSpace1D (-1 = jump, +1 = fall) and sets `blend_position = -1f` in `Enter()`. After knockback launches the character upward, the remaining upward KVY velocity triggers the jump animation — even though the player didn't press jump.

**Fix — Split into JumpState + FallState, with centralized jump input:**

| State | Trigger | Animation | Transition |
|-------|---------|-----------|------------|
| **JumpState** | JumpJustPressed | "air" BlendSpace1D at -1 (jump) | `JumpDurationTicks` expiry → FallState |
| **FallState** (ex-AirState) | Airborne without jumping | "air" BlendSpace1D, always at +1 (fall) | Grounded → landing/run; jump input → JumpState |

**Key differences from the old implementation:**
1. **Duration-based, not velocity-based** — JumpState uses `CharacterDefinition.Movement.JumpDurationTicks` (per-character) instead of `Vy <= 0`. Keeps rising animation consistent regardless of gravity changes or knockback interference.
2. **Centralized jump input** — ALL jump transitions are handled in `PlayerController._Process`, not in individual states. States no longer check `JumpJustPressed` themselves.
3. **Double jump is just the same fall→jump transition** — from FallState, pressing jump goes to JumpState. No special double-jump logic inside either state.

**Centralized jump handler (PlayerController._Process):**
```csharp
// Runs after _inputCtrl.Poll(), before FSM._Process (parent runs before children)
string currentState = _fsm?.CurrentStateName ?? "";
if (_inputCtrl.JumpJustPressed && currentState is "idle" or "run" or "landing" or "fall")
{
    _fsm?.TransitionTo("jump");
}
```

**Transition map:**

| Source | Old target | New target |
|--------|-----------|------------|
| IdleState: JumpJustPressed | → "air" | → **centralized in PlayerController** → "jump" |
| RunState: JumpJustPressed | → "air" | → **centralized** → "jump" |
| IdleState/RunState: off edge | → "air" | → "fall" (still in state) |
| LandingState: JumpJustPressed | → "air" | → **centralized** → "jump" |
| **HitReactionState**: lock expired, airborne | → "air" | → **"fall"** ✅ |
| **AttackState**: lock expired, airborne | → "air" | → **"fall"** ✅ |
| **DashState**: dash expired, airborne | → "air" | → **"fall"** ✅ |

**Why centralized instead of per-state checks:**
- Single source of truth for which states allow jump transitions
- No risk of a new state forgetting to handle JumpJustPressed
- Easy to add conditions (e.g., disable jump during certain states) in one place
- Double jump naturally works: FallState→JumpState transition is handled by the same code path as ground→JumpState

**JumpState — duration-based:**
```csharp
public override void Enter()
{
    _ticksRemaining = Player.CharDef.Movement.JumpDurationTicks;
    StateMachine.SetAnimParameter("parameters/air/blend_position", -1f);
    base.Enter();
}

public override void OnPhysicsProcess(float delta)
{
    if (_ticksRemaining > 0)
    {
        _ticksRemaining--;
        if (_ticksRemaining == 0)
            StateMachine.TransitionTo("fall");
    }
}
```

**FallState — no jump input, only ground detection:**
```csharp
public override void OnProcess(float delta)
{
    // No JumpJustPressed check — handled centrally in PlayerController
    float vy = Player.Velocity.Y;
    float t = Mathf.Clamp(Mathf.Abs(vy) / FallBlendSpeed, 0f, 1f);
    StateMachine.SetAnimParameter("parameters/air/blend_position", Mathf.Lerp(-1f, 1f, t * t));
    // Ground detection with threshold...
}
```

**Registration:** Add JumpState programmatically before FSM init:
```csharp
_fsm?.AddState(new JumpState());  // registered as "jump"
```

**CharacterDefinition — add JumpDurationTicks:**
```csharp
public struct MovementStats
{
    // ... existing fields ...
    public ushort JumpDurationTicks;  // rising phase duration in ticks
    // Manki: 24 ticks (~0.4s), FightGuy: 28 ticks (~0.47s)
}
```

## Component Architecture (post-Refactor — Custom FSM)

```
Scripts/
├── Animation/
│   ├── State.cs                    — abstract base: AnimationName, Enter/Exit/OnProcess/OnPhysicsProcess/OnInput
│   ├── StateMachine.cs             — custom FSM: auto-registers State children, finds AnimationTree playback
│   └── States/
│       ├── IdleState.cs            — grounded idle, transitions to run/air
│       ├── RunState.cs             — grounded running, transitions to idle/air
│       ├── JumpState.cs            — rising phase after jump press, plays jump anim, auto-transitions to FallState when Vy <= 0
│       ├── FallState.cs            — airborne falling (air BlendSpace1D at +1), handles double-jump detection, grounded check
│       ├── LandingState.cs         — brief recovery after landing, transitions to idle/run
│       └── AttackState.cs          — generic for all 8 ability slots, combo chaining via ChainTo()
├── Entities/
│   ├── PlayerController.cs         — orchestrator: creates FSM, handles input, delegates to MovementComponent
│   └── DummyManager.cs
├── Combat/
│   ├── MovementComponent.cs        — bridges Simulation → Godot body, calls MoveAndSlide
│   ├── CombatComponent.cs
│   └── LocalSimulation.cs
├── InputController.cs              — centralized input polling (Jump, Dash)
├── Characters/
│   ├── AbilityRegistry.cs
│   └── Manki/MankiAbilities.cs
├── Camera/
│   └── CameraMount.cs            — sibling in world tree, absolute yaw mouse-only, CameraMode system
├── Debug/
│   └── DebugHitboxDraw.cs
├── World/
│   ├── Main.cs
│   └── ArenaManager.cs
├── UI/
│   ├── UnitFrames.cs
│   ├── ActionBarHUD.cs
│   └── SettingsUI.cs
└── Shared/ (pure C#, no Godot)
    ├── Simulation.cs
    ├── CharacterState.cs
    └── CharacterDefinition.cs
```

### Custom FSM (StateMachine.cs + State.cs)

**Two-layer architecture:**

```
AnimationTree (root StateMachine, FLAT — 17 states)
  Idle, Run, air (BlendSpace1D), Land, melee, leg_sweep, ...
  → handles animation transitions (xfade, blend)

Custom C# FSM (StateMachine.cs + State child nodes in the character .tscn)
  idle, run, jump, fall, landing, attack
  → handles game logic (input detection, state transitions, state-specific forces)
```

**Lifecycle of a State:**
```
Enter() ──→ OnProcess(dt) ──→ OnPhysicsProcess(dt) ──→ Exit()
```

- **OnProcess()**: called from _Process. Read Input, trigger TransitionTo(). Do NOT modify velocity here.
- **OnPhysicsProcess()**: called from _PhysicsProcess AFTER MovementComponent.Tick(). Apply state-specific velocity overrides (e.g. jump force). Do NOT call MoveAndSlide().
- **Enter()/Exit()**: base.Enter() calls AnimPlayback.Travel(AnimationName).

**Key rules:**
- States do NOT call MoveAndSlide() — MovementComponent.Tick() handles all physics
- OnProcess() for transition checks, OnPhysicsProcess() for velocity overrides
- Jump force is applied by the sim (Simulation.cs) — FSM (JumpState/FallState) only handles the animation
- StateMachine auto-discovers State children by node name (lowercased, "state" suffix stripped)

### Flat AnimationTree (no nested StateMachines)

Godot 4 DOES NOT support `Travel("SubMachine/State")` across nested AnimationNodeStateMachines (`_travel_children()` C++ error in `animation_node_state_machine.cpp:442`). Use a flat root StateMachine instead:

```
sm_main (root StateMachine, ~18 states)
├── Idle ←0.15s→ Run
├── Idle/Run → air (BlendSpace1D: jump -1 ↔ fall +1; JumpState sets -1, FallState sets +1) → Land → Idle
├── Idle/Run/air → melee → End  (xfade 0.15s) — chain to leg_sweep/backflip via code ChainTo()
├── Idle/Run/air → leg_sweep → End
├── Idle/Run/air → backflip → End
├── Idle/Run/air → attack_air_lmb / attack_heavy_charge → End
├── Idle/Run/air → attack_air_rmb → End
├── Idle/Run/air → spell_q/e/r/f → End
└── Start → Idle
```

All Travel() targets are flat names: `Travel("Idle")`, `Travel("air")`, `Travel("melee")`.
BlendSpace parameters accessed directly: `parameters/air/blend_position`.

### PlayerController

- No longer uses AnimationController for game state — animation driven entirely by custom FSM
- Still uses AnimationController for utility: FindSkeleton(), FindAnimationPlayer(), FixAnimationTracks()
- ExecuteSlot() routes through AttackState: sets NextAnimName, calls _fsm.TransitionTo("attack")
- LMB combo chaining: queue-based input buffer (BufferedChain byte, max 2, souls-like style):
  - `ExecuteSlot` pushes to `BufferedChain` during AnimLock (instead of discarding input)
  - `PlayerController._Process` consumes from queue when lock expires → calls ChainTo(nextAnim)
  - Natural double-increment protection: after ChainTo resets AnimLockTicks, subsequent inputs
    push to queue again (max 2) — lock reset automatically spaces out consumption
- Non-LMB slots blocked during attack: `if (slotIndex != 0 && _fsm.IsInState("attack")) return;`
- SelfLockTicks from AbilityData controls attack duration (no timer in AttackState — waits for AnimLockTicks to reach 0)

### Hitbox/Hurtbox System (June 2026 — Capsules)

**Pure C# collision (sphere + capsule).** `SpellResolver` supports `HitboxShape.Sphere` and `HitboxShape.Capsule`. Capsule = segment (start→end) + radius. Collision via `ClosestPointsSegmentSegment()` — handles capsule-capsule, capsule-sphere, sphere-sphere. No Godot physics nodes.

**Hurtboxes: 6 bone-attached capsules** on the character skeleton (Smash-standard):
- Head: degenerate capsule (sphere), Torso: Spine2→Hips, Arms ×2: Arm→ForeArm, Legs ×2: UpLeg→Leg
- `BoneHurtboxSetup` reads `Skeleton3D.GetBoneGlobalPose()` directly each frame — no BoneAttachment3D
- Positions flow: `BoneHurtboxSetup` → `LocalSimulation.Entities` → `SpellResolver.Tick()`

**Hitboxes**: Sphere or Capsule. Set `Shape` + `EndX/EndY/EndZ` at spawn. Default is Sphere (value 0).

```csharp
// Shared/Hitbox.cs
SlopArena.Shared.Hitbox  // X/Y/Z, VX/VY/VZ, Radius, DurationTicks, Shape, EndX/Y/Z
                        // Damage, KnockbackForce, KnockbackUpward, StunTicks, OwnerId

// Shared/SpellResolver.cs
SpellResolver.Spawn(hb);
var hits = SpellResolver.Tick(ents);  // dispatches sphere or capsule path
```

**Key rules:**
- One-hit per hitbox, one-hit per entity per tick (no double-dip)
- **Hitbox direction: PlayerForward, not CameraForward** — camera orbits behind looking AT player; its forward points toward character's back. `GetAttackDirection()` fallback uses `GetPlayerForward()`.
- **Warp initiation (server-authoritative):** Warp is a velocity override (`WarpSpeed > 0`), not a state (`ActionState.Warping` removed). Initiated by setting `CharacterState.WarpTargetX/Z`, `WarpSpeed`, and `WarpAttackRange` on the client (in `PlayerController`) — the server (or sim) runs `Simulation.ProcessWarp()` which sets velocity toward the target each tick until within `WarpAttackRange`. At arrival, `WarpSpeed = 0` and velocity is cleared. See `docs/systems/ability-architecture.md` for full integration with `ServerAbility.OnStart`.
- **WarpSpeed from SprintSpeed:** `WarpSpeed` is set from `CharacterDefinition.Movement.SprintSpeed` (not an `AttackStage` field). The warp stops when distance to target ≤ `WarpAttackRange`. Client also transitions to the `warp` FSM state (`WarpState`) for animation (running towards target with blue emission).
- **Hit reactions**: `HitReactionState` (FSM) + 3 AnimationTree states. Triggered by knockback magnitude.
- **State colors**: Per-state emission in `Enter()/Exit()`. **EMISSION, not AlbedoColor** — additive glow visible on textured models. `MaterialOverride` created lazily for special states, cleared to `null` for normal. Target `_playerModel` subtree only.
- **Damage pipeline**: `LocalSimulation.RouteHit()` → `CombatComponent.TakeDamage()` → `OnTakeDamage`. Main.cs is UI-only.
- **Debug (F3)**: `DebugHitboxDraw` shows wireframe spheres (red=hitboxes) and capsules (blue=hurtboxes) via `DrawWireCapsule()`.

See `godot-csharp-networked-game` references: `capsule-collision.md`, `hurtbox-bone-system.md`, `damage-pipeline.md`, `state-debug-colors.md`.

See `references/capsule-collision.md` for the full capsule collision math (SpellResolver, ClosestPointsSegmentSegment), EntityData extension, and debug visualization flow.

**Manki skeleton naming:** Uses Mixamo convention with UNDERSCORES (not colons). Key bones: `mixamorig_Head`, `mixamorig_Spine2`, `mixamorig_Hips`, `mixamorig_LeftArm`/`RightArm`, `mixamorig_LeftForeArm`/`RightForeArm`, `mixamorig_LeftUpLeg`/`RightUpLeg`, `mixamorig_LeftLeg`/`RightLeg`, `mixamorig_LeftFoot`/`RightFoot`, `mixamorig_LeftHand`/`RightHand`, `mixamorig_LeftShoulder`/`RightShoulder`. 25 bones total.

### Debug Visualization

`Scripts/Debug/DebugHitboxDraw.cs` — `MeshInstance3D` with `ImmediateMesh` wireframe:
- **Red** = active hitboxes (from `SpellResolver.GetActiveHitboxes()`). Spheres or capsules.
- **Blue** = entity hurtboxes (from simulation entity positions). Capsules shown as wireframe tubes (end rings + connecting lines).
- Attached globally in Main.cs (not per-player) so positions are stable when camera moves
- Updates every `_Process` tick via `Main.cs`
- **Material:** Requires `VertexColorUseAsAlbedo = true`, `ShadingMode = Unshaded`, `Transparency = Alpha` on a `StandardMaterial3D`.
- `DrawWireCapsule()` renders capsule wireframe: 2 end rings + 4 connecting lines. `DrawWireSphere()` unchanged (3 orthogonal rings).

### MovementComponent (242 lines)

Thin bridge — delegates to `Simulation.SimulateTick()`:
- Syncs Godot position into CharacterState at start of tick
- Calls `Simulation.SimulateTick(ref State, _charDef, input, _arenaDef)`
- Applies result velocity to Godot body, calls `MoveAndSlide()`
- Syncs grounded state back from Godot
- Public API: Tick(), StartDash(), ApplyJump(), ApplyKnockback(), ApplyDamage(), DoTechRoll(), Respawn()

### Simulation.cs (pure C#)

Contains ALL movement physics logic for one tick:
- `SimulateTick()` — main entry point: timers → knockback → state machine → gravity → position → collision → void
- `ProcessKnockback()` — KV decay, minimal gravity, landing clear
- `ProcessDash()` — maintain velocity, jump cancel on ground
- `ProcessNormalMovement()` → `ProcessGroundMovement()` / `ProcessAirMovement()` — friction, acceleration, sprint, jump, air drift
- `StartDash()` — 1s dash, full invincibility, ground or air
- `ApplyKnockback()` — scales base KB by `1 + DamagePercent * 0.01`
- `ApplyDamage()` — increases DamagePercent

## Data-Driven Ability System

6 identical slots: 0=LMB, 1=RMB, 2=Q, 3=E, 4=R, 5=F.
- Slots 0-5: hitboxes spawned by ability code via `SpellResolver.Spawn()` in `ResolveAbilityStages()` or class-specific `SpecialEffectKeys`
- Slots 2-5: delegate to AbilityRegistry key → method in class-specific files
- LMB combo: stage tracking via `CharacterState.ComboStage`, resets after chain window or landing
- Multi-hit combos should have **increasing knockback**: early stages `KnockbackForce=3..5` (low KB to keep opponent in range), final stage `KnockbackForce=15+` (launches). The % scaling amplifies all stages proportionally.

## Shared/ State Machine (CharacterState)

```
ActionState: Idle, Dashing, AirDodging, Attacking
```

- Transitions: Idle → Dashing (on dash input), Dashing → Idle (timer expires), Idle → Attacking (on attack), any → Idle (from knockback)
- `StateTicks`: countdown for state duration
- `AnimLockTicks`: locks ability execution during attack startup (counts down each tick)
- `BufferedChain`: queue for LMB chain inputs during lock (max 2, consumed when lock expires)
- `ComboStage`/`ComboTimerTicks`: LMB combo chain tracking (resets to 0 when timer expires)

## Arena System

Multi-arena with void death. Defined in `Shared/ArenaDefinition.cs`.

| Arena | Size | Features |
|-------|------|----------|
| pit | 80x80 | Simple flat platform for testing knockback |
| cross | 60x60 | Cross-shaped with gaps between arms |
| split | 60x60 | Two-level: lower ring + raised center with ramps |
| sanctum | 200x200 | Large multi-level: floor + central platform + galleries + stairs |

**Surface data:** Each arena has a `FloorHeight` (main fallback Y) and `PlatformDef[]` (rectangular walkable surfaces). The server's `GetGroundSurfaceY()` checks all platforms and picks the highest surface under the character's feet. See `references/arena-platform-definitions.md` for the full per-arena breakdown.

**Adding a new platform:** Add a `PlatformDef` entry to the arena's `Platforms[]` array in `ArenaRegistry.BuildAll()`. Derive from CSGBox3D in `.tscn`: `SurfaceY = pos.Y + size.Y/2`, center/half from position/size.

**Void death:** Y < `KillHeight` → respawn with 0%.

## Controls

| Input | Action |
|-------|--------|
| Mouse move | Camera orbit (decoupled, always captured) |
| Z | Forward (camera direction) |
| S | Backward (toward camera) |
| Q | Strafe left |
| D | Strafe right |
| Space | Jump / double jump |
| Shift | Dash (ground or air, 1s, invincible) |
| LMB | Slot 0 attack (3-hit combo on ground, single in air) |
| RMB press | Slot 1 attack (ground/air) |
| RMB hold 0.3s | Slot 1 charged attack |
| Q (physical) | Class ability slot 2 |
| E (physical) | Class ability slot 3 |
| R (physical) | Class ability slot 4 |
| F (physical) | Class ability slot 5 |
| Escape | Release mouse cursor |

> **AZERTY note:** All key bindings use `physical_keycode` in `project.godot`, not `keycode`. This matches physical key positions regardless of layout. On AZERTY: Q physical = label A, E = E, R = R, F = F. Movement WASD may need rebinding in Project Settings → Input Map.

## Art Direction (June 2026, revised)

The game uses the **"Megabonk / Pixel8r2"** direction: pixel art 3D aesthetic. 3D models rendered with limited palettes, 3-tone cell shading (no gradients), 1px outlines. Think retro fighter in 3D space — flat colours, no textures, no PBR.

See `references/art-direction.md` for full palettes, design rules, technical specs (GLB + Mixamo rig + ~4000 tris), and 3daistudio prompt guidelines.

**Roster status (June 2026):** 1 playable character. Wraith/Vanguard/Channeler/Knight removed — old gameplay tests only, no design carried forward.

| # | Character | Role | Status |
|---|-----------|------|--------|
| 1 | **Manki** — Mad Bomber Monkey, mischievous macaque with explosives, aerosol flamethrower, dynamite jump, dive bomb | Agile rushdown / aerial bombardier | In-game (prototype — placeholder animations) |
| 3 | **TBD** — proposed Assassin | Speedster | Open |
| 4 | **TBD** — proposed Ranger | Hybrid mid-range | Open |

## Character Kit Design Principles

A design guide for building character kits (attack archetypes, CD patterns, class templates) lives at:
The game design guide for building character kits lives at:
`docs/character-kit-design-principles.md`.

## Attack Timing Model

**SelfLockTicks = full attack duration, not animation lock.** The combat system is decoupled from animation length. See `references/attack-timing-and-hitbox-redesign.md` for:
- SelfLockTicks / ChainWindowTicks timing diagram
- IsAttacking() guard that prevents ability canceling
- Spam-click protection
- Proposed HitboxFrame system (active frames per tick)

See `references/frame-data-balancing.md` for how to derive SelfLockTicks values from DKO-style frame data (startup + active + endlag) with a quick-reference table of values by category (LMB/RMB/abilities/ultimate).

See `references/combo-queue-system.md` for the queue-based input buffer design, AnimationTree transition rules, timing values, and souls-like comparison.

See `references/aimed-charge-state.md` for the modular aimed charge state — a C# FSM state pattern for charge-loop + cone indicator + attack release. Implements Manki's RMB (Aerosol + Lighter) with `rmb_loop`/`rmb_attack` animations, movement lock, procedural cone mesh, and hitbox resolution.

See `references/startup-ticks-system.md` for the frame-based attack startup delay system — each AttackStage has a StartupTicks value that defers hitbox resolution. Covers architecture (SetPendingResolve/TickStartup), reference values per ability, design rules (chain hits bypass startup), and ordering constraints.

See `references/dko-patch-notes-research.md` for SteamDB patch note scraping methodology, what data is available (damage/cooldown/prefire) vs what's not (active frames/endlag/frame-by-frame), and the URL pattern for automated collection.

## To Add a New Character

See `docs/character-import-checklist.md` for the full step-by-step checklist (concept → 3D model → animations → code → test).

Quick reference:
1. Define movement stats + ability data in a `BuildXxx()` method in `CharacterRegistry` (Shared/)
2. Add ability special effects in the class's ability file (Scripts/Characters/)
3. Register effects in `AbilityRegistry.cs`
4. Add class button + description in `ClassSelectUI.cs`
5. Add model path, scale, position in `PlayerController.LoadPlayerModel()` switch
6. Create `docs/characters/<name>.md` with kit design + palette + silhouette notes
7. Generate 3D model via 3daistudio (see `references/art-direction.md` for prompt rules)
8. Rig, animate, and compose master .glb (Blender + Cascadeur or Mixamo)
9. Import master .glb in Godot — animations come embedded in the AnimationPlayer

## To Replace a Character (remove old + add new)

See `references/character-replacement-checklist.md` for the full file-by-file checklist.
The June 2026 Channeler→Narodin replacement touched all these files in order.

## Known Build Pitfalls

- **Godot 4 nested AnimationNodeStateMachines:** `Travel("SubMachine/State")` is NOT supported. The C++ function `_travel_children()` at `animation_node_state_machine.cpp:442` refuses paths from a parent SM into a nested sub-SM. Always use a flat root StateMachine and call `Travel("StateName")` with flat names.
- **AnimationTree BlendSpace1D in a flat StateMachine:** Works fine. Add the BlendSpace1D node directly in the root StateMachine, set its parameter name, and set the parameter via `_animTree.Set("parameters/node_name/blend_position", value)` from C#.

- **SelfLockTicks must match animation duration:** SelfLockTicks = 8 ticks (128ms) is too short — the animation is ~300ms, so the lock expires mid-animation and the next LMB press cuts it. Set SelfLockTicks to 18-40 ticks (288-640ms) to match typical attack animations. Each tick = ~16ms.
- **Jump force is applied by the sim (Simulation.cs), not by the FSM:** `Simulation.ProcessGroundMovement` and `ProcessAirMovement` apply `s.VY = stats.JumpForce` when `input.Jump && s.JumpsLeft > 0`. The FSM (JumpState/FallState) only handles animation — it reacts to velocity but does not apply the jump force. This keeps the sim as the single authority on physics.
- **Attack state waits for AnimLockTicks:** AttackState.OnProcess returns early if AnimLockTicks > 0. Only returns to movement when lock == 0 and combo window closed. This prevents attack animation clipping from spammed input.
- **Attack timing vs animation timing:** `SelfLockTicks` IS the attack duration. Do NOT use short lock values (8-12 ticks) — the animation is much longer. Set SelfLockTicks to the full intended attack duration (60-90 ticks for 1-1.5s attacks). The chain window (`ChainWindowTicks`) must be LONGER than SelfLockTicks for combo chaining to work.
- **Sword scale:** Model scale (2.0) applies to children. Sword child at scale 2.0 = 4x effective. Always use `sword.Scale = Vector3.One`.
- **IsAttacking() blocks everything:** Once an attack fires, `IsAttacking()` returns true for the full SelfLockTicks duration. Other abilities are blocked. For explicit cancel mechanics, reduce SelfLockTicks or add special handling in `ExecuteSlot`.

- **Hitbox ambiguity:** There's both `SlopArena.Shared.Hitbox` (data struct, pure C#) and `Scripts/Combat/Hitbox` (Godot Area3D node, partial class). Always qualify as `SlopArena.Shared.Hitbox` in Godot scripts -- using `Hitbox` alone resolves to the wrong type.
- **Debug visualization:** `DebugHitboxDraw` uses `ImmediateMesh` which is fast but has no depth testing by default. Call `UpdateHitboxes()` each physics frame with hitbox + hurtbox lists.
- **ChainWindowTicks vs SelfLockTicks timing:** Both decrement each tick from their set values. The post-lock chain window = ChainWindowTicks - SelfLockTicks ticks (the remaining ComboTimerTicks after AnimLockTicks hits 0). With the queue system (BufferedChain byte, max 2), inputs during lock are also accepted and buffered — so the effective chain window starts at the buffer point, not just after lock expires. Design values so post-lock window is 15-40 ticks (0.25-0.67s) for comfortable chaining.  
- **SelfLockTicks must match animation duration:** SelfLockTicks = 8 ticks (128ms) is too short — the animation is ~300ms, so the lock expires mid-animation and the next LMB press cuts it.

- **Patch tool + C# tabs:** The patch tool may insert extra tab indentation when patching C# code inside deeply-nested blocks. After patching, verify indentation of the changed section matches the surrounding code (tabs, not spaces).
- **Orphaned AnimationTree states after restructuring:** When converting from sub-AnimationTree states to flat states, old `states/X/node`, `states/X/position` lines, their transition entries in the `transitions` array, and the referenced `ext_resource` all remain in the .tscn as dead code. Search for the old state name after editing — delete the state definitions (2 lines), prune all transitions mentioning it from the array (each is `from","to","xfade`), and remove the ext_resource line if the state was the only consumer. Godot Editor doesn't error on orphaned states but they clutter the tree and may cause double-transition confusion.

- **Patch tool + escaped quotes:** Use `execute_code` + Python `write_file` for files with complex escape sequences.
- **AZERTY keyboards:** All Input Map bindings in `project.godot` must use `\"physical_keycode\"` (not `\"keycode\"`) so they match physical key positions regardless of layout. The format:
  ```
  \"physical_keycode\": \"Key.Q\",
  \"keycode\": 0,\"
- **Godot temp cache:** Always wipe `.godot/mono/temp` before `dotnet build` to avoid stale-assembly errors.
- **HP→% migration gotchas:** All references to `State.HP`, `State.MaxHP`, `GetHP()`, `GetMaxHP()` must be migrated to `State.DamagePercent` and `GetDamagePercent()`. Old hurtbox handlers that checked `HP <= 0` for death must be removed — only arena void kills now.
- **Camera-relative dependency:** `BuildInputState()` reads from `_camera?.GetForwardDirection()` (nullable — null-safe for NPCs). NPCs and remotes use `GetPlayerForward()` as fallback. The ground arrow is hidden for NPCs.
- **SetClass before AddChild:** `PlayerController.SetClass()` MUST be called BEFORE `AddChild()`, not after. `_Ready()` runs during `AddChild` and `LoadPlayerModel()` reads `_playerClass` inside `_Ready()`. If `SetClass` is after `AddChild`, the default class (Vanguard) is used and the wrong model loads. Fix: always `SetClass()` → `AddChild()`.

## Character Import

### KayKit (Adventurers 2.0) — GLB/.res pipeline

KayKit characters use GLB models + extracted `.res` animation files. See `references/kaykit-character-pipeline.md` for the full setup: directory structure, extracting `.res` from animation_source GLBs, loading flow, animation name mapping, weapon attachment via BoneAttachment3D.
