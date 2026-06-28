# Unity Animation System

> Single-layer trigger-driven Unity Animator. Server-authoritative state drives transitions via parameters. No `Play()` seeking.

## Architecture

```
CharacterState (server simulation)
  → PlayerRenderer.UpdateAnimationState()
    → SetBool/SetFloat/SetTrigger/SetInteger
      → Animator transitions between states
```

**Single layer.** All states (Movement blend tree, Jump, Fall, Land, Dash, Hitstun, all ability animations) live in one state machine. No layer blending — this is a platform fighter, there's no "upper body shoots while lower body walks" scenario.

## Parameters

| Name | Type | Purpose |
|------|------|---------|
| `Speed` | Float | Drives Idle↔Run blend (0-1) |
| `IsGrounded` | Bool | Controls Fall→Land, Movement→Fall transitions |
| `Jump` | Trigger | Fired once on JumpSquat entry |
| `AttackSlot` | Int | 0-5, maps to ability slot (0=LMB, 1=RMB, 2=Q, 3=E, 4=R, 5=F) |
| `ComboStage` | Int | Current combo stage index within the ability |
| `Attack` | Trigger | Fired on combat entry + combo stage change |
| `Dash` | Trigger | Fired when server sets Dashing state |
| `Hitstun` | Trigger | Fired when server sets Hitstun state |
| `Idle` | Trigger | Force-clear combat, AnyState → Movement |

## State machine layout

```
Movement (BlendTree: Idle↔Run via Speed)  ← default state
├── Jump (trigger: Jump)
├── Fall (bool: !IsGrounded)
├── Land (exit time from Fall)
│
AnyState ──→ Dash (trigger: Dash)
AnyState ──→ Hitstun (trigger: Hitstun)
AnyState ──→ Movement (trigger: Idle)
AnyState ──→ spell_lmb_1 (trigger: Attack + slot=0 + stage=0)
AnyState ──→ spell_lmb_2 (trigger: Attack + slot=0 + stage=1)
             ... one per ability animation name
```

Each combat state auto-exits to Movement at 85% clip completion (fallback). The Idle trigger provides immediate force-clear when the server ends combat early.

## PlayerRenderer animation driver

`UpdateAnimationState()` in `PlayerRenderer.cs`:

1. Sets `IsGrounded` and `Speed` every frame — drives movement transitions
2. Fires `Jump` trigger once on JumpSquat entry
3. On combat state change (Attacking/Dashing/Hitstun):
   - Resets all combat triggers
   - Sets `AttackSlot` + `ComboStage` integers
   - Fires the appropriate trigger
4. On combat end (server moves to Idle while previous frame was combat):
   - Fires `Idle` trigger → force-clear to Movement

No `Play()` calls during combat — the Animator plays naturally after the trigger transition. No seeking, no normalized time computation, no frame counting.

## Generating the controller

**SlopArenaAnimatorGenerator** (`Assets/Scripts/Editor/SlopArenaAnimatorGenerator.cs`):

Right-click a character's model folder in the Project view → **Create SlopArena Animator**.

The generator:
1. Scans all FBX/GLB files in the folder tree for AnimationClip sub-assets
2. Builds a name→clip dictionary from the files
3. Creates/updates a `CharacterAnimationConfig` ScriptableObject (for manual tweaking)
4. Creates the AnimatorController with all parameters and states
5. Registers the BlendTree as a sub-asset for proper serialization
6. Returns a fully-populated controller with clips assigned

It's a `[MenuItem]` static method that runs in the Unity Editor. No external Python needed.

On new character:
1. Place animation FBX files in the character's model folder
2. Run Create SlopArena Animator on that folder
3. Any missing clips get logged as warnings — drag manually in the Animator window

## Pitfalls

- **Struct copy trap**: `controller.layers[0].defaultWeight = 1f` sets on a struct copy. Must write back: `layers = controller.layers; layers[0].defaultWeight = 1f; controller.layers = layers;`
- **Clip naming**: The generator matches FBX clip name → state name by case-insensitive string. If your FBX has internal names like "Take 001", they won't match. Either rename them in the FBX import or add aliases to `BuildClipMap()`.
- **Humanoid rig**: Animation FBXs must have valid humanoid bone mappings (via `avatarSetup=2 CopyFromOther` if they export generic curves). Without this, clips play at the right time but no bones move.
