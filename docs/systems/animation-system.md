# Unity Animation System

> Animancer-driven clip playback. Server timing dictates when clips start and end.
> No AnimatorController, no trigger parameters, no transition tables.

## Architecture

```
CharacterState (server simulation, 60Hz)
  → PlayerRenderer.ApplyServerState()
    → UpdateAnimationState()
      → _charConfig.GetClipByName(name)     → AnimationClip
      → _animancer.Play(clip, fadeDuration)  → drives the Animator directly
```

## State transitions

PlayerRenderer tracks `_lastAnimState` / `_lastAttackSlot` / `_lastComboStage` and
detects state changes. On change, plays the appropriate clip with a 0.05-0.1s
crossfade. The state machine is pure C# in `UpdateAnimationState()`:

- **Non-combat:** idle ↔ run (crossfade by Speed threshold), jump, fall
- **Double jump:** override fall with jump when upward impulse detected
- **Attacking:** lookup by `(AttackSlot, ComboStage)` → `AnimationNames[]` → clip
  → apply speed modulation = `frameCount / DurationTicks`
- **Dashing:** play dash clip (0s crossfade)
- **Hitstun:** play `hit_small`/`hit_medium`/`hit_hard` by HitstunLevel

No blend tree, no Animator parameters, no AnyState transitions.

## Clip lookup

`CharacterAnimationConfig` ScriptableObject (loaded from `Resources/AnimationConfigs/`):
  - **Standard clips:** idle, run, jump, fall, dash, hit_small, hit_medium, hit_hard, death
  - **Ability clips:** Name→Clip dictionary, one entry per `AnimationNames` string
  - **Auto-loaded** in `PlayerRenderer.LoadModel()` by `CharacterDefinition.Class`

## Per-clip overrides

`AnimationClipConfig` on `CharacterDefinition`:
  - **Extrapolation:** `None` | `Hold` | `Continuous` (how clip behaves past its length)
  - **FrameRateOverride:** override baked framerate (0 = use clip's native framerate)

See also: `AnimationClipConfig.cs` in Shared.

## Speed modulation

For data-driven abilities with baked skeleton data:

```
animSpeed = frameCount / DurationTicks
```

This ensures the animation finishes exactly when the server moves to the next
stage. Without baked data (or `AnimSpeed > 0` override), plays at 1x speed.

## Extrapolation (Continuous)

When a clip reaches its end and Extrapolation = Continuous:
1. `StopOnEnd = false` on the AnimancerState (time keeps advancing)
2. `ClipExtrapolator` reads each bone curve's last two position frames from baked data
3. In LateUpdate, applies extrapolated bone positions:
   `lastPosition + velocity * extraTime`
4. Once the server sends a new state, crossfade to the next clip takes over

This replaces separate "loop" clips for continuous motion (hover, drift, aura).
**Rotation holds the last keyframe** (v1 — position curves only from baked data).

## Pitfalls

- **AnimancerComponent** is added at runtime by `PlayerRenderer.LoadModel()`.
  The prefab's built-in Animator must NOT have a dead controller reference.
- **Extrapolation** only activates when the clip reaches its natural end —
  invisible during normal playback where the server transitions before clip end.
- **Baked data frame rate** must be uniform (currently all exported 30fps).
  Variable frame rate would produce inaccurate velocity extrapolation.
- **Rotation extrapolation** (not in v1) would need quaternion data added to
  `BakedAnimationData` format or Editor-only `AnimationUtility.GetCurveBindings()`.
