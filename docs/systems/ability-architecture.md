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
│  ServerAbility (base class)                          │
│  ┌────────────────────────────────────────────────┐ │
│  │ OnStart(ref state, def)  // called once        │ │
│  │ Tick(ref state, input)   // called per tick   │ │
│  │ OnEnd(ref state)         // natural end only  │ │
│  │                                                │ │
│  │ Helpers:                                       │ │
│  │ - SetVelocityInFacing()                        │ │
│  │ - SpawnHitbox()                                │ │
│  │ - GetParam(key, fallback)                      │ │
│  │ - EndAbility()                                 │ │
│  └────────────────────────────────────────────────┘ │
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
    (3, _) => new NewAbility(),  // New slot mapping
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
    (0, true) => new MankiAirPunch(),      // Air LMB
    (1, false) => new MankiAerosolFlame(), // Ground RMB
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

## Lifecycle Details

### OnStart
- Called once when ability activates
- Set initial state (State, AnimLockTicks, AnimIndex)
- Apply initial velocity if needed
- Initialize private fields

### Tick
- Called every sim tick while active
- Check `s.AttackElapsedTicks` for timing
- Spawn hitboxes at specific ticks
- Apply per-tick movement
- Check for combo chains (read `input.ActiveSlot`)
- Call `EndAbility()` when done

### OnEnd
- Called ONLY on natural completion (NOT interruption)
- Override to apply lingering effects
- Cooldown is applied automatically by ServerSimulation

### Interruption
- Hitstun, death, or new ability activation
- OnEnd is NOT called
- Ability dropped from `_activeAbilities`
- Velocity preserved (important for momentum-granting abilities)

## Best Practices

1. **Keep logic in Tick(), data in Params**
2. **Use `_stageTicks++` for tick counters** (not duration -= delta)
3. **Read params in OnStart** for performance
4. **Spawn hitboxes relative to character** (OffX/OffY/OffZ are facing-rotated)
5. **End explicitly** - call EndAbility() when done
6. **Don't use Godot types** in Shared/Abilities/

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

## Related Docs

- `architecture-overview.md` - Codebase structure
- `attack-hitbox-system.md` - Hitbox spawning details
- `netcode-architecture.md` - Server-authoritative model
