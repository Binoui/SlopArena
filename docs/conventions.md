# SlopArena — Art & Design Conventions

> Guidelines for visual direction, asset pipeline, naming, and design philosophy.

---

## 1. Art Direction

### Style

**Pixel8r2 / Pixel Art 3D** — 3D models rendered with a pixel art aesthetic.

- **3-tone cell shading** (highlight / midtone / shadow) per color. No gradients, no dithering.
- **1px dark outlines** on characters. Sharp edges, clean silhouettes.
- **Limited color palette** per character. Warm high-contrast palettes.
- **Flat matte materials** — no PBR, no photorealism.

The look: a 1990s arcade cabinet fighter rendered in 3D pixel art.

### What this is NOT

- Not Roblox blocky
- Not low-poly cube minimalism (Megabonk)
- Not realistic / PBR
- Not cel-shaded cartoon (Guilty Gear)
- Not cute / chibi (KayKit)

### Palette philosophy

Each character has **one signature color** plus a neutral secondary. The palette fits on a sticky note:

| Character | Primary | Secondary | Accent |
|-----------|---------|-----------|--------|
| Manki     | Burnt orange #D84315 | Charcoal #333333 | Bright red #E53935 |

No more than 5 colors per character total (skin included).

---

## 2. Pipeline

```
3daistudio        → concept art + rough model
  ↓
Blender           → cleanup, rigging, bone naming
  ↓
Blender         → animation (one file per motion)
  ↓
Blender (NLA)     → compose all animations into one master .glb
  ↓
Godot             → import + wire up
```

### 3daistudio

- Generate in **T-pose** for clean auto-rigging
- **No floating parts** — they cause rigging issues
- **No weapons / props on the model** — attach via BoneAttachment in Godot
- **No fire / particle effects on geometry** — all VFX are Godot particles
- Export format: **GLB** (not FBX)
- Rig: **Mixamo standard** (23 bones, mixamorig: naming)
- Poly count: ~4000 tris

### Mixamo + Blender (rigging)

- Rename bones to match `mixamorig:` convention once, never change after
- No finger bones, no twist bones
- Export with **animations embedded** in the .glb when complete
- Textures: JPEG embedded in .glb (option 1)

### Blender (Animations)

- Import FBX/GLB with the Mixamo rig
- Block out key poses → let Cascadeor calculate physics
- Export each animation as a separate .glb
- Later stage: compose into one master file per character

---

## 3. Bone Naming Convention

Use **Mixamo standard** (`mixamorig:` prefix with colon):

```
mixamorig:Hips
mixamorig:Spine
mixamorig:Spine1
mixamorig:Spine2
mixamorig:Neck
mixamorig:Head
mixamorig:LeftShoulder
mixamorig:LeftArm
mixamorig:LeftForeArm
mixamorig:LeftHand
mixamorig:RightShoulder
mixamorig:RightArm
mixamorig:RightForeArm
mixamorig:RightHand
mixamorig:LeftUpLeg
mixamorig:LeftLeg
mixamorig:LeftFoot
mixamorig:LeftToeBase
mixamorig:RightUpLeg
mixamorig:RightLeg
mixamorig:RightFoot
mixamorig:RightToeBase
```

23 bones total. No more, no less. This keeps Mixamo AND Cascadeor compatible.

---

## 4. Animation Naming Convention

All lower case. Underscores for spaces.

### Movement (universal)
| Key | Animation | Notes |
|-----|-----------|-------|
| `idle` | Standing idle | Looping |
| `run` | Forward run | Looping |
| `walk` | Forward walk | Looping |
| `jump` | Full jump | Takeoff + apex + land |
| `jump_up` | Just takeoff | Clipped at apex |
| `fall` | Falling loop | Loop while falling |
| `land` | Landing impact | One-shot, blended |
| `dash` | Forward dash | One-shot |
| `dash_directional` | Side/back dash | Optional |

### Damage
| Key | Animation | Notes |
|-----|-----------|-------|
| `hit_small` | Flinch | One-shot |
| `hit_large` | Big knockback | One-shot |
| `knockdown` | Getting knocked down | One-shot |
| `getup` | Standing back up | One-shot |
| `death` | Death / KO | One-shot |

### Character-specific (Manki example)
| Key | Animation | Maps to ability |
|-----|-----------|-----------------|
| `attack_1` | Punch L | LMB stage 1 |
| `attack_2` | Punch R | LMB stage 2 |
| `attack_3` | Backflip kick | LMB stage 3 (launcher) |
| `rmb_loop` | Charged punch start | RMB hold |
| `attack_heavy_release` | Charged punch release | RMB release |
| `spell_q` | Fire Lash (ground kick) | Q |
| `spell_e` | Rising Flame (uppercut) | E |
| `spell_r` | Ember Burst (explosion) | R |
| `spell_f` | Inferno Dance (ult) | F |

Each character defines their own ability animation names in `CharacterDefinition.cs` under `AnimationNames`.

---

## 5. File Structure


```
assets/
  characters/
    manki/
      anim_monkey.glb           → master file (model + all anims embedded)
      anim_monkey.glb.import    → Godot import config
    weapons/
      sword_2handed_color.gltf
```

animations/
  run.res           → extracted per-animation .res files (Godot format)
  idle.res
  jump.res
  ...

animation_source/
  medium_run.glb    → raw source files, not used directly in game
  *.fbx
  mixamo_com.res

tools/
  patch_anim_res.py → fix bone paths in .res files
  rename_bones.py   → scripts for batch operations

docs/
  characters/
    manki.md      → kit, concept, notes per character
  character-kit-design-principles.md  → design rules
  combat-systems.md                    → universal combat mechanics
  conventions.md    → this file
```

---

## 6. Character Kit Design Rules

See `docs/character-kit-design-principles.md` for the full guide.

Key rules for SlopArena:
- **8 ability slots**: LMB, AirLMB, RMB, AirRMB, Q, E, R, F (ult on F, like DKO)
- **One ability, one job**: poke, move, CC, zone, counter, buff, burst
- **E = recovery** (vertical or horizontal)
- **Q = CC / engage**
- **F = ultimate**
- **No mana, only cooldowns**

---

## 7. Design Principles

### Visual readability over detail
A character must be identifiable by silhouette alone in 0.1s. The pixel art 3D style helps here — fewer pixels means clearer shapes.

### Animation tells the story
The fire dancer is fun because of how he moves, not what he wears. Exaggerated key poses, fast recovery, clear hit frames. The model is just a container.

### Fewer polygons, more personality
~4000 tris is enough. The character's identity comes from:
- **Signature color** (Narodin = orange)
- **Silhouette** (Narodin = wide pants + headband tails)
- **Movement style** (Narodin = acrobatic, floaty)

### Fix in engine, not in the asset
- Weapons → BoneAttachment3D in Godot
- Fire / particles → Godot VFX
- Capes / cloth → Godot cloth simulation
- Flames / glows → Godot shaders or particles

The 3D model should be as clean and simple as possible.

---

## 8. Current Roster

| # | Name | Archetype | Status |
|---|------|-----------|--------|
| 1 | Manki | Agile rushdown / fire monkey | In-game prototype |

See `docs/characters/manki.md` for details.
