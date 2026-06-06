# SlopArena — Animation & State Machine System

> Architecture of the custom state machine and animation system.

---

## Overview

Two independent layers that communicate via `Travel()`:

```
AnimationTree (StateMachine root)          ← animation transitions
    Idle ↔ Run → Jump → Fall → Land → Idle

Custom FSM (StateMachine.cs)                ← game logic + state transitions
    IdleState → RunState → JumpState → FallState → LandingState
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
| `idle` | `Idle` | run, jump, fall | movement input, jump press, off floor |
| `run` | `Run` | idle, jump, fall | stop, jump press, off floor |
| `jump` | `Jump` | fall, landing | velocity.Y < 0, on floor |
| `fall` | `Fall` | landing | on floor |
| `landing` | `Land` | idle, run | timer expired |
| `attack` | `melee` | idle, run, fall | AnimLockTicks == 0 |

### Rules

- **`OnProcess()`** : read Input, trigger transitions. Do not modify velocity.
- **`OnPhysicsProcess()`** : direct forces on `Player.Velocity`. E.g. jump sets `velocity.Y = JumpForce`.
- **Do NOT call `MoveAndSlide()` in states** — already done by `MovementComponent.Tick()`.
- **Jump force is applied by JumpState, not by the simulation** — the sim handles gravity, friction, dash, knockback only.
- **AnimationName in constructor** : default value, can be overridden in the Inspector.

---

## AnimationTree (manki.tscn)

```
AnimationNodeStateMachine (root)
├── Start ──0.1s──→ Idle ←──0.15s──→ Run
│   │  0.1s            │  0.1s
│   │   └── Jump ──0.2s──→ Fall
│   │                      │  0.15s
│   │                      ↓
│   │                    Land ──at end──→ Idle
│   │
│   └─── LMB (combat) ──0.15s──→ End
```

Each state is an `AnimationNodeAnimation` with the corresponding clip. The `xfade_time` on each transition provides the blend.

---

## Adding a new character

1. Create its .tscn (model + AnimationPlayer + AnimationTree as root StateMachine)
2. Add an `FSM` node with `StateMachine.cs` as its script
3. Add child State nodes (idle, run, jump, fall, landing, etc.)
4. In `PlayerController.cs`, the line `_fsm = _playerModel.GetNodeOrNull<StateMachine>("FSM")` finds it automatically
5. Each State is auto-registered by its node name (lowercased, "state" suffix stripped)
