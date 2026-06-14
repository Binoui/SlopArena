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

## 3. Flow in the Simulation

### Initialization

When the player presses LMB (input.Attack = true):

```
SimulateTick:
  1. InputState.Attack ∧ State == Idle → Stage = Stages[0]
  2. State = Attacking
  3. s.AnimLockTicks = Stage.DurationTicks
### Buffer Input

```csharp
// Buffer section (end of SimulateTick):
if input.ActiveSlot > 0 && AnimLockTicks > 0 && BufferedSlot == 0:
  // Combo chain: same slot → always buffer (after first frame)
  if State == Attacking && ActiveSlot == AttackSlot && AttackElapsedTicks > 0:
    BufferedSlot = ActiveSlot

  // General buffer: within InputBufferWindow (6 frames) of unlock
  else if AnimLockTicks <= InputBufferWindow || HitstunTicks <= InputBufferWindow:
    if cooldown == 0:
      BufferedSlot = ActiveSlot
```

### Tick During Attack

```csharp
ProcessAttack (when AnimLockTicks reaches 0):
  // 1. Buffered chain (click during previous frames)
  if BufferedSlot == AttackSlot && ComboStage < Stages.Length - 1:
    -> Advance to next stage
    AnimLockTicks = nextStage.DurationTicks
  
  // 2. Immediate chain (click on this same frame)
  else if ActiveSlot == AttackSlot && ComboStage < Stages.Length - 1:
    ActiveSlot = 0  // consumed, prevent re-buffer
    -> Advance to next stage
  
  // 3. No combo -> idle
  else:
    State = Idle, clear AttackSlot/ComboStage
```

### Cancel on Hitstun

```csharp
When State == Hitstun:
  Clear BufferedSlot (cancels interruptible hitbox events)
```

### Combo Chaining Mechanism

The combo uses a **buffer-slot** system, not a timing window:

1. During an attack, if the player clicks the **same slot** (`ActiveSlot == AttackSlot`), the sim sets `BufferedSlot = ActiveSlot`
2. The click that **started** the current attack is not buffered (`AttackElapsedTicks > 0` guard)
3. When the current stage ends (`AnimLockTicks == 0`), `ProcessAttack` checks:
   - `BufferedSlot` first (click arrived during earlier frames)
   - `ActiveSlot` second (click arrived on the same frame)
4. If either matches the current `AttackSlot` and a next stage exists, **advance**:
   - `ComboStage++`, set `AnimLockTicks = nextStage.DurationTicks`
5. The client detects the `ComboStage` change and calls `ChainTo(nextAnimName)` to play the next animation without leaving the FSM "attack" state
6. If no buffer and no input, **idle**: `State = Idle`, `AttackSlot = 0`

The general input buffer (`InputBufferWindow = 6`) catches any slot input within the last 6 frames of any lock state (attack, hitstun). This is separate from the combo buffer which catches same-slot inputs during the entire attack.

## 4. Advantages

- **The sim is truly the authority** — hitboxes spawned by the same sim that calculates positions
- **No desync** — the client only renders
- **Natural cancel** — hitstun → clear queue
- **Flexible** — N hitboxes per attack, variable positions, arbitrary timings
- **Combo** — buffer-slot system works for any multi-stage ability (not just LMB)
- **Works for all abilities** — projectile, burst, multi-hit, everything
