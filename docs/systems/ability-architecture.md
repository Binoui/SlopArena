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
│  │ Manki + slot 1 => new MankiAerosolFlame()      │ │
│  └────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────┐
│  │ OnStart(ref state, def)  // called once; set AnimIndex via AnimIndex property   │
│  │ Tick(ref state, input)   // called per tick; set AnimIndex on ability instance  │
│  │ OnEnd(ref state)         // natural end only; no interrupt callback   │
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
- MankiLmbCombo: Lunges forward for first 10 ticks of each stage
- MankiRoundBomb: Spawns projectile at specific tick
- MankiAerosolFlame: Checks ChargeTicks to select variant

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
    (1, false) => new MankiAerosolFlame(),
    (2, _) => new MankiRoundBomb(),
    (3, _) => null,          // E — data-driven ExplosiveMineSpec
    (4, _) => new MankiDiveBomb(),   // R
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

The `airborne` parameter allows different abilities for ground vs air (e.g., Manki LMB combo on ground, air punch when airborne).

**Example:**
```csharp
private static ServerAbility? CreateMankiAbility(byte slot, bool airborne) => (slot, airborne) switch
{
    (0, false) => new MankiLmbCombo(),     // Ground LMB
    (0, true) => null,                      // AirLMB — data-driven fallback
    (1, false) => new MankiAerosolFlame(), // Ground RMB
    (2, _) => new MankiRoundBomb(),        // Q (same ground/air)
    (3, _) => null,                         // E — data-driven ExplosiveMineSpec
    (4, _) => new MankiDiveBomb(),         // R
    (5, _) => new MankiOverclock(),        // F
    _ => null, // Data-driven fallback for slots without ServerAbility
};
```

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


## Best Practices

1. **Keep logic in Tick(), data in Params**
2. **Use `_stageTicks++` for tick counters** (not duration -= delta)
3. **Read params in OnStart** for performance
4. **Spawn hitboxes relative to character** (OffX/OffY/OffZ are facing-rotated)
5. **End explicitly** - call EndAbility() when done
6. **Don't use engine types** in Shared/Abilities/

## Warp Movement

Warp movement (dash, teleport, lunge) is now server-side in `Simulation.ProcessWarp()`:

```csharp
public override void OnStart(ref CharacterState s, CharacterDefinition def)
{
    // Set warp parameters
    s.WarpTargetX = s.PX + (s.FacingX * 5f);
    s.WarpTargetY = s.PY;
    s.WarpTargetZ = s.PZ + (s.FacingZ * 5f);
    s.WarpSpeed = 0.3f;  // 30% per tick
    s.IsWarping = true;
}
```

The sim will interpolate position each tick until `IsWarping` is cleared or warp completes.

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

### Aim Data Caching

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

## Test Coverage

All abilities have matching xUnit tests in `tests/Shared.Tests/`:

| Test file | What it covers |
|---|---|
| `AbilityLifecycleTests.cs` | Activation, AttackSlot wiring, data-driven expiry |
| `PhysicsTests.cs` | State transitions during attacks, hitstun knockback |
| `CombatIntegrationTests.cs` | Two-entity stability during attacks |
| `SpellResolverTests.cs` | Hitbox collision, CanHitOwner, explosions |
| `ServerSimulationTests.cs` | Ability lifetime, self-hit prevention |
| `CombatMathTests.cs` | Knockback formulas, DI, projectile math |

**Run after every ability change:**
```bash
dotnet test tests/Shared.Tests/ --nologo
```
Build + test completes in <3s. See `docs/testing.md` for details.

## Related Docs

- `architecture-overview.md` - Codebase structure
- `attack-hitbox-system.md` - Hitbox spawning details
- `netcode-architecture.md` - Server-authoritative model
