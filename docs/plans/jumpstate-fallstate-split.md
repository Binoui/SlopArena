# JumpState / FallState Split — Summary

## Problem
Old `AirState` used a `BlendSpace1D` (-1=jump, +1=fall). `OnProcess` auto-set blend to -1 when `Vy > 0`. After knockback → hitstun → air, the character still had upward knockback velocity → jump animation played without pressing jump.

## Solution
Split the single "air" state into two C# FSM states, both referencing the same `"air"` AnimationTree blend space node (no AnimationTree changes):

### JumpState (`Scripts/Animation/States/JumpState.cs`)
- Handles rising phase after pressing jump
- `AnimationName = "air"`, sets blend to **-1.0** (jump pose)
- Auto-transitions to `"fall"` when `Vy <= 0`
- Double jump: `AnimPlayback?.Travel("air")` to restart the animation
- Registered in `PlayerController.cs` via `_fsm?.AddState(new JumpState())`

### FallState (renamed from `AirState` → `Scripts/Animation/States/FallState.cs`)
- Default airborne state when not pressing jump (hitstun, off edge, attack end, dash end)
- `AnimationName = "air"`, sets blend to **+1.0** (fall pose)
- Double jump detection → `TransitionTo("jump")`
- Ground detection → `TransitionTo("landing"` or `"run")`
- **Crucial:** both JumpState and FallState set `blend_position` **before** `base.Enter()` (which calls `Travel("air")`) so the xfade blends to the correct pose from frame 1, not the old value

## Transition Changes

| From | Old target | New target |
|------|-----------|------------|
| IdleState — jump press | `"air"` | `"jump"` |
| IdleState — off edge | `"air"` | `"fall"` |
| RunState — jump press | `"air"` | `"jump"` |
| RunState — off edge | `"air"` | `"fall"` |
| LandingState — jump press | `"air"` | `"jump"` |
| HitReactionState — airborne | `"air"` | `"fall"` |
| AttackState — airborne | `"air"` | `"fall"` |
| DashState — airborne | `"air"` | `"fall"` |

## Files Modified
- **Created:** `Scripts/Animation/States/JumpState.cs`
- **Renamed:** `Scripts/Animation/States/AirState.cs` → `FallState.cs` (class renamed too)
- **Patched:** `IdleState.cs`, `RunState.cs`, `LandingState.cs`, `HitReactionState.cs`, `AttackState.cs`, `DashState.cs`, `PlayerController.cs`
- **.tscn:** `manki.tscn` and `bunny.tscn` — ext_resource path `AirState.cs` → `FallState.cs`, node script reference updated, old AirState ext_resource removed (Godot may re-add it on scene save — watch for this)

## What NOT to Touch
- AnimationTree (`sm_main`) — unchanged. Both states Travel to the existing `"air"` BlendSpace1D node
- Server-side Simulation.cs — unchanged. Jump force is already applied server-side
- The old `AirState.cs` file was renamed, not deleted — verify it's gone from disk

## Potential Issue
Godot may re-add the `res://Scripts/Animation/States/AirState.cs` ext_resource to .tscn files on scene save. If you see `Cannot open file 'res://Scripts/Animation/States/AirState.cs'` errors at runtime, grep for AirState in both .tscn files and remove the dead ext_resource line.
