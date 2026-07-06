# Ability Architecture - ServerAbility System

## Overview

All abilities use the **ServerAbility pattern**: polymorphic C# classes with data-driven parameters.

- **Logic:** `ServerAbility` subclasses (OnStart/Tick/OnEnd lifecycle)
- **Data:** `AbilitySpec.Params` dictionary (tunable without recompiling)
- **Server:** Authoritative execution in `ServerSimulation`
- **Client:** Renders predicted state, no ability logic

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  CharacterDefinition (MankiData.cs)                  │
│  ┌────────────────────────────────────────────────┐ │
│  │ LMB = new AbilitySpec {                        │ │
│  │   Name = "Monkey Combo",                       │ │
│  │   Params = {                                   │ │
│  │     ["lunge_duration"] = 10f,                  │ │
│  │   },                                           │ │
│  │   Stages = [...],  // hitbox timing/damage    │ │
│  │ }                                              │ │
│  └────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────┐
│  AbilityFactory.CreateServer(characterClass, slot)   │
│  ┌────────────────────────────────────────────────┐ │
│  │ Manki + slot 0 => new MankiLmbCombo()          │ │
│  │ Manki + slot 2 => new MankiRoundBomb()         │ │
│  │ Manki + slot 1 => null (data-driven ChargeAtk)  │ │
│  └────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────┐
│  │ OnStart(ref state, def)  // called once; set AnimIndex via AnimIndex property   │
│  │ Tick(ref state, input)   // called per tick; set AnimIndex on ability instance  │
│  │ OnEnd(ref state)         // natural end only; no interrupt callback   │
│  │ OnHitEntity(ref attacker, ref target, attackerDef, ref damage, ref kbForce) // called when hitbox connects   │
└─────────────────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────┐
│  ServerSimulation.TickAbilities()                    │
│  For each active ability:                            │
│    ability.Tick(ref state, ref input, def)          │
│  If ability ended: apply cooldown, deactivate       │
└─────────────────────────────────────────────────────┘
```

## When to Use ServerAbility

**Use ServerAbility when you need:**
- Per-tick movement control (dash, lunge, warp)
- Conditional logic (spawn hitbox only if X)
- Dynamic behavior (tracking projectiles, mines)
- Input-driven state (hold to charge)

**Examples:**
  - **MankiLmbCombo**: Lunges forward for first 10 ticks of each stage
  - **MankiRoundBomb**: Spawns projectile at specific tick, three-phase aim-hold pipeline
  // Manki RMB is now data-driven ChargeAttack (see Hold-to-Charge section)

## Data-Driven Parameters

All tunable values live in `AbilitySpec.Params`:

```csharp
Params = new()
{
    ["lunge_duration"] = 10f,
    ["explosion_damage"] = 25f,
    ["charge_threshold"] = 45f,
}
```

Read in ServerAbility:
```csharp
float duration = GetParam(def, "lunge_duration", 10f);
```

**Benefits:**
- Designers tune without recompiling
- Same ServerAbility class, different params per character
- Easy A/B testing

## Creating a New Ability

1. **Create ServerAbility subclass** in `Shared/Abilities/`:
```csharp
public sealed class NewAbility : ServerAbility
{
    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        s.State = ActionState.Attacking;
        s.AnimLockTicks = (ushort)GetParam(def, "duration", 30f);
    }
    
    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        // Your logic here
        if (s.AttackElapsedTicks >= s.AnimLockTicks)
            EndAbility(ref s);
    }
}
```

2. **Register in AbilityFactory:**
```csharp
// Add to the appropriate character's private method (e.g., CreateMankiAbility)
private static ServerAbility? CreateMankiAbility(byte slot, bool airborne) => (slot, airborne) switch
{
    (0, false) => new MankiLmbCombo(),
    (1, false) => null,              // RMB — data-driven ChargeAttack
    (2, _) => new MankiRoundBomb(),
    (3, _) => new MankiGrapple(),    // E — Grapple Gun
    (4, _) => new MankiBazooka(),   // R — FPS rocket launcher (no rise)
    (5, _) => new MankiOverclock(),  // F
    _ => null,
};
```

### Slot-Based Mapping

Each ability is mapped by (CharacterClass, slot, airborne) tuple:
- Slot 0 = LMB
- Slot 1 = RMB
- Slot 2 = Q
- Slot 3 = E
- Slot 4 = R
- Slot 5 = F

The `airborne` parameter allows different abilities for ground vs air (e.g., Manki LMB combo on ground, air combo via AirLmbCombo when airborne).

**Example:**
private static ServerAbility? CreateBunnyAbility(byte slot, bool airborne) => (slot, airborne) switch
{
    (0, false) => new BunnyLmbCombo(),
    (0, true) => new AirLmbCombo(),   // AirLMB — multi-hit air combo
    (1, _) => null,       // RMB — data-driven
    (2, _) => new BunnyWhirlingCarrot(),   // Q
    (3, _) => new BunnyFlipKick(),          // E
    (4, _) => new BunnyDragonKick(),        // R
    (5, _) => new BunnyJadeHare(),          // F
    _ => null, // Data-driven fallback for slots without ServerAbility
};

3. **Add to CharacterDefinition:**
```csharp
E = new AbilitySpec
{
    Name = "New Ability",
    Params = new() { ["duration"] = 30f },
    // ... rest of data
}
```

### AnimIndex — Set on Ability Instance (Not Struct Field)

`AnimIndex` is a property on `ServerAbility` (the base class). This is the source
of truth. Set it in `OnStart` and `Tick`:
```csharp
AnimIndex = 2;  // sets the property on the ability instance
```

`ActivateAbility` and `TickAbilities` sync this to `CharacterState.AnimIndex` via
`state.AnimIndex = ability.AnimIndex`. Writing `s.AnimIndex` directly would be
overwritten on the next sync.

**Bug history:** Several abilities originally wrote `s.AnimIndex = X` (the struct field),
which was silently overwritten by the sync. Fixed by changing all sites to `AnimIndex = X`.
There is only one write path: the ability instance property.

### OnStart
- Called once when ability activates
- Set initial state (State, AnimLockTicks), and AnimIndex via `AnimIndex = X` (ability property, synced to struct)
- Apply initial velocity if needed
- Initialize private fields
 
### OnEnd
- Called ONLY on natural completion (NOT interruption)
- Override to apply lingering effects
- Cooldown is applied automatically by ServerSimulation

### Interruption
- Hitstun, death, or new ability activation → OnEnd NOT called
- Ability dropped from `_activeAbilities`
- Velocity preserved (important for momentum-granting abilities)
- **Known gap**: No `OnInterrupt` callback. Ability.Tick() runs during hitstun
  until the ability's own EndAbility fires, which overwrites State=Idle.


**Critical implementation detail:** `ActivateAbility` in `ServerSimulation.cs` sets
`state.AttackSlot = (byte)(slot + 1)` after calling `OnStart`. Individual abilities
`should NOT set AttackSlot in OnStart` — rely on ActivateAbility. (Bug: MankiLmbCombo
and MankiAerosolFlame originally didn't set it, causing TickAbilities to immediately
deactivate them via the `AttackSlot == 0` check. Fixed in `ActivateAbility`.)

### OnHitEntity — Hit-Time Effects

New in this branch: `OnHitEntity` is called when an ability's hitbox connects with a target.
Override to apply status effects (Marked), conditional damage, or spawn secondary hitboxes (AoE).

Example — BunnyDragonKick:
```csharp
public override void OnHitEntity(...)
{
    if ((target.StatusFlags & MARK_BIT) != 0)
    {
        target.StatusFlags &= ~MARK_BIT;     // consume mark
        target.StatusRemainingTicks = 0;
        Resolver.Spawn(AoE hitbox);           // spawn secondary explosion
    }
}
```

### StatusFlags — Marked, Slowed, etc.

`CharacterState.StatusFlags` (byte bitfield) and `StatusRemainingTicks` (ushort)
provide a generic status effect system. Currently used for Bunny Q's Marked status (bit 2).
Flag is auto-cleared when `StatusRemainingTicks` reaches 0.

### SimulationStates — Cross-Entity Inspection

`ServerAbility.SimulationStates` is set by `ServerSimulation` before each tick.
Abilities can inspect other entities' state for homing (Bunny R), area pull (Bunny F), etc.
Returns `Dictionary<ulong, CharacterState>` — do NOT mutate other entities' state directly.


## Best Practices

1. **Keep logic in Tick(), data in Params**
2. **Use `_stageTicks++` for tick counters** (not duration -= delta)
3. **Read params in OnStart** for performance
4. **Spawn hitboxes relative to character** (OffX/OffY/OffZ are facing-rotated)
5. **End explicitly** - call EndAbility() when done
6. **Don't use engine types** in Shared/Abilities/

## Warp Movement

Warp movement (auto-dash toward target before attacking) is server-side in `Simulation.ProcessWarp()`:

Warp parameters (`WarpTargetX`, `WarpTargetZ`, `WarpSpeed`) are set by `ProcessTargetLock()` when the target is within `WarpRange` but outside `AttackRange`. The ability `OnStart` does NOT set warp — it is set by the sim before abilities tick.

```csharp
// Not in OnStart — warp is set by ServerSimulation.ProcessTargetLock() each tick
// s.WarpSpeed = 0.3f;   // done in ProcessTargetLock, not OnStart
```

The sim interpolates position each tick using exponential convergence:
- Each tick closes `WarpSpeed` fraction of remaining distance: `V = dx * WarpSpeed / TickDt`
- At `WarpSpeed = 0.3`, warp reaches attack range in ~3 ticks
- Once warp completes (within `WarpAttackRange`), `WarpSpeed` is cleared and the ability's lunge phase auto-kicks in on the next tick

## Hold-to-Aim Ability Pattern (Manki Q)

Manki RoundBomb demonstrates the hold-to-aim pattern for `AimedProjectile` abilities:

### Three-Phase Pipeline

```
spell_q_start (AnimIndex=0) → spell_q_loop (AnimIndex=1) → spell_q_end (AnimIndex=2)
```

1. **OnStart**: Sets `s.State = Attacking`, `s.ComboStage = 0`, `AnimIndex = 0`
2. **Tick (hold phase)**: After 8 ticks, switches to `AnimIndex=1` (loop). Checks `input.IsAiming`:
   - If `true`: stays in loop, accumulates ChargeTicks
   - If `false`: transitions to throw phase
3. **Tick (throw phase)**: At `throw_trigger_tick`, spawns projectile. At `throw_duration`, calls `EndAbility`

### AimMode — Client-Side Camera Routing

Each `AbilitySpec` carries an `AimMode` field (defined in `AbilitySpec.cs`):

```csharp
public enum AimMode : byte
{
    None,            // No aim input; camera free, cursor locked
    GroundCursor,    // Cursor unlocked; raycast ground → AimYaw + AimDistance
    CameraForward3D, // Cursor locked; camera yaw+pitch → AimYaw + AimPitch
}
```

`TrainingMatch.OnMatchFixedUpdate` reads `spec.AimMode` and routes to `CameraMount.SetMode`:
- `GroundCursor` → `CameraMode.FreeCursor` (cursor unlocks, `AimIndicator` drives ground ring)
- `CameraForward3D` → `CameraMode.Frozen` (camera angle held, crosshair drawn, yaw+pitch read from camera)
- `None` → `CameraMode.Normal` (camera free, cursor locked)

**When adding a new aimed ability**, set `AimMode` on its `AbilitySpec` — no changes to `TrainingMatch` needed.

### Aim Data Caching (server side)

Critical detail: `s.AimTargetDistance` and `s.AimYaw` are overwritten every tick by `SimulateTick`.
The projectile spawns 10 ticks after the release transition. Cache both values at transition time:

```csharp
_cachedAimDistance = s.AimTargetDistance;
_cachedAimYaw = s.AimYaw;
// Use _cachedAimDistance, _cachedAimYaw for spawn, not s.AimTargetDistance/s.AimYaw
```

### Cooldown

`CharacterState` is a value type. After `SetCooldown(ref state, slot, ticks)`, persist with:
```csharp
_states[id] = state;
```
Client-side cooldown check in `Simulation.SimulateTick` mirrors the server check in `PreTickAbilities`.

## Hold-to-Charge Ability Pattern (Manki RMB)

Manki RMB is a data-driven `ChargeAttack` behavior. No `ServerAbility` class — the pipeline
handles charging, release detection, and stage selection automatically.

### Two-Phase Lifecycle

```
Phase 1: spell_rmb_charged (ComboStage=0) → charge hold
Phase 2: spell_rmb_attack   (ComboStage=1) → release-to-attack
```

**Phase 1 — Charge hold** (ComboStage=0):
- The data-driven pipeline detects `ChargeAttack` behavior and holds at ComboStage=0.
- `ChargeTicks` accumulates each tick while `input.IsAiming` is true.
- ChargeTicks is capped at `ChargeHoldTicks` (45 ticks = 0.75s).
- The stage used for hitbox/expiry is `Stages[0]` (no `ChargedStages` during charge phase).

**Release conditions** (in `Simulation.SimulateTick`):
- **Manual release** (`!input.IsAiming && AttackElapsedTicks >= 5`): Player released RMB key,
  fires as normal or charged depending on accumulated ChargeTicks.
- **Auto-release** (`AttackElapsedTicks >= 10 || ChargeTicks >= ChargeHoldTicks`): Fallback
  that prevents infinite charge. At ChargeHoldTicks the attack fires automatically.
- On release: `ComboStage = 1`, `AttackElapsedTicks = 0`.

**Phase 2 — Attack** (ComboStage=1):
- Stage selection uses `ResolveStage()`:
  - `ChargeTicks >= ChargeHoldTicks` → `ChargedStages[0]` (charged variant)
  - Otherwise → `Stages[1]` (normal variant)
- Same as standard data-driven attack: spawn hitbox at `triggerTick`, end when
  `AttackElapsedTicks >= stage.DurationTicks`.

### Key Differences from Hold-to-Aim (Manki Q)

| Aspect | Hold-to-Aim (Q) | Hold-to-Charge (RMB) |
|--------|-----------------|----------------------|
| Input signal | `input.IsAiming` = release-to-throw | `input.IsAiming` = hold-to-charge |
| Client indicator | Ground ring + arc via AimIndicator | No indicator (hidden behind `aimingSlot >= 2`) |
| Camera lock | Locked during aiming | Not locked (camera follows player) |
| Animation phases | 3 phases (start/loop/end) | 2 phases (charge/release) |
| Variants | Always same projectile | Normal vs charged (damage, range, radius) |

### Cooldown

Cooldown (30 ticks) starts after the attack ends. Total commitment time =
`hold_duration + attack_duration`, so RMB cannot re-fire during the active ability.

### Client Integration (TrainingMatch)

`TrainingMatch` reads `spec.AimMode` (not `spec.Behavior`) to determine camera/cursor state.
RMB has `AimMode = AimMode.None`, so it never triggers cursor unlock or camera freeze — the
camera follows freely during the charge hold, which is correct.
## Test Coverage

All abilities have matching xUnit tests in `tests/Shared.Tests/`:

| Test file | What it covers |
|---|---|
| `AbilityLifecycleTests.cs` | Activation, data-driven expiry, RMB charge-hold lifecycle (8 tests: normal/charged hitbox params, under-threshold hold, auto-release, cooldown, hold-release) |
| `AttackToIdleTests.cs` | State transitions back to idle after attacks |
| `AnimatorGraphBuilderTests.cs` | Animation graph transition correctness |
| `PhysicsTests.cs` | State transitions during attacks, hitstun knockback |
| `CombatIntegrationTests.cs` | Two-entity stability during attacks |
| `SpellResolverTests.cs` | Hitbox collision, CanHitOwner, explosions |
| `ServerSimulationTests.cs` | Ability lifetime, self-hit prevention |
| `CombatMathTests.cs` | Knockback formulas, DI, projectile math |
| `MankiExplosiveMineTests.cs` | Mine placement, detonation, auto-detonate, Overclock buff bonus |
| `BunnyAbilityTests.cs` | Bunny LMB/Q/E/R/F activation, hitbox, damage, mark, homing, launcher |

**Run after every ability change:**
```bash
dotnet test tests/Shared.Tests/ --nologo
```
Build + test completes in <3s. See `docs/testing.md` for details.

## Related Docs

- `architecture-overview.md` - Codebase structure
- `attack-hitbox-system.md` - Hitbox spawning details
- `netcode-architecture.md` - Server-authoritative model
