# Character Import Checklist

> Step-by-step: from concept to playable character in Godot.
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

## ☐ Phase 1 — 3D Model (3daistudio)

- [ ] Generate model from prompt
- [ ] Export as **GLB** with **Mixamo rig** (23 bones, `mixamorig:` naming)
- [ ] Verify: T-pose, no floating parts, no weapons, no fire geometry
- [ ] Verify: ~4000 triangles
- [ ] Verify: textures embedded in .glb (JPEG)

---

## ☐ Phase 2 — Cleanup (Blender)

- [ ] Open .glb in Blender
- [ ] Verify bone names match `mixamorig:` standard (23 bones)
- [ ] Fix bind pose if needed (hips at correct height)
- [ ] Remove any duplicate Root / extra bones
- [ ] Export clean .glb to `assets/characters/<name>/<name>.glb`
- [ ] Delete old `.import` file so Godot re-imports

---

## ☐ Phase 3 — Animations

### Mixamo (for base movement)
- [ ] Download `idle` → save in `animation_source/`
- [ ] Download `run` → save in `animation_source/`
- [ ] Download `jump` full → save in `animation_source/`
- [ ] Download `fall` (or reuse jump apex) → save in
- [ ] Download `hit_small` → save in
- [ ] Download `hit_large` → save in
- [ ] Download `death` → save in

### Blender (for custom ability animations)
- [ ] `attack_1` — first hit of LMB combo
- [ ] `attack_2` — second hit
- [ ] `attack_3` — launcher / finisher
- [ ] `rmb_loop` — RMB charge start
- [ ] `attack_heavy_release` — RMB release
- [ ] `attack_air_lmb` — air attack
- [ ] `attack_air_rmb` — air spike
- [ ] `spell_q` — Q ability animation
- [ ] `spell_e` — E ability animation
- [ ] `spell_r` — R ability animation
- [ ] `spell_f` — F ultimate animation

---

## ☐ Phase 4 — Compose Master .glb (Blender)

- [ ] Import clean character .glb (with rig)
- [ ] Import each animation .glb (they share the same rig)
- [ ] In **NLA Editor**, stack all animations with proper names
- [ ] Export master .glb with **all animations embedded**
- [ ] Animation names match conventions (lowercase, `attack_1`, `spell_q`, etc.)
- [ ] Export to `assets/characters/<name>/<name>.glb` (overwrite)

---

## ☐ Phase 5 — Godot Import

- [ ] Copy .glb to `assets/characters/<name>/`
- [ ] Delete old `.import` file → Godot regenerates on next launch
- [ ] Verify: model visible in viewport
- [ ] Verify: skeleton found with correct bone count (23)
- [ ] Verify: animations listed in AnimationPlayer (run, idle, etc.)
- [ ] Set **import scale** if model is too small/large
- [ ] Set **root type** (if different from default)

---

## ☐ Phase 6 — Code Integration

### CharacterDefinition.cs (`Shared/`)
- [ ] Add `CharacterClass.<Name>` to enum
- [ ] Add `Build<Name>()` method with:
  - [ ] `DisplayName`
  - [ ] `MovementStats`
  - [ ] `LMB` — light combo stages
  - [ ] `AirLMB` — air light attack
  - [ ] `RMB` — heavy attack (optional charge)
  - [ ] `AirRMB` — air heavy attack
  - [ ] `Q` — CC / engage ability
  - [ ] `E` — mobility / recovery ability
  - [ ] `R` — strong ability
  - [ ] `F` — ultimate
  - [ ] `AnimationNames` per ability (matching .glb anim names)
  - [ ] `SpecialEffectKeys` for visual effects
- [ ] Add `Build<Name>()` to `CharacterRegistry` array

### AbilityRegistry.cs (`Scripts/Characters/`)
- [ ] Create `Scripts/Characters/<Name>/<Name>Abilities.cs`
- [ ] Add special effect methods called by `SpecialEffectKeys`
- [ ] Register key bindings in `AbilityRegistry`

### PlayerController.cs
- [ ] Add model path, scale, position in `LoadPlayerModel()` switch
- [ ] Set `hasWeapon = true/false`

### ClassSelectUI.cs
- [ ] Add `CharacterClass.<Name>` button
- [ ] Write class description
- [ ] Write class stats (HP / Speed / Range / Difficulty)

### Main.cs
- [ ] Update NPC cycle comment if class count changed

---

## ☐ Phase 7 — Test In-Game

- [ ] Select character from class select screen
- [ ] Verify: correct model appears
- [ ] Verify: animations play on movement (run, idle, jump)
- [ ] Verify: LMB combo plays 3 stages
- [ ] Verify: RMB charges and releases
- [ ] Verify: Q, E, R, F abilities fire
- [ ] Verify: special effects appear (visuals from StatusSpells)
- [ ] Verify: hitboxes connect
- [ ] Verify: no errors in output log

---

## ☐ Phase 8 — Polish

- [ ] Fine-tune movement stats (speed, jump, gravity)
- [ ] Tune ability damage / cooldown / knockback values
- [ ] Adjust model scale and ground position
- [ ] Add VFX taunts / idle particles (Godot particles, not model geo)
- [ ] Optional: add weapon via BoneAttachment3D (Knight only)

---

## Conventions Reference

| Item | Convention |
|------|-----------|
| Bone names | `mixamorig:Hips` etc. (23 bones, colon separator) |
| Anim names | lowercase, underscore: `attack_1`, `spell_q`, `hit_small` |
| Model export | GLB with embedded textures + all anims |
| Poly count | ~4000 tris |
| Rig | Mixamo humanoid (23 bones) |
| Colors | 5 max per character including skin |
| Style | Pixel8r2 — 3-tone cell shading, 1px outlines |
| Weapons | Not in model — BoneAttachment3D in Godot |
| Fire/VFX | Godot particles, not in geometry |
