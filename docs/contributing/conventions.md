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
3daistudio        → concept art + rough model (FBX with Mixamo rig)
  ↓
Blender           → cleanup, rigging, bone naming
  ↓
Mixamo            → download per-animation FBX files
  ↓
Unity             → import FBX, rename clips, generate Animator, create prefab
```

### Model export

- Generate in **T-pose** for clean auto-rigging
- **No floating parts** — they cause rigging issues
- **No weapons / props on the model** — attach via bone child in Unity
- **No fire / particle effects on geometry** — all VFX are Unity particle systems
- Export format: **FBX** with Mixamo rig (not GLB)
- Rig: **Mixamo standard** (23 bones, mixamorig: naming)
- Poly count: ~4000 tris

### Animations

- Download each motion as a separate FBX from Mixamo
- All FBX files share the same Mixamo skeleton
- Place in `Assets/Art/Characters/<name>/Animations/`
- Imported clips must be renamed from `mixamo.com` to the FBX filename
- Clip assignments are configured per character via `CharacterAnimationConfig` ScriptableObjects (see `docs/systems/animation-system.md`)

### Mixamo + Blender (rigging)

- Rename bones to match `mixamorig:` convention once, never change after
- No finger bones, no twist bones
- Export as **FBX** (not GLB) — Unity imports FBX natively
- Textures: separate material assign in Unity (not embedded)

### Blender (Animations)

- Import FBX with the Mixamo rig
- Block out key poses → let Cascadeor calculate physics
- Export each animation as a separate FBX file
- Name exports to match convention: `spell_lmb_1`, `spell_lmb_2`, `spell_q`, etc.
- All FBX files share the same skeleton
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
| `spell_q` | Round Bomb (projectile) | Q |
| `spell_e` | Dynamite Jump (ground launch) | E |
| `spell_r_start` | Bazooka rise | R (start) |
| `spell_r_loop` | Bazooka aim hover | R (loop) |
| `spell_r_end` | Bazooka fire | R (end) |
| `spell_f` | Overclock (ult) | F |

Each character defines their own ability animation names in `CharacterDefinition.cs` under `AnimationNames`.

---

## 5. File Structure


```
assets/
  characters/
    manki/
      manki.glb           → master file (model + all anims embedded), imported via the Unity FBX/GLB importer (no .import sidecar)
    weapons/
      sword_2handed_color.gltf
```

animations/
  run.fbx           → per-animation FBX with clips renamed to clip names in Unity
  idle.fbx
  jump.fbx
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

## 5b. Third-Party Assets

**Purchased asset packs are never committed directly.** The flow:

1. Asset source files go on `/mnt/storage` and are **symlinked** into the Unity project
2. Symlinked paths are in `.gitignore` — the repo doesn't contain the data
3. Auto-generated prefabs from import (`.prefab` + `.meta`) are also `.gitignore`d
4. Only project-customized assets (edited materials, configured prefabs) are committed

If you clone fresh, you need access to `/mnt/storage` or re-import from the original
`.unitypackage`. This keeps the repo under 50MB while the full project with assets is ~5GB.

### Cartoon FX movement feedback

`Cartoon FX Remaster Free` is an optional local Asset Store dependency. Its source files
and SlopArena's derived prefab variants are not committed because this repository is
public and the Standard Unity Asset Store EULA permits distribution only inside a built
product.

To enable movement feedback in a fresh checkout:

1. Acquire and import `Cartoon FX Remaster Free` through the Unity Asset Store.
2. Keep its source under an ignored third-party asset directory such as
   `Assets/~AnimationPacks/`.
3. Run **Tools > SlopArena > Setup Cartoon Movement VFX**.

The setup command disables the package's incompatible Unity 6 soft-particle path,
reimports its shaders for the active pipeline, and generates the ignored prefabs under
`Assets/Resources/VFX/SlopArena/`. Without the dependency, the project still compiles and
movement VFX loading is a no-op.

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
- Weapons → attach to a bone transform (child of the weapon bone) in Unity
- Fire / particles → ParticleSystem / VFX Graph
- Capes / cloth → SkinnedMeshRenderer + Cloth component
- Flames / glows → Shader Graph or ParticleSystem

The 3D model should be as clean and simple as possible.

---

## 8. Current Roster

| # | Name | Archetype | Status |
|---|------|-----------|--------|
| 1 | Manki | Agile rushdown / fire monkey | In-game prototype |

See `docs/characters/manki.md` for details.

---

## 9. Git & Commits

**One squash commit per branch.** Feature branches are squashed at finish time (see `.omp/skills/sloparena-finish-branch`); the message is written once, at the end, and must describe the whole branch.

### Format — Conventional Commits

```
<type>(<scope>): <imperative summary> (issue #N)
```

- **type**: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`
- **scope**: the subsystem touched — `match`, `client`, `server`, `shared`, `netcode`, `arena`, `docs`, `mcp`, …
- **summary**: imperative, lowercase, ≤ 72 chars. What the change does, not how.
- **issue ref**: append ` (issue #N)` when the work resolves a GitHub issue. The PR body must also carry `Closes #N` — GitHub auto-close keywords are parsed from the body/commit message, never the title (see `.omp/skills/sloparena-finish-branch`).

### Examples (from the repo log)

```
feat(match): roster-driven match start with character classes (issue #35)
fix(client): repair Unity-MCP stack
docs: sync skills and docs to Unity codebase
chore(mcp): add Unity-MCP extensions, update docs
```

### Rules

- One type + one scope per commit; pick the dominant one when mixed.
- Server-authoritative changes note the Shared files touched in the PR body (not the subject).
- Never commit generated artifacts (`artifacts/`, Unity Plugins DLLs — gitignored).

