# SlopArena — Animation & State Machine System

> Architecture of the custom state machine and animation system.

---

## Overview

Two independent layers that communicate via `Travel()`:

```
AnimationTree (flat StateMachine root)    ← visual blends only
    17 states: Idle, Run, air (BlendSpace1D),
    Land, melee, spell_lmb_2, backflip, spells...

Custom FSM (StateMachine.cs)               ← game logic + state transitions
    IdleState → RunState → AirState → LandingState → AttackState
```

**Principle:** the custom FSM decides WHICH state to enter, the AnimationTree handles HOW the animation transitions (blend, xfade).

---

## State lifecycle

```
Enter()
  ↓
OnProcess(dt)        ← _Process, for transition checks (Input)
  ↓
OnPhysicsProcess(dt) ← _PhysicsProcess, for state-specific forces (jump, landing)
  ↓
Exit()
```

### Execution order per frame

```
1. _Process on all nodes
   └─ FSM._Process → CurrentState.OnProcess()
      → check Input (jump press, movement, etc.)
      → TransitionTo() if conditions met

2. _PhysicsProcess on all nodes
   └─ PlayerController._PhysicsProcess
      ├─ BuildInputState()
      └─ MovementComponent.Tick(input)
         → tick-based sim (gravity, friction, dash, knockback)
         → MoveAndSlide()

   └─ FSM._PhysicsProcess → CurrentState.OnPhysicsProcess()
      → state-specific forces (e.g. JumpState sets velocity.Y)
      → Do NOT call MoveAndSlide() — already done by Tick
```

---

## Creating a new state

1. **Create the file** in `Scripts/Animation/States/`

```csharp
using Godot;

public sealed partial class MyState : State
{
    public MyState()
    {
        AnimationName = "Idle"; // must match a state in the AnimationTree
    }

    public override void Enter()
    {
        // reset, setup
        base.Enter(); // calls AnimPlayback.Travel(AnimationName)
    }

    public override void Exit()
    {
        // cleanup
    }

    public override void OnProcess(float delta)
    {
        // check conditions → StateMachine.TransitionTo("otherState");
    }

    public override void OnPhysicsProcess(float delta)
    {
        // state-specific forces (optional)
    }
}
```

2. **Add the node in the character's .tscn** (Godot editor):
   - Child node of `FSM`
   - Script = `Scripts/Animation/States/MyState.cs`
   - Node name = the key used by `TransitionTo()`

3. **Add the state to the AnimationTree** (editor):
   - New state in the root StateMachine
   - Assign the corresponding animation

---

## State conventions

| State | Animation | Transitions to | Triggers |
|-------|-----------|----------------|----------|
| `idle` | `Idle` | run, air | movement input, jump press, off floor |
| `run` | `Run` | idle, air | stop, jump press, off floor |
| `air` | BlendSpace1D (jump↔fall) | landing | on floor |
| `landing` | `Land` | idle, run | timer expired |
| `attack` | `melee` (configurable) | idle, run, air | AnimLockTicks == 0 |

### Rules

|- **`OnProcess()`** : read InputCtrl, trigger transitions. Do not modify velocity.
|- **`OnPhysicsProcess()`** : direct forces on `Player.Velocity`.
|- **Do NOT call `MoveAndSlide()` in states** — already done by `MovementComponent.Tick()`.
|- **Jump force is applied by Simulation, not by states** — the sim handles jump, gravity, friction, dash, knockback.
|- **`InputCtrl` centralizes all input polling** — states never call `Input.Get*()` directly.
|- **`AnimationName`** in constructor : default value, must match an AnimationTree state name.

---

## AnimationTree (manki.tscn)

```
AnimationNodeStateMachine (root)
├── Start ──0.1s──→ Idle ←──0.15s──→ Run
│                     │  0.1s
│                     ├── air_jump (BlendSpace1D)
│                     │              │
│                     │         air_fall ←── [jump↔fall blend via velocity curve]
│                     │              │
│                     │             Land ──at end──→ Idle
│                     │
│                     ├── melee ──0.15s──→ End (LMB stage 1)
│                     │      └── code ChainTo() → spell_lmb_2 → backflip
│                     ├── attack_air_lmb, attack_air_rmb, attack_heavy_charge
│                     └── spell_q, spell_e, spell_r, spell_f
```

Each state is wrapped in `AnimationNodeBlendTree → TimeScale → AnimationNodeAnimation`
for runtime speed control. Transitions use `xfade_time` for blends. Combo chaining
is handled by the AttackState code (ChainTo()), not by AnimationTree auto-transitions.

---

## Adding a new character

1. Create its .tscn (model + AnimationPlayer + AnimationTree as root StateMachine)
2. Add an `FSM` node with `StateMachine.cs` as its script
3. Add child State nodes (idle, run, air, landing, attack) each with their C# script
4. Add corresponding states in the AnimationTree (BlendTree+TimeScale wrappers recommended)
5. Register the character's abilities in `CharacterDefinition.cs` under `BuildRegistry()`
6. Add special effects to `AbilityRegistry.cs` if needed

The StateMachine auto-registers states by their Node name. The AnimationTree state names
must match the `AnimationName` property in each State constructor.
