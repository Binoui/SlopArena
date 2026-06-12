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
    public float KnockbackForce;
    public float KnockbackUpward;
    public ushort StunTicks;
    public bool Interruptible;      // Cleared if the player gets hit
}
```

### AttackStage (simplified)

```csharp
public struct AttackStage
{
    public ushort DurationTicks;           // Total animation lock duration
    public HitboxEvent[] HitboxEvents;     // Events during this stage
    public ushort ChainWindowTicks;        // Window to advance to the next stage
                                          // (0 = no combo)
    // Knockback & damage are in HitboxEvents now
}
```

`DurationTicks` is the total animation lock time. Hitboxes spawn at `TriggerTick` within this window.

### Example: Manki LMB (3-hit combo)

```
Ability LMB:
  Stages[0] (jab):
    DurationTicks: 12
    HitboxEvents: [{ TriggerTick: 6, DurationTicks: 2, Radius: 0.5, Damage: 4, ... }]
    ChainWindowTicks: 8    ← 8 ticks to buffer the next input
    
  Stages[1] (melee):
    DurationTicks: 14
    HitboxEvents: [{ TriggerTick: 8, DurationTicks: 2, Radius: 0.7, Damage: 6, ... }]
    ChainWindowTicks: 6
    
  Stages[2] (flying kick):
    DurationTicks: 20
    HitboxEvents: [{ TriggerTick: 8, DurationTicks: 3, Radius: 0.8, Damage: 12, ... }]
    ChainWindowTicks: 0    ← last stage, no chain
```

## 3. Flow in the Simulation

### Initialization

When the player presses LMB (input.Attack = true):

```
SimulateTick:
  1. InputState.Attack ∧ State == Idle → Stage = Stages[0]
  2. State = Attacking
  3. s.AnimLockTicks = Stage.DurationTicks
  4. Queue all Stage.HitboxEvents into s.ActiveHitboxEvents[]
```

### Tick During Attack

```
SimulateTick (State == Attacking):
  For each HitboxEvent in s.ActiveHitboxEvents:
    event.ElapsedTicks++
    if ElapsedTicks == TriggerTick:
      SpawnHitbox(event)  → added to SpellResolver for this tick
    if ElapsedTicks >= TriggerTick + DurationTicks:
      Remove event (expired)
  
  if s.AnimLockTicks == 0:
    // Animation finished
    if Stage.ChainWindowTicks > 0 ∧ input.Attack buffered:
      → Next Stage (combo)
    else:
      → Idle
```

### Cancel on Hitstun

```
SimulateTick (State == Hitstun):
  Clear s.ActiveHitboxEvents (all interruptible events)
  Clear s.ComboBuffer
```

### Combo Chaining

```
When AnimLockTicks == 0:
  If Stage.ChainWindowTicks > 0:
    Opens a chain window of ChainWindowTicks
    If input.Attack during this window:
      → Next Stage
      Reset AnimLockTicks = Stage.DurationTicks
      Queue the hitbox events of the new stage
```

## 4. Advantages

- **The sim is truly the authority** — hitboxes spawned by the same sim that calculates positions
- **No desync** — the client only renders
- **Natural cancel** — hitstun → clear queue
- **Flexible** — N hitboxes per attack, variable positions, arbitrary timings
- **Simple combo** — chain window ticks + input buffer on the sim side
- **Works for all abilities** — projectile, burst, multi-hit, everything
