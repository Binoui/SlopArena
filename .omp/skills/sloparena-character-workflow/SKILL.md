---
name: sloparena-character-workflow
description: "Full pipeline for adding a new character to SlopArena: concept, kit, model import, clip config, hurtbox bake, and C# code wiring. Covers the Unity FBX/GLB import path + CharacterAnimationConfig approach."
category: game-dev
---

# SlopArena Character Workflow

Trigger when the user wants to design, generate, or implement a new playable character in SlopArena. Full reference: `docs/characters/character-import-checklist.md` (checklist lives there — this skill is the condensed pipeline).

## User Workflow Preferences

- **Diagnose the full pipeline before any change** — When debugging (jump doesn't work, hurtboxes misaligned, etc.), DO NOT make random changes in a loop. Instead: trace the complete data/execution flow first (input → simulation → state → action → animation → render), identify the exact point of divergence, THEN propose ONE fix with justification. The user will tell you to "step back" and "stop making random changes" if you skip this step.
- **Explain every change before making it** — The user explicitly said "ne fais pas des changements sans m'expliquer" when changes were applied without explanation. Always state the root cause and the proposed fix before applying the patch.
- **Incremental changes, one step at a time** — Do NOT batch all files in one turn. Do ONE file change, wait for feedback, then move to the next.
- **Look at the user's work first.** When the user says "regarde mon X" or "j'ai mis les Y dans le glb", stop proposing solutions and examine what they've already set up (GLB animation names, imported clip names, code changes). The user often prepares assets (animations in Blender/GLB, clip configs) before asking for wiring.
- **Fix at source** — orientation and bone naming are fixed in Blender; no runtime track remapping.
- **Production-ready** — each step must be repeatable for all future characters, not a hack for today.
- **Prefer the Unity Editor for manual setup** — import options, clip renaming, prefab wiring happen in the Editor. Repetitive operations can be scripted (Editor tools) when the format is too complex for manual work.
- **Prefer direct file reads for simple inspections** — reserve Unity MCP for operations that genuinely need the editor (checking live state, running the project, screenshots).
- **Direct** — when asked a specific question, answer it directly. Do not execute code or make changes. Do not propose alternatives or caveats unless asked.
- **Let the user experiment with values** — when the user chooses a specific numeric value (crossfade time, tick count, etc.), don't argue or say "X is overkill" or "Y is too long". Suggest respectfully once if there's a clear correctness issue, then accept their choice and implement it. The user is iterating by feel in the game.
- **Enforce the standard** — all clip playback goes through Animancer via `PlayerRenderer`; no ad-hoc fallback paths for new characters.
- **Check existing infrastructure before extending** — When adding a new feature (projectiles, hitboxes, input fields), first verify what the data structures already support. The `Hitbox` struct has `VX/VY/VZ` for velocity, `SpellResolver.Tick()` already moves hitboxes by velocity. The existing system may already cover 80% of what you need — don't recreate it.
- **Do NOT apply Ctrl+A → Scale on only the Armature in Blender** — mesh and bone animation data remains in cm while the Armature node's local transform is reset → 100m character. Always handle scale in Unity import settings (`VisualScale`).

## 1. Concept and Kit Design

### Art Direction (Pixel8r2)
- Pixel art 3D (not blocky Roblox)
- 3-tone cell shading, 1px outlines, no gradients, no dithering
- ~4000 triangles, Mixamo rig (mixamorig: prefix, 23 bones)
- Fire/effects = Unity VFX (`ProjectileVFXManager` / particle systems), never on model
- No floating parts, no weapons/props on the model — attach via bone child in Unity

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

## 2. Model Import (Unity)

- **GLB/FBX via the Unity importer** — no `.import` sidecars; the importer handles the conversion. Static mesh FBX with `importAnimation = OFF`; per-animation FBX files with `importAnimation = ON`.
- **`VisualScale = 1.0f`** on `CharacterDefinition` (NOT the old 0.022f-era value). Scale lives in Unity import settings + `VisualScale`, never in Blender node transforms.
- **Clip renaming after import** — imported clips are renamed from `mixamo.com` to the animation name (`run.fbx` → `run`, `spell_lmb_1`, `spell_q`, ...). Batch via an Editor script that updates `ModelImporter.clipAnimations[0].name`.
- **Feet alignment** — tune `ModelYOffset` / `AutoModelYOffset` so the mesh sole touches the ground: `ModelYOffset` is visual-only (never in server hurtbox formulas); the server uses `py - capsuleHalf + by`, universal.
- **Bone naming** — Mixamo standard, 23 bones, `mixamorig:` prefix, colon separator. Never change after export. No finger/twist bones.
- **Size rule** — if the new character's mesh height differs by >1.5× from Manki (~1.516m), adjust the import scale. Update `HurtboxBoneScale` to match the baked export scale.

## 3. Clip Config

- **`CharacterAnimationConfig` ScriptableObject per class** (`client/Unity/Assets/Scripts/Runtime/Animation/CharacterAnimationConfig.cs`): standard clip fields (idle/run/jump_up/jump_down/fall/dash/hit_small/hit_medium/hit_hard/death) + an `AbilityClips` name→clip list.
- **Auto-loaded** in `PlayerRenderer.LoadModel()` from `Resources/AnimationConfigs/{Class}_AnimConfig` (with a fallback to the model class path).
- **`AnimationNames[]` on each `AbilitySpec` drives the lookup** — keys must match the config's `AbilityClips` entries exactly. Wrong name = T-pose with zero errors.
- **`PopulateAbilityClips` editor window** (`Assets/Scripts/Editor/PopulateAbilityClips.cs`) fills the config from the character's animation files.
- **Playback** — `PlayerRenderer.UpdateAnimationState()` detects `(AttackSlot, ComboStage)` changes and calls `_animancer.Play(clip, fade)`; attack clip speed = `frameCount / DurationTicks` (`GetAnimSpeedFromDuration`), so the animation ends exactly when the server advances the stage.
- **Extrapolation** — `ClipExtrapolator` (`Runtime/Animation/`) continues bone motion past clip end for hover/drift/aura (position curves from baked data; rotation holds the last keyframe). Set per clip on the config entry.
- No animator controller states to build — Animancer drives the Animator directly from server state. See `docs/systems/animation-system.md`.

## 4. Hurtbox Bake

- **`SlopArenaBaker`** (`Assets/Scripts/Editor/SlopArenaBaker.cs`, menu `Tools/SlopArena/Bake Skeleton...`) bakes bone positions per animation frame into `data/<name>_skeleton.bin`.
- **Set `HurtboxBoneScale` to match the export scale** — it converts baked bone coords into sim meters. `VisualScale` and `HurtboxBoneScale` must agree (Manki: both 1.0).
- **Diagnose with `tools/read_skeleton_bin.py`** — check Hips Y ≈ 0 (within ±5) at idle frame 0 and that the lowest bone is negative (below root). All-positive Y = shifted coordinate space → rebake with the skeleton at origin.
- Hurtbox definitions: `HurtboxBoneDefs[]` (bone spheres, preferred) replace `HurtboxCapsules[]` (fixed local-space capsules) when loaded; `BakedDataPath` points at the `.bin`.

## 5. C# Wiring Checklist

1. **`src/Shared/Characters/<Name>Data.cs`** — new `static partial class CharacterRegistry` file with `Build<Name>()` returning the `CharacterDefinition` (copy the pattern of `MankiData.cs`): `Class`, `DisplayName`, `MovementStats`, capsule, `HipHeight`, `HurtboxBoneDefs`, `ModelResourcePath`, `VisualScale`, `HurtboxBoneScale`, `ModelYOffset`/`AutoModelYOffset`, `BakedDataPath`, and an `AbilitySpec` per slot with `AnimationNames[]` (matching imported clip names).
2. **`CharacterClass` enum** (`src/Shared/CharacterDefinition.cs`) — add the new value.
3. **`BuildRegistry()`** (same file) — add `Build<Name>()` at the matching enum index (index 0 is `default` for `None`).
4. **Ability classes** — simple kits are pure `AbilitySpec` data; unique mechanics get a `ServerAbility` subclass in `src/Shared/Abilities/` wired via `SpecialEffectKeys` and created by `AbilityFactory.CreateServer(def.Class, slot, airborne)` (`src/Shared/Abilities/AbilityFactory.cs`).
5. **Selection** — the class is picked through the `TrainingMatch` / `PvPMatch` player-setup path (`client/Unity/Assets/Scripts/Runtime/World/`); `PlayerRenderer` loads the prefab from `def.ModelResourcePath` and the anim config from `Resources/AnimationConfigs/`.

## 6. Tools

- `tools/inspect_glb.py` — list embedded animation names in a GLB (use before setting `AnimationNames[]`).
- `tools/strip_root_motion.py` — strip Hips root motion from Mixamo animations (character must stay in place).
- `tools/read_skeleton_bin.py` — dump baked skeleton frames for coordinate-space diagnostics.
- `scripts/build_character_sheets.py` — generate character reference sheets.

Blender root-motion stripping (manual alternative): delete `mixamorig:Hips` location keyframes per action in the Action Editor, then re-export. Mixamo always uses `rotation_quaternion`, never `rotation_euler`; animations may have inconsistent channel counts.
