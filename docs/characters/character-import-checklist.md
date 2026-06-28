# Character Import Checklist

> Step-by-step: from concept to playable character in Unity.
> Each step is captured in `docs/characters/<name>.md` once done.

---

## ☐ Phase 0 — Concept & Kit

- [ ] Define archetype (rushdown, control, assassin, tank)
- [ ] Design ability kit (LMB, RMB, Q, E, R, F) + Air LMB + Air RMB
- [ ] Define signature color palette (max 5 colors)
- [ ] Define silhouette hooks (headband, cape, weapon shape, etc.)
- [ ] Write concept art prompt for 3daistudio (front T-pose, Pixel8r2 style)
- [ ] Write to `docs/characters/<name>.md`

---

## ☐ Phase 1 — 3D Model

- [ ] Generate model (3daistudio, Mixamo, or custom Blender)
- [ ] Export static mesh FBX with Mixamo rig (23 bones, `mixamorig:` naming)
- [ ] Verify: T-pose, no floating parts, no weapons, no fire geometry
- [ ] Verify: ~4000 triangles
- [ ] Verify: textures embedded (or separate material assignment)

---

## ☐ Phase 2 — Animation FBX Files

Each ability/locomotion slot needs a separate FBX file with one animation clip.

### Mixamo (for base movement + basic attacks)
- [ ] Download `idle` → save as `Idle.fbx`
- [ ] Download `run` → save as `run.fbx`
- [ ] Download `jump` full → save as `jump.fbx`
- [ ] Download `fall` apex → save as `fall.fbx`
- [ ] Download `spell_lmb_1` → first hit of LMB combo
- [ ] Download `spell_lmb_2` → second hit
- [ ] Download `spell_lmb_3` → launcher / finisher
- [ ] Download `spell_rmb` → heavy attack
- [ ] Download `spell_air_rmb` → air heavy
- [ ] Download `spell_q` → Q ability
- [ ] Download `spell_e` → E ability
- [ ] Download `spell_f` → F ultimate

### Optional (replace Mixamo defaults)
- [ ] Custom `hit_small` / `hit_medium` / `hit_hard` FBX
- [ ] Custom `dash` FBX
- [ ] Custom `land` FBX

### Place FBX files
```
client/Unity/Assets/Art/Characters/<name>/bunny.fbx  (static mesh, importAnimation=0)
client/Unity/Assets/Art/Characters/<name>/Animations/Idle.fbx
client/Unity/Assets/Art/Characters/<name>/Animations/run.fbx
...
```

---

## ☐ Phase 3 — Unity Import & Clip Renaming

- [ ] Copy static mesh FBX to `Assets/Art/Characters/<name>/<name>.fbx`
  - importAnimation: OFF (0) — no animations in mesh
- [ ] Copy animation FBX files to `Assets/Art/Characters/<name>/Animations/`
  - importAnimation: ON (1)
- [ ] **Rename clips** from `mixamo.com` to filename:
  - Unity batch script updates `ModelImporter.clipAnimations[0].name`
  - Run: `Renamed clip in ... -> "spell_lmb_1"` etc.
- [ ] Verify: each FBX has correct clip name

---

## ☐ Phase 4 — Code Integration

### BunnyData.cs (`src/Shared/Characters/`)
- [ ] Create `Build<Name>()` method with:
  - [ ] `Class = CharacterClass.<Name>`
  - [ ] `DisplayName`
  - [ ] `MovementStats`
  - [ ] `CapsuleRadius`, `CapsuleHeight`
  - [ ] `HurtboxCapsules` (fallback) and `HurtboxBoneDefs` (preferred)
  - [ ] `ModelResourcePath = "Characters/<Name>"` (matches prefab location)
  - [ ] `VisualScale = 1.0f` (Unity import scale, NOT Godot-era 0.022f)
  - [ ] `HurtboxBoneScale` (matches baked data export scale)
  - [ ] `ModelYOffset` (tune to align feet with capsule)
  - [ ] `AutoModelYOffset = false`
  - [ ] `BakedDataPath = "res://data/<name>_skeleton.bin"`
  - [ ] `LMB` — animation names match FBX clip names
  - [ ] `AirLMB` — uses existing clip (e.g., `spell_lmb_3`)
  - [ ] `RMB` — `spell_rmb`
  - [ ] `AirRMB` — `spell_air_rmb`
  - [ ] `Q` — `spell_q`
  - [ ] `E` — `spell_e`
  - [ ] `R` / `F` — `spell_f` (may share clip)
  - [ ] `AnimationNames` per ability (matching FBX clip names)
- [ ] **If new CharacterClass enum value**: add to `CharacterClass` enum in `CharacterDefinition.cs`

### SlopArenaAnimatorGenerator.cs (`Assets/Scripts/Editor/`)
- [ ] Add folder name → CharacterClass mapping:
  ```csharp
  var charClass = name.ToLowerInvariant() switch
  {
      "manki" => CharacterClass.Manki,
      "<name>" => CharacterClass.<Name>,
      _ => CharacterClass.Manki,
  };
  ```

---

## ☐ Phase 5 — Generate Animator Controller

- [ ] Force Unity recompile (touch a non-symlinked .cs file)
- [ ] Right-click `Assets/Art/Characters/<name>/` → `Create SlopArena Animator`
- [ ] Verify: `Assets/Animations/Controllers/<name>_Animator.controller` created
- [ ] Verify: `Assets/Art/Characters/<name>/<name>_AnimConfig.asset` created
- [ ] Verify controller states:
  - Movement (BlendTree with idle/run), Jump, Fall, Land, Dash, Hitstun
  - All ability states matching animation names
- [ ] Verify config clip assignments:
  - Idle → clip from `Idle.fbx`
  - Run → clip from `run.fbx`
  - Attack1 → `spell_lmb_1`, Attack2 → `spell_lmb_2`, Attack3 → `spell_lmb_3`
  - HitSmall → `hit_light` (fallback), HitLarge → `hit_medium` (fallback)
  - SpellQ → `spell_q`, SpellE → `spell_e`, SpellF → `spell_f`

---

## ☐ Phase 6 — Create Prefab

- [ ] Drag static mesh FBX into scene
- [ ] Assign `<name>_Animator.controller` to Animator component
- [ ] Set `Apply Root Motion = false`
- [ ] Right-click → Prefab → Create Prefab Variant
- [ ] Save to `Assets/Resources/Characters/<Name>.prefab`
- [ ] Delete temporary scene instance
- [ ] Verify: `Resources.Load<GameObject>("Characters/<Name>")` succeeds

---

## ☐ Phase 7 — Scene Setup & Test

- [ ] Open `Arena_Offline` scene
- [ ] Select TrainingMatch → set `_playerClass` to new class
- [ ] Save scene
- [ ] Press **Play**:
  - [ ] Model appears at correct scale
  - [ ] Idle animation plays
  - [ ] LMB combo chains through all stages
  - [ ] RMB plays correct clip
  - [ ] Q/E/R/F fire correctly
  - [ ] Walk → run animation
  - [ ] Jump/Fall → correct clips
  - [ ] Abilities work simulation-side (damage dealt, HUD updates)

---

## ☐ Phase 8 — Tuning

- [ ] Adjust `ModelYOffset` if model floats/clips
- [ ] Tune movement stats (speed, jump, gravity)
- [ ] Tune ability damage / cooldown / knockback values
- [ ] Verify hurtbox alignment with debug visualization

---

## Conventions Reference

| Item | Convention |
|------|-----------|
| Bone names | `mixamorig:Hips` etc. (23 bones, colon separator) |
| Anim names | lowercase, underscore: `spell_lmb_1`, `spell_q`, `hit_small` |
| Model export | Static FBX (no animations in mesh) + separate animation FBX files |
| Poly count | ~4000 tris |
| Rig | Mixamo humanoid (23 bones) |
| VisualScale | 1.0f (Unity import scale) |
| HurtboxBoneScale | Matches baked data export scale (often 0.01 or 0.022) |
| ModelYOffset | Manual, per-character (-0.52 for Manki) |
| Colors | 5 max per character including skin |
| Style | Pixel8r2 — 3-tone cell shading, 1px outlines |
