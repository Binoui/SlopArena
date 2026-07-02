# Attack & Hitbox System

## 1. Philosophy

Every attack is an **animation** with **hitbox events** triggered at precise ticks. There is no concept of "startup" or "self-lock" — just an animation duration and a list of events.

```
Animation:  ┌────────────────────────────────────────────┐
            │            52 ticks total                   │
            │                                              │
Events:      tick 6: hitbox "punch" (5 ticks active)
             tick 20: hitbox "kick" (3 ticks active)
```

## 2. Data Model

### HitboxEvent

```csharp
public struct HitboxEvent
{
    public ushort TriggerTick;      // Tick from the start of the attack
    public ushort DurationTicks;    // Hitbox lifetime (0 = 1 tick)
    public float Radius;            // Hitbox size
    public float OffsetX, OffsetY, OffsetZ;  // Position relative to attacker
    public float Damage;
    public float BaseKnockback;
    public float KnockbackGrowth;
    public float KnockbackUpward;
    public ushort StunTicks;
    public bool Interruptible;      // Cleared if the player gets hit
}
```

### AttackStage

```csharp
public struct AttackStage
{
    public ushort DurationTicks;           // Total animation lock duration
    public HitboxEvent[] HitboxEvents;     // Events during this stage
    public ushort ChainWindowTicks;        // Legacy — not used for timing
    public float LungeForce;               // Forward burst during attack
    public float AttackRange;
    public float WarpRange;
    public bool UseTargetLock;
    public bool RotateTowardTarget;
    public float TrackingStrength;
}
```

`DurationTicks` is the total animation lock time. Hitboxes spawn at `TriggerTick` within this window.

### Example: Manki LMB (3-hit combo)

```
Ability LMB:
  Stages[0] (jab):
    DurationTicks: 52
    HitboxEvents: [{ TriggerTick: 6, DurationTicks: 3, Radius: 0.5, Damage: 4, ... }]
    LungeForce: 0
    
  Stages[1] (spell_lmb_2):
    DurationTicks: 38
    HitboxEvents: [{ TriggerTick: 8, DurationTicks: 3, Radius: 0.6, Damage: 5, ... }]
    LungeForce: 0
    
  Stages[2] (backflip):
    DurationTicks: 66
    HitboxEvents: [{ TriggerTick: 10, DurationTicks: 4, Radius: 0.7, Damage: 10, ... }]
    LungeForce: 0
```

## 3. ServerAbility Lifecycle

### Activation (ServerSimulation.cs pre-sim phase)

When player presses an ability slot:
1. Check cooldown (GetCooldown)
2. Check AnimLockTicks/HitstunTicks
3. Check if ability already active
4. Instantiate ServerAbility via AbilityFactory
5. Call ability.OnStart(ref state, def)
6. Register in _activeAbilities dictionary

### Tick Loop (ServerSimulation.TickAbilities)

Each active ability's Tick() is called:
```csharp
ability.Tick(ref state, ref input, def)
```

ServerAbility can:
- Set velocity (SetVelocityInFacing, SetVelocity)
- Spawn hitboxes (SpawnHitbox helper)
- Read params (GetParam)
- Advance combo stages (track _stage, _stageTicks)
- End naturally (EndAbility)

### Completion or Interruption

**Natural end (EndAbility called):**
- OnEnd() is called
- Cooldown applied
- State set to Idle

**Interruption (hitstun, death):**
- OnEnd() is NOT called
- Ability dropped from _activeAbilities
- Velocity preserved (momentum-granting abilities work correctly)

### Example: MankiLmbCombo

```csharp
public override void OnStart(ref CharacterState s, CharacterDefinition def)
{
    _stage = 0;
    s.State = ActionState.Attacking;
    s.AnimLockTicks = GetCurrentStage(def).DurationTicks;
    SetVelocityInFacing(ref s, stage.LungeForce);
}

public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
{
    // Apply lunge for first 10 ticks
    if (_stageTicks <= _lungeDuration && stage.LungeForce > 0f)
        SetVelocityInFacing(ref s, stage.LungeForce);
    
    // Spawn hitboxes at trigger tick
    foreach (var evt in stage.HitboxEvents)
        if (evt.TriggerTick == _stageTicks)
            SpawnHitbox(ref s, evt);
    
    // Chain to next stage if input buffered
    if (input.ActiveSlot == (Slot + 1) && _stageTicks >= stage.ChainWindowTicks)
        AdvanceStage();
    
    // End when duration expires
    if (_stageTicks >= stage.DurationTicks)
        EndAbility(ref s);
}
```

## 4. Advantages

- **The sim is truly the authority** — hitboxes spawned by the same sim that calculates positions
- **No desync** — the client only renders
- **Natural cancel** — hitstun → clear queue
- **Flexible** — N hitboxes per attack, variable positions, arbitrary timings
- **Combo** — ServerAbility subclasses can track stages and chain logic
- **Works for all abilities** — projectile, burst, multi-hit, everything

---

## Related Docs

- `ability-architecture.md` — Full ServerAbility pattern guide
- `netcode-architecture.md` — Server-authoritative model
- `combat-systems.md` — Universal combat mechanics
