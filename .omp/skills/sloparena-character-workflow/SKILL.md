---
name: sloparena-character-workflow
description: "Full pipeline for adding a new character to SlopArena: concept, kit, 3daistudio prompt, model import, AnimationTree setup, and C# code wiring. Covers embedded GLB animations + .tscn wrapper approach."
category: game-dev
---

# SlopArena Character Workflow

Trigger when the user wants to design, generate, or implement a new playable character in SlopArena.

## User Workflow Preferences

- **Diagnose the full pipeline before any change** — When debugging (jump doesn't work, hurtboxes misaligned, etc.), DO NOT make random changes in a loop. Instead: trace the complete data/execution flow first (input → simulation → state → FSM → animation → render), identify the exact point of divergence, THEN propose ONE fix with justification. The user will tell you to "step back" and "stop making random changes" if you skip this step.
- **Explain every change before making it** — The user explicitly said "ne fais pas des changements sans m'expliquer" when changes were applied without explanation. Always state the root cause and the proposed fix before applying the patch.
- **Incremental changes, one step at a time** — Do NOT batch all files in one turn. Do ONE file change, wait for feedback, then move to the next.
- **Look at the user's work first.** When the user says "regarde mon X" or "j'ai mis les Y dans le glb", stop proposing solutions and examine what they've already set up (GLB animation names, tscn states, code changes). The user often prepares assets (animations in Blender/GLB, .tscn config) before asking for wiring.
- **Fix at source** — orientation and bone naming are fixed in Blender. The *FixAnimationTracks()* runtime code fix is acceptable for the Godot 4 GLTF track-path bug.
- **Production-ready** — each step must be repeatable for all future characters, not a hack for today.
- **Use the Godot Editor** — prefer iterative manual setup in the Editor. **Exception:** repetitive AnimationTree state wrapper additions/renames can be text-rewritten directly.
- **Prefer direct file reads over Godot MCP for simple inspections** — Godot MCP is useful for editor operations (opening scenes, inspecting live AnimationTree properties through the editor) but is overkill for reading source files. Use `read_file` / `patch` directly for `.cs` and `.tscn` edits. Reserve MCP for operations that genuinely need the editor (checking real-time state, running the project, screenshots, or when the `.tscn` format is too complex for text patching).
- **Direct** — when asked a specific question, answer it directly. Do not execute code or make changes. Do not propose alternatives or caveats unless asked.
- **Let the user experiment with values** — when the user chooses a specific numeric value (crossfade time, blend speed, tick count, etc.), don't argue or say "X is overkill" or "Y is too long". Suggest respectfully once if there's a clear correctness issue, then accept their choice and implement it. The user is iterating by feel in the game.
- **Enforce the standard** — the old animPlayer.Play() fallback is tolerated for legacy Knight but not for new characters.
- **Check existing infrastructure before extending** — When adding a new feature (projectiles, hitboxes, input fields), first verify what the data structures already support. The `Hitbox` struct has `VX/VY/VZ` for velocity, `SpellResolver.Tick()` already moves hitboxes by velocity. The existing system may already cover 80% of what you need — don't recreate it.
- **Do NOT apply Ctrl+A → Scale on only the Armature in Blender** — mesh and bone animation data remains in cm while the Armature node's local transform is reset → 100m character in Godot. Always handle scale in Godot code.

## 1. Concept and Kit Design

### Art Direction (Pixel8r2)
- Pixel art 3D (not blocky Roblox)
- 3-tone cell shading, 1px outlines, no gradients, no dithering
- ~4000 triangles, GLB with Mixamo rig (mixamorig: prefix, 23 bones)
- Fire/effects = Godot VFX, never on model

### Kit Slot Convention (DKO-style)

| Slot | Role | CD (ticks) |
|------|------|------------|
| LMB | 3-hit light combo, 3rd launches | 0 |
| Air LMB | Upward air attack | 0 |
| RMB | Heavy chargeable attack | 15-20 |
| Air RMB | Downward spike | 0 |
| Q | CC (slow/stun/knockup) | 60-120 |
| E | Recovery/mobility | 120-180 |
| R | Get-off-me/burst | 120-180 |
| F | Ultimate finisher | 360-420 |

All characters get AirLMB + AirRMB. No floating/capes/weapons on geometry.

## 2. Animation Pipeline

### Recovering from accidental rest pose change

If you already moved bones in Pose Mode and clicked **Apply Pose as Rest Pose**, all animations break. See `references/blender-rest-pose-fix.md` for the correction technique — it computes per-bone correction matrices and applies them to all action fcurves using Blender 5.x's layered action API (`action.layers[0].strips[0].channelbags[0].fcurves`).

The safest fix corrects **only Hips location** keyframes and leaves rotation/children untouched. This keeps the character facing the rest pose direction (Y+) during animations instead of restoring the original Mixamo facing (Z+).

**Mixamo always uses `rotation_quaternion`**, never `rotation_euler`. Mixamo animations may have inconsistent channel counts (some have 2 location channels instead of 3).

### GLB orientation fix in Blender
1. A (select all: armature + mesh)
2. R Z 180 (if facing wrong direction)
3. Ctrl+A → Rotation
4. Select armature → Pose Mode → A → Pose → Apply → Apply Pose as Rest Pose
5. Now import animations via Append → Actions

### Root Motion Stripping (CRITICAL for Mixamo GLBs)

Mixamo animations have **Hips root motion** — the character drifts in idle, slides during attacks, run doubles the simulation's velocity. All animations must be "in-place".

**Method A — Blender Action Editor (manual, always works):**
1. **File → Open** → character.glb in Blender
2. Switch to **Action Editor**
3. Select animation from dropdown
4. Expand `mixamorig:Hips` → right-click `mixamorig:Hips (Loc)` → **Delete Keyframes**
5. Repeat for ALL animations
6. **File → Export → glTF 2.0 (.glb)** — overwrite
7. Delete `.glb.import` in Godot to force re-import

**Method B — Blender Python script:** `scripts/strip_root_motion.py`

### Animation Name Discovery (CRITICAL STEP)

**Always discover the GLB's real animation names before setting `AnimationNames[]` or writing the .tscn.** Wrong name = T-pose with zero errors.

**Discovery methods:**
1. **(BEST) Run the headless bake** — `godot --headless --script tools/headless_bake.gd --path .`
2. **(Also good) Run `python3 tools/inspect_glb.py`**
3. **Import the GLB in Godot** and check the AnimationPlayer's animation list

### State name vs animation name — two separate things

When the FSM calls `AnimPlayback.Travel("spell_lmb_1")`, it travels to a **state** in the AnimationNodeStateMachine named `"spell_lmb_1"`. That state's sub-resource has an `animation = &"some_glb_name"` which references a GLB animation name.

**When renaming GLB animations, you must update THREE things:**
1. `CharacterDefinition.BuildX().AnimationNames[]` — the strings the FSM passes to `Travel()`
2. `.tscn` `states/{name}/...` entries — the state names in the StateMachine (must match AnimationNames)
3. `.tscn` `animation = &"{name}"` in sub-resources — the GLB animation names (must match what's in the GLB)

**Common failure modes:**
- `No such node: 'spell_lmb_1'` = state name missing in the StateMachine
- `couldn't resolve track` = animation name in sub-resource doesn't exist in the GLB
- `transitions[i].from == p_from && transitions[i].to == p_to` = duplicate transitions

### Character size verification

When importing a new character, verify its mesh dimensions match existing characters at the GLB level (scale=1 on all parent nodes ≠ same size). See `references/character-size-comparison.md` for the full workflow: open both .tscn's → run project → `game_eval` to read mesh `get_aabb()` → compare Y height, Y origin, and X/Z proportions.

**Rule of thumb:** If the new character's mesh height (AABB Y size) differs by >1.5× from Manki (~1.516m), adjust the import scale or add a parent node scale factor. Update `HurtboxBoneScale` to match.

### Scale coupling: VisualScale MUST drive both model + hurtbox scale

**Everything is data-driven now.** `PlayerModel.Load()` uses `_charDef.VisualScale` instead of a hardcoded per-class scale. `ServerSimulation` uses `_charDef.HurtboxBoneScale`. These two values MUST be the same number.

| Character | VisualScale | HurtboxBoneScale | Result |
|-----------|------------|------------------|--------|
| Manki | 1.0 | 0.01 | ✅ Works — Mixamo cm→m handled by GLB import, VisualScale=1, baked data in cm, HurtboxBoneScale=0.01 converts |
| Bunny | **0.022** | **0.022** | ✅ Must be equal — non-Mixamo GLB units need same conversion on both sides |

**⚠️ Client and server compute hurtbox positions from DIFFERENT sources:** the client reads the actual skeleton (`Skeleton3D.GetBoneGlobalPose()` at visual scale), while the server reads baked data (`BakedAnimationData × HurtboxBoneScale`). If these scales differ, the foot hurtbox on the server is at a different Y than the foot hurtbox on the client. See `sloparena-combat-engine` → "Client/server hurtbox scale agreement" for the full diagnostics table.

**The foot bone vs capsule bottom relationship also differs per character's GLB coordinate space:**

| Character | Bone foot relative to capsule bottom | SoleOffset meaning |
|-----------|--------------------------------------|-------------------|
| Manki | Bone foot is **inside capsule** (0.116 above capsule bottom) | SoleOffset = 0.47 = extra downward push from bone to mesh surface |
| Bunny | Bone foot is **below capsule** (0.113 below capsule bottom) | SoleOffset = 0.35 = correction to align bone-based hurtbox with visual sole |

**Rule of thumb:** If after setting VisualScale = HurtboxBoneScale the model's bones sit inside the capsule, SoleOffset should be positive (push visual down to ground). If bones sit below the capsule already, SoleOffset is negative/smaller to pull the visual up.

### ModelSoleOffset — visual-only, NOT on server

`ModelSoleOffset` adds extra downward offset so the mesh sole touches the ground. It belongs ONLY in `PlayerModel.ComputeModelYOffset()` — NEVER in `ServerSimulation` hurtbox formulas. The server uses `py - capsuleHalf + by` (universal, no per-character offset) because bake normalization puts the lowest idle bone at Y=0. The visual model offset and server hurtbox positioning are independent concerns. See `sloparena-combat-engine` → "Two Y formulas" pitfall.

**Hurtbox bone radii must also scale with character size:**
| Bone | 1m char (×0.01) | 1.7m char (×0.017) |
|------|---------------|-------------------|
| Head | 0.22 | 0.35 |
| Spine2/Hips | 0.28 | 0.40 |
| Hands | 0.12 | 0.18 |
| Feet | 0.16 | 0.22 |

### Headless bake (parameterized, single script)

Single `tools/headless_bake.gd` accepts character names as CLI args:

```bash
./tools/bake_all.sh                              # bakes all known characters
```

or individually:

```bash
godot --headless --script tools/headless_bake.gd --path . -- manki
godot --headless --script tools/headless_bake.gd --path . -- bunny
godot --headless --script tools/headless_bake.gd --path . -- manki bunny newchar
```

**⚠️ T-pose invariant reference (Juillet 2026):** The script samples `tpose_hips_inv` once from the rest pose (no animation), not per-frame from the animated Hips. This puts every animation in the same T-pose-Hips-local space. The idle animation captures real T-pose→idle offsets. The 11 standard Mixamo bones are hardcoded in `BONE_NAMES`.

### Debugging baked data coordinate space

When `ComputeModelYOffset()` produces wrong results and the visual model floats:

1. **Read the `.bin` file** to check bone Y values at idle frame 0 (use `tools/read_skeleton_bin.py` or see `references/baked-skeleton-debugging.md`)
2. **Check the Hips bone Y** — it should be near 0 (within ±5). If it's far from 0 (e.g. Y=+47), the skeleton origin was offset when baking
3. **Check if all Y values are positive** — the lowest bone should be negative (below the root); all-positive Y means the coordinate space is shifted
4. **Verify `HurtboxBoneScale` matches the visual model scale** — see scale coupling table above
5. **Regenerate the `.bin`** if the coordinates are wrong: ensure the Skeleton3D is at origin (0,0,0) before running `headless_bake.gd`

**`ComputeModelYOffset()` — now uses "tpose" if available:**

See `references/baked-skeleton-debugging.md` for full diagnostics and the T-pose fallback pattern.

## 3. Code Wiring

### AnimationTreeBuilder (replaces .tscn sub-resources)

The old approach required hand-editing `.tscn` sub-resources for every AnimationTree state. The `AnimationTreeBuilder` (`Scripts/Animation/AnimationTreeBuilder.cs`) generates the entire `AnimationNodeStateMachine` from `CharacterDefinition` data at runtime.

**How it works:**
```csharp
// In PlayerController._Ready():
animTree.TreeRoot = AnimationTreeBuilder.Build(animPlayer, _charDef);
```

The builder reads animation name fields from `CharacterDefinition`:
- `IdleAnim`, `RunAnim`, `DashAnim` — locomotion (looping, UseCustomTimeline + TimelineLength + LoopMode)
- `JumpAnim`, `FallAnim` — dedicated standalone states with 0.15s crossfade between them
- `HitSmallAnim`, `HitMediumAnim`, `HitHardAnim` — hit reactions
- `AbilitySpec.AnimationNames[]` — attacks (auto-discovered from all 8 slots)
- `ClipOverrides[]` — per-clip timeline/loop overrides

No blend space. JumpState targets "jump", FallState targets "fall" dedicated standalone states with a 0.15s crossfade between them. No parameters/air/blend_position. No blend logic in FallState. LandingState has AnimationName = "" (no Travel crossfade handles it).

Crossfade between different animation poses causes rotation. If jump and fall have different body orientations (arms up vs arms down), any crossfade >0.15s between them visibly twists bones. Keep ALL animation state crossfades at 0.15s. If you want the jump pose to linger, increase JumpDurationTicks in MovementStats instead.

**Ground snap removed:** The server no longer sets `s.PY = groundY` on landing. `MoveAndSlide()` on the Godot side handles floor contact naturally through collision detection, eliminating the visual Y-pop.

**Adding a new character is now:**
1. Populate the animation name fields in `CharacterDefinition.BuildX()`
2. Add `AnimationNames[]` to each `AbilitySpec` (was already required)
3. Add `ClipOverrides[]` only for clips that need custom timeline/loop
4. **Zero `.tscn` sub-resource editing**

**`AnimationClipConfig` struct (in Shared/):**
```csharp
public struct AnimationClipConfig
{
    public string Name;
    public ClipLoopMode? LoopMode;      // None/Linear/PingPong
    public float? StartOffset;
    public float? TimelineLength;
    public bool StretchTimeScale;
}
```

**Loop modes:** `None` — once, `Linear` — loop (idle/run/dash), `PingPong` — forward/back.
**State name case:** `"Idle"` (capital I), `"Run"` (capital R), `"air"`, `"dash"`, `hit_small`, etc. Must match the C# state's `AnimationName` property exactly.

Crossfades use `AnimationNodeStateMachineTransition` (default 0.15s). All transitions use the same xfade. `Start` node is implicit — transitions from `"Start"` work without adding it.

**⚠️ Godot C# API traps:**
- Start/End nodes are built-in — do NOT `AddNode("Start", ...)`
- BlendTree output node is built-in — do NOT `AddNode("output", ...)` — just connect to it
- Looping needs `UseCustomTimeline = true` + `TimelineLength = 1.0f` — not just `LoopMode`
- BlendSpace1D `Discrete` mode for jump/fall (different poses twist under Interpolated)

See `references/animationtree-builder.md` for the full builder API, transition rules, data source table, and pitfall list.

### .tscn after stripping

After the builder is wired in, the `.tscn` needs only:
```tscn
[node name="AnimationTree" type="AnimationTree" parent="."]
root_node = NodePath("../model_name")
anim_player = NodePath("../model_name/AnimationPlayer")
```
FSM state nodes + skeleton bone data are preserved. All `[sub_resource]` blocks for animation are removed. Remove `tree_root = SubResource(...)` and all `parameters/*/TimeScale/scale = 1.0` lines from the AnimationTree node.

### TimeScale compatibility

`ApplyAnimationTimeScales()` calls `animTree.Set($"parameters/{animName}/TimeScale/scale", timeScale)` — the builder creates `AnimationNodeTimeScale` nodes producing the same parameter paths as the old `.tscn`. No changes needed.

### Legacy `.tscn` patch approach (pre-builder)

Before the builder existed, states were added via raw `.tscn` text patching. This is **no longer needed** — add to `AddTransitions()` instead. Documented only for understanding existing changes:

| File | Change |
|------|--------|
| Shared/CharacterDefinition.cs | Enum + BuildRegistry + BuildX() — set `VisualScale` and `HurtboxBoneScale` to the same value |
| Shared/Characters/XData.cs | Set `VisualScale` (Manki=1.0, Bunny=0.022, etc.), `ModelSoleOffset` (0.0 default), and ability data including optional `ProjectileConfig` for targeted throw Q abilities |
| Scripts/Abilities/XAbility.cs | Create an ability class per unique gameplay mechanic — see `sloparena-combat-engine` skill for the `Ability` base class API (OnActivate, Tick, OnDeactivate). Simple instant abilities just set `ActiveSlot` in Tick; aimed abilities use GroundCircle/ArcDrawer/GroundCone helpers |
| Scripts/Entities/PlayerModel.cs | `ComputeModelYOffset()` — scans baked idle frame 0 for lowest bone Y; model scale = `_charDef.VisualScale` |
| Scripts/Entities/PlayerController.cs | `ApplyAnimationTimeScales()` computes TimeScale from baked frame count / DurationTicks |
| Shared/ServerSimulation.cs | Hurtbox Y uses `wy = py - capsuleHalf + by` in BOTH `Tick()` and `BuildEntitiesFromState()` — no ModelSoleOffset, universal formula |
| Scripts/World/TrainingMatch.cs | Register NPC with its own CharacterDefinition + baked data (not player's) |
| Scripts/World/PvPMatch.cs | Call `SetBakedData(_playerBakedData)` on the Opponent visual too |
| Scripts/UI/ClassSelectUI.cs | Button + description |
| docs/characters/x.md | Kit doc in English |

### Creating an ability (after ability-architecture refactor)

After the refactor, creating a new ability touches 3-4 files instead of 6+:

1. **`Shared/Characters/XData.cs`** — add an `AbilitySpec` with `Params` dictionary for tunable values
2. **`Scripts/Abilities/XAbility.cs`** — create a class extending `Ability`, override `OnActivate`, `Tick`, `OnDeactivate`
3. **`Scripts/Abilities/AbilityFactory.cs`** — add a pattern-match arm in the `Create()` switch
4. **`assets/characters/x/x.tscn`** — add an `aimed_charge` FSM node ONLY if the ability is hold-to-charge (uses AimedChargeData)

No changes to `PlayerController` or `Simulation`.

### Hold-to-charge ability pattern

For abilities with `AimedChargeData` (e.g. RMB flamethrower): the ability and FSM state split responsibilities cleanly:

| Concern | Handled by | Mechanism |
|---------|-----------|-----------|
| Movement blocking | `AimedChargeState` (FSM) | `CanMove = false` → `BuildInputState` zeroes movement |
| Charge animation loop | `AimedChargeState` (FSM) | `AnimationName` from `AimedChargeData.ChargeAnimName`, played via `Enter()` → `AnimPlayback.Travel()` |
| Charge timing | `Ability` class | `Tick()` increments `_chargeTicks`, syncs `state.ChargeTicks` to sim |
| Release detection | `Ability` class | `Tick()` checks `Input.IsMouseButtonPressed()` on each frame |
| Effect trigger | `Ability` class | `TriggerEffects(player)` before returning `ActiveSlot` |

**Flow:**

```
Player presses RMB → ActivateAbility(AerosolFlame)
  → OnActivate: configure AimedChargeState, FSM.TransitionTo("aimed_charge")
  → FSM blocks movement, plays charge anim
  → Tick() returns null each frame (meaning "still charging, keep alive")
Player releases RMB → Tick() detects release
  → returns { ActiveSlot = SlotNumber }
  → _Process: DeactivateAbility() → OnDeactivate:
      → set AttackState.NextAnimName = AimedChargeData.AttackAnimName
      → FSM.TransitionTo("attack")
  → Next frame sim processes attack, existing _PhysicsProcess code detects
    simState == Attacking but doesn't retransition (already in "attack")
```

**Critical — Tick() null semantics:** `Tick()` returning `null` means "ability is still active (charging/aiming)". It does NOT mean "finished". The `_Process` loop only deactivates the ability when `Tick()` returns a value with `ActiveSlot.HasValue == true`. Instant abilities fire immediately by returning `ActiveSlot` on their first Tick() call.

**Setup required in `.tscn`:**

```tscn
[ext_resource type="Script" path="res://Scripts/Animation/States/AimedChargeState.cs" id="10_charge"]

[node name="aimed_charge" type="Node" parent="FSM" unique_id=...]
script = ExtResource("10_charge")
```

The `AimedChargeState` class is in `Scripts/Animation/States/AimedChargeState.cs` — it's already coded to read `AimedChargeData.ChargeAnimName` and set `CanMove = false`. No additional configuration in the `.tscn` is needed beyond the node reference.

### Targeted projectile — three-way shared trajectory

For targeted-throw abilities (Q with `ProjectileConfig`), THREE things must follow the same parabolic arc:

| Component | Location | Compute method |
|-----------|----------|----------------|
| Predicted arc | `ArcDrawer.Draw()` (client, during aiming) | `CombatMath.ComputeProjectileLaunch()` |
| Server hitbox | `ServerSimulation.cs` lines 233-248 | `CombatMath.ComputeProjectileLaunch()` with same params |
| Visual bomb model | `MankiAbilities.RoundBomb()` (client, after firing) | `CombatMath.ComputeProjectileLaunch()` → Euler integration |

All three use `CombatMath.ComputeProjectileLaunch(targetDistance, launchAngleRad, gravity, heightOffset, out speed, out hSpeed, out vSpeed)` with the SAME inputs from `ProjectileConfig` + `CharacterState.AimYaw`/`AimTargetDistance`.

**Common parameters passed to all three:**
- `targetDistance` = `state.AimTargetDistance` (clamped to `ProjectileConfig.MaxRange`)
- `launchAngleRad` = `ProjectileConfig.LaunchAngleDeg × π/180`
- `gravity` = `ProjectileConfig.Gravity`
- `heightOffset` = `-capsuleHalf - ProjectileConfig.LaunchOffsetY` (target is below launch point)

**Client visual projectile** (`ParabolicProjectile` helper in `MankiAbilities.cs`) uses Euler integration identical to `SpellResolver.Tick()`:
```csharp
position += velocity * dt;
velocity.y -= gravity * dt;
```

### MatchManager.BuildServerGhostEntities() — no hardcoded classes

Replace `CharacterClass.Manki` with `_charDef.Class`:
```csharp
var def = CharacterRegistry.Get(_charDef.Class); // NOT CharacterClass.Manki
```

### ServerApp now in solution

`ServerApp/` was added to `SlopArena.sln`. `dotnet build` at root now builds everything. If the solution file breaks:
```bash
dotnet build ServerApp/ServerApp.csproj
```

### Server character class passthrough

1. **`Main.cs`** — pass selectedClass:
   ```csharp
   StartLocalServer(selectedClass);
   Arguments = $"\"{dllPath}\" \"{playerClass}\"",
   ```
2. **`ServerApp/Program.cs`** — parse arg:
   ```csharp
   if (args.Length > 0 && Enum.TryParse<CharacterClass>(args[0], true, out var parsed))
       playerClass = parsed;
   ```

### AnimationController fixes

- `_tracksFixed`: `static` → instance field
- `RootNode`: point to GLB instance, not wrapper root:
  ```csharp
  Node? glbRoot = skeleton.GetParent()?.GetParent();  // Armature's parent = GLB instance
  ```

### Animation TimeScale (data-driven)

The animation playback speed must match `DurationTicks` from CharacterDefinition so the visual duration equals the simulation tick count.

**Formula:** `TimeScale = bakedFrameCount / DurationTicks`

Called once at `_Ready()` in `PlayerController.ApplyAnimationTimeScales()`:

```csharp
string paramPath = $"parameters/{animName}/TimeScale/scale";
float timeScale = frameCount / (float)durationTicks;
animTree.Set(paramPath, timeScale);
```

This iterates all 8 ability slots (LMB, RMB, AirLMB, AirRMB, Q, E, R, F) and each stage, maps animation name → DurationTicks, reads `frameCount` from `BakedAnimationData`, and sets the TimeScale on the AnimationTree.

**Requirements:**
- The AnimationTree's per-animation blend trees must have a `TimeScale` node with a `scale` parameter
- The baked `.bin` must contain the animation (frame count loaded from `BakedAnimationData`)
- Looping animations (idle/run/jump/fall) stay at 1.0× (not covered by DurationTicks)

**Example — Bunny LMB stage 1:**
- Baked: "spell_lmb_1" = 93 frames
- Definition: DurationTicks = 50
- TimeScale = 93 / 50 = **1.86×**

The animation plays 1.86× faster, lasting exactly 50 ticks (0.83s).

**⚠️ Server-client frame mismatch:** The server advances `_animFrames[id]` by **1 tick per frame**, but now maps tick → baked frame using `bakedFrame = tick * fc / durationTicks` for attack animations. This is applied in `ServerSimulation.cs` when `state.State == ActionState.Attacking && state.AttackSlot > 0`. Looping animations (idle/run/jump/fall) keep the old 1-frame-per-tick with wrap (no TimeScale).

| Tick | Server reads baked frame | Client visual is at baked frame |
|------|--------------------------|--------------------------------|
| 0 | 0 | 0 |
| 25 | 25 | 25 × (93/50) = **46** |
| 49 | 49 | 49 × (93/50) = **91** |

The server only reads the first `DurationTicks` frames of the baked animation (frames 0-49 out of 93). The remaining baked frames are never used. The client plays all 93 frames compressed into 50 ticks.

**Fix:** The server must map `tick → bakedFrame` using the TimeScale ratio:
```csharp
int bakedFrame = (int)(currentTick * fc / (float)durationTicks);
```
This replaces the current 1-frame-per-tick increment for non-looping attack animations. For looping animations (idle/run/jump/fall), the current 1-frame-per-tick with wrap is correct.

## Pitfalls Checklist

### Embedded editor input quirk

Mouse motion and click-press events do NOT reach `_UnhandledInput` in embedded editor mode — the Godot Editor consumes them for its own viewport navigation. Only wheel events pass through. Camera orbit and attack click handlers must go in `_Input()` instead. See `references/godot-embedded-input.md`.

### Crosshair positioning in embedded mode

`DisplayServer.WindowGetSize()` returns the embedded window size (e.g. 1729x973), NOT the actual game viewport (e.g. 1920x1080). Use `GetViewport().GetVisibleRect().Size` for screen-space UI. See `references/godot-embedded-input.md`.

### Jump input — centralized in PlayerController

All jump transitions are handled in `PlayerController._Process()` rather than in individual states:

```csharp
string currentState = _fsm?.CurrentStateName ?? "";
if (_inputCtrl.JumpJustPressed && currentState is "idle" or "run" or "fall")
    _fsm?.TransitionTo("jump");
```

This means **no state's `OnProcess()` checks `InputCtrl.JumpJustPressed`** — not IdleState, not RunState, not LandingState, not FallState, not JumpState.

**Why:** Keeps jump mapping in one place. Adding a new state that can jump just requires adding its name to the `is` pattern.

**JumpState** (C# state, separate from FallState):
- Has `JumpDurationTicks` (per-character from `MovementStats`)
- Targets `AnimationName = "jump"` (dedicated AnimationTree node — no blend space)
- After ticks expire, transitions C# to FallState, which targets `AnimationName = "fall"`
- The crossfade between `jump → fall` is configurable via a separate `AnimationNodeStateMachineTransition`

**1. Double-jump consumed by duplicate handler**
Lines 123 and 450 both process jump input. Line 123 consumes one JumpsLeft, then line 450 consumes another → both jumps gone in one frame → no double jump possible.
**Fix:** Remove the duplicate at line 123.

**2. PlatformLandTolerance eats the first frame of the jump**
`PlatformLandTolerance = 0.2f` snaps the character back to `groundY` if `PY <= groundY + 0.2`. After a jump, PY advances ~0.156m → still within 0.2m → snap to ground, VY=0.
**Fix:** `PlatformLandTolerance = 0.02f`.

### FallState — no dedicated animation (keeps previous state)

`FallState` now has `AnimationName = ""` — it keeps showing whatever animation was last playing (jump, run, idle). When landing, the crossfade goes directly from that pose to idle/run without a fall-pose flash.

- **No fall pose flash** — landing transitions directly from the previous animation
- Detects ground contact (3-frame threshold) → transitions to `"run"` or `"idle"` directly (LandingState was removed)
- Ground detection also calls `Movement.ResetJumps()` so jumps are refreshed on landing
- Jump input is handled centrally in `PlayerController._Process()`

### AirState landing transition — counter pattern

**Never stack buffers (MovementComponent) with `_wasGrounded` tracking (AirState).** 
- **MovementComponent:** `s.IsGrounded = _body.IsOnFloor();` — direct, NO buffer
- **AirState:** consecutive grounded frames counter:
  ```csharp
  private int _groundedCount;
  if (Movement.IsGrounded) {
      _groundedCount++;
      if (_groundedCount >= 3)  // no vy check!
          StateMachine.TransitionTo(moving ? "run" : "landing");
  } else { _groundedCount = 0; }
  ```
- **`vy <= 0f` trap:** `Player.Velocity.Y` can be 0.0 on the exact frame of ground contact. **Remove the vy check entirely.**

Log diagnostic pattern: `[Air] Land tick={Engine.GetPhysicsFrames()} count={_groundedCount}`

### `vy <= 0f` trap — REMOVE it

`Player.Velocity.Y` can be 0.0 or slightly positive on the exact frame of ground contact (`MoveAndSlide()` + ground snap). The AirState's landing condition should ONLY check `_groundedCount >= 3`, NOT `&& vy <= 0f`. The 3-frame counter already prevents premature re-landing after a jump.

### Duplicate AnimationTree node in .tscn

A second `AnimationTree` node with `parent="AnimationTree"` (nested inside the first) causes `'../bunny' is an invalid root_node path`. Fix: remove the nested node.

### Duplicate transition error

`transitions[i].from == p_from && transitions[i].to == p_to` = two transitions between the same from→to states. Give each ability a unique AnimationName (state name) — even as placeholders.

### .tscn read_file line number trap

`read_file()` returns `N|` prefix content. Writing it back without stripping kills the file. Fix:
```python
cleaned = re.sub(r'^\d+\|', '', content, flags=re.MULTILINE)
```

### Missing state for ult/specific skill

Adding a new state like `spell_f` requires BOTH:
1. states/spell_f/node = SubResource("bt_...") + states/spell_f/position = Vector2(...) (state declaration)
2. Transition entries in the transitions = [...] array (from/to for each animation-interruptible transition from Idle/Run/air)

### Legacy `.tscn` patch approach (pre-builder) — NO LONGER NEEDED

The builder (`Scripts/Animation/AnimationTreeBuilder.cs`) now generates all AnimationTree states from `CharacterDefinition` data. No `.tscn` patching for animation states. Documented only for understanding existing changes.

### Stale FSM node entries in .tscn after removing scripts

When deleting old FSM state files (e.g. AimedChargeState.cs, AimedThrowState.cs), you MUST also remove:
1. The [ext_resource] line referencing the deleted script
2. The [node name="state_name"] block under the FSM node

Missing either causes a Parse Error at scene load: the Godot parser tries to resolve the ext_resource reference and fails. The error message includes the .tscn file and line number. Check for these references when the error says "Parse error" pointing at a .tscn line.

### VisualScale + ModelSoleOffset consistency

Can get accidentally changed by `replace_all=true` patches. After any large-scale text patch, verify:
- Manki: `VisualScale = 1.0f`, `HurtboxBoneScale = 0.01f`, `ModelSoleOffset = 0.0f`
- Bunny: `VisualScale = 0.022f`, `HurtboxBoneScale = 0.022f`, `ModelSoleOffset = 0.0f`

VisualScale and HurtboxBoneScale MUST be equal for non-Mixamo characters. `ModelSoleOffset` is a visual tunable (sole thickness), NOT a server physics value — the server uses `py - capsuleHalf + by` which is universal when bake normalization is correct (lowest idle bone ≈ 0).

### StatusSpells null-safe guard

`StatusSpells.CreateImpactVisual()` can crash if the VFX owner node isn't in the scene tree yet (NPC spawning race condition). Guard with:
```csharp
if (combat.GetOwner() is Node3D owner && owner.IsInsideTree())
    impact.GlobalPosition = position;
```

### Dash direction — MoveY=0 + simulation overwrite trap

Two bugs compound for dash direction:

**Bug 1 — MoveY hardcoded to 0 in Simulation.Tick():**
`Simulation.Tick()` line 127: `StartDash(ref s, stats, input.MoveX, 0f)`. The forward/backward component (`MoveY`) is **always 0**. Player pressing Z (forward) with slight sideways input → normalized `DashDir = (-1, 0)` = dash goes **fully sideways**.
**Fix:** `StartDash(ref s, stats, input.MoveX, input.MoveY)`.

**Bug 2 — DashState.Enter() overwrites simulation:**
Dash direction is set THREE times:
1. `Simulation.Tick()` → `StartDash(...)` (wrong MoveY=0)
2. `PlayerController` reads `DashDirX/Z` → passes to `DashState.SetDirection()`
3. `DashState.Enter()` → `Movement.StartDash(_dirX, _dirZ)` overwrites everything
**Fix:** Remove `Movement.StartDash()` from `DashState.Enter()`. Use `_movementComponent.State.DashDirX/Z` for FSM direction.

The simulation's `StartDash` correctly falls back to `FacingYaw` when input length < 0.01f.

See `references/dash-direction-trace.md` for full trace.

### NPCs use player's `_charDef` in simulation — FIXED

**Issue (TrainingMatch):** `RegisterEntity(100, _charDef, ...)` used the player's CharacterDefinition for the NPC. When playing as Bunny, the NPC Manki was simulated with Bunny's capsule (1.5m height) and HurtboxBoneScale (0.02) instead of Manki's (1.3m, 0.01). This caused the NPC's physics position to diverge from its visual.

**Fix:** Register the NPC with its own CharacterDefinition matching its visual class:
```csharp
var npcClass = CharacterClass.Manki; // matches SpawnNPCs() logic
var npcDef = CharacterRegistry.Get(npcClass);
var npcBaked = LoadBakedData(npcDef);
_localSim.RegisterEntity(100, npcDef, ..., npcBaked);
```

### Q ability with targeted throw: ProjectileConfig + Ability class

If the character's Q ability uses **targeted throw** (hold-to-aim, release-to-throw projectile), two things beyond the standard ability data are required:

1. **`Shared/Characters/XData.cs`** — add `ProjectileConfig` to the Q `AbilityData` (see `sloparena-combat-engine` → "Targeted Projectile System" for the full schema). The stage `HitboxEvent` should use `TriggerTick=0` and `Interruptible=false`.

2. **`Scripts/Abilities/`** — create a new ability class extending `Ability` (e.g., `RoundBomb.cs`). See the `sloprena-combat-engine` skill for the base class API and drawing helpers. The ability's `Data` property is set by `PlayerController` from `_charDef.GetSlotAbility(2, false)` — you only need to override `OnActivate`, `Tick`, `OnDeactivate`, and optionally `OnInput`.

**For instant abilities (LMB, E, R, F, etc.):** No FSM state changes needed — the standalone `Ability` class handles everything internally via Tick() → ActiveSlot.

**For hold-to-charge abilities (RMB with AimedCharge, etc.):** You DO need an `aimed_charge` FSM node in the character's `.tscn`. The FSM state handles movement blocking (`CanMove = false`) and the charge animation loop. The ability class handles timing, release detection, and TriggerEffects. See "Hold-to-charge ability pattern" below.

**Camera yaw convention — NEGATE Z, NOT +π:** The naive fix for Godot's −Z forward vs math's +Z forward is adding `π` to the yaw: `(Sin(θ+π), Cos(θ+π)) = (−Sin(θ), −Cos(θ))`. This flips BOTH X and Z — Z goes forward correctly, but X is negated, so the crosshair appears on the LEFT when the camera looks RIGHT.

**Correct fix — negate only Z:**
```csharp
// RoundBomb.cs — crosshair position
targetPos.X += aimSin * _targetDistance;    // Sin stays positive
targetPos.Z -= aimCos * _targetDistance;    // NEGATE Cos for Godot's −Z

// RoundBomb.cs — initial yaw (no +π):
_aimYaw = player.GetCameraYaw();  // was: cameraYaw + π

// MankiRoundBomb.cs — server projectile VZ:
VZ = −hSpeed * aimCos;  // was: +hSpeed * aimCos

// MankiAbilities.cs — visual projectile VZ:
hSpeed * aimSin, vSpeed, −hSpeed * aimCos
```

Three places must agree: crosshair position, visual projectile VFX, and server projectile hitbox. If any one still uses `+Cos(Z)` or `+π`, the crosshair, VFX, and actual hitbox path disagree.

**Ability classes are one-shot instances:** Each ability is a `new` instance created on every activation. Do NOT reuse instances — per-activation state is stored directly on the class. The `Data` property (from `CharacterDefinition`) is set before `OnActivate()` is called, so you can read `Data.ProjectileConfig`, `Data.AimedCharge`, etc. directly — no `Configure()` method needed.

### NPCs missing SetBakedData — FIXED

**Issue:** `TrainingMatch.SpawnNPCs()` did NOT call `SetBakedData()` on NPCs. Without baked data, `ComputeModelYOffset()` returned `_charDef.ModelYOffset = 0` instead of the auto-calculated offset. The visual model sat at capsule center (Y=0) instead of being pushed down to align feet with the capsule bottom.

**Symptoms:**
- Hurtboxes float ~0.5m above the model
- Only visible when NPC character class ≠ Manki (Manki's default ModelYOffset=0 is close to the auto-computed value)
- Affects ALL NPCs, not just the one with the wrong class

**Fix pattern:**
```csharp
var npcDef = CharacterRegistry.Get(npcClass);
var npcBaked = LoadBakedDataFromDef(npcDef);
npc.SetBakedData(npcBaked);
```

⚠️ **This only helps if the baked `.bin` file has correct coordinate data.** If the baked data itself has offset coordinates (all-positive Y values, Hips not near origin), the auto-computed offset will still be wrong. See `references/baked-skeleton-debugging.md` for diagnostics and regeneration steps.
