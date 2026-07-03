---
id: "bunny"
name: "Bunny"
title: "The Kung-Fu Rabbit Assassin"
status: "Alpha Core (Ready)"
archetype: "High-Agility Melee Assassin / Dive Spec. Uses long-range carrot poke setups to apply marks, triggering high-damage execution finishers."
source_image: "bunny_action.png"
palette:
  fur: "#FFFFFF"
  gi: "#1A237E"
  belt: "#EEEEEE"
  headband: "#D50000"
  skin: "#FFAB91"
kit:
  - slot: "LMB"
    name: "Rabbit Combo"
    type: "melee"
    description: "3 hits: low -> medium -> high kick combo. High kick is a launcher."
  - slot: "Air LMB"
    name: "Rising Bun"
    type: "melee"
    description: "Upward kick. Fast, high-priority vertical air-to-air launcher."
  - slot: "RMB"
    name: "Carrot Slam"
    type: "aoe"
    description: "Slam giant carrot on ground. Ground-pound AoE for field control."
  - slot: "Air RMB"
    name: "Helicopter"
    type: "melee"
    description: "Spinning wheel kick in air. Extended air combo hit with downward spike knockback."
  - slot: "Q"
    name: "Whirling Carrot"
    type: "projectile"
    description: "Throw spinning carrot like a boomerang. Poke that marks target for R."
  - slot: "E"
    name: "Tornado Kick"
    type: "engage"
    description: "Powerful spinning kick. Forward engage with stun on hit."
  - slot: "R"
    name: "Dragon's Kick"
    type: "finisher"
    description: "Powerful flying kick. Hard hit finisher, deals massive damage if target is marked."
  - slot: "F"
    name: "Tempest of the Hare"
    type: "ultimate"
    description: "Cyclone spin pulling enemies in over 1.5s, finished by a devastating launcher kick."
---

# Bunny — Kung-Fu Rabbit Assassin

> Status: Implemented (Unity — initial)
> Prefab: `Resources/Characters/Bunny.prefab`
> Animator: `bunny_Animator.controller`
> AnimConfig: `bunny_AnimConfig.asset`
> Skeleton: `data/bunny_skeleton.bin`
> Inspired by: Lee Sin (LoL) × rabbit — agile martial artist with a carrot-themed twist

## Concept

A white-furred rabbit in ninja-esque martial arts attire. A deadly bunny who fights with powerful kicks and a giant carrot as a weapon. What appears cute is actually a fierce close-quarters combatant — fast, technical, and hit-and-run.

The character's "silliness" comes from the **carrot** (weapon, projectile, slam) rather than being a joke character. Think **Overgrowth × Lee Sin** — serious martial arts with slight vegetable-themed absurdity.

## Archetype

**Agile melee assassin / kick-focused brawler** — rushdown with mobility tools.
- LMB = 3-hit kick combo (low → medium → high)
- RMB = carrot slam (ground AoE)
- Q = carrot boomerang (poke + mark setup for R, mark lasts ~5s)

- **E**: tornado kick (risky forward engage, stuns for combo setup. Can jump/dash cancel mid-animation? Stun is ~0.5s.)
- F = Tempest of the Hare (ult — spin in place, cyclone pulls enemies toward center, final launcher kick)

## Kit

| Slot | Name | FBX clip | Visual | Mechanic |
|------|------|----------|--------|----------|
| **LMB** | Rabbit Combo | `spell_lmb_1/2/3` | 3 kicks: low → medium → high kick | Melee rushdown chain, high kick launcher |
| **Air LMB** | Rising Bun | `spell_lmb_3` | Upward kick | Air-to-air combo (shares LMB stage 3 clip) |
| **RMB** | Carrot Slam | `spell_rmb` | Slam giant carrot on the ground | Ground pound AoE, zone control |
| **Air RMB** | Helicopter | `spell_air_rmb` | Spinning wheel kick in air | Air combo extender, downward spike knockback |
| **Q** | Whirling Carrot | `spell_q` | Throw spinning carrot like a boomerang | Projectile poke, marks target for R |
| **E** | Tornado Kick | `spell_e` | Powerful spinning kick | Forward engage, high stun for combo setup |
| **R** | Dragon's Kick | `spell_r_loop/attack/end` | Powerful flying kick | Hard hit finisher; boosted if target has Q mark (mark lasts ~5s) |
| **F** | Tempest of the Hare (ult) | `spell_f` | Rapid spin in place, cyclone visual | Kicks pull nearby enemies toward center over 1.5s, then a final launcher kick. Bunny cannot move during it. Big damage + knockback if enemy is caught |

## Stats

| Stat | Value |
|------|-------|
| WalkSpeed | 10 |
| SprintSpeed | 14 |
| DashSpeed | 32 |
| JumpForce | 14 |
| Gravity | 34 |
| MaxJumps | 2 |
| DashCooldown | 48 ticks |
| Capsule | 0.6 × 1.5m |
| VisualScale | 1.0 (Unity import scale) |
| HurtboxBoneScale | 0.022 |
| ModelYOffset | -0.52 (manual, aligns feet with capsule) |

## Hurtbox Bone Defs (7 bones)

| Bone | Radius |
|------|--------|
| `mixamorig:Head` | 0.25 |
| `mixamorig:Spine2` | 0.30 |
| `mixamorig:Hips` | 0.30 |
| `mixamorig:RightHand` | 0.14 |
| `mixamorig:LeftHand` | 0.14 |
| `mixamorig:RightFoot` | 0.18 |
| `mixamorig:LeftFoot` | 0.18 |

## Animation Names (FBX clip → Unity AnimConfig slot)

| FBX file | Clip name | Config slot |
|----------|-----------|-------------|
| `Idle.fbx` | Idle | Idle |
| `run.fbx` | run | Run |
| `jump.fbx` | jump | Jump |
| `fall.fbx` | fall | Fall |
| `spell_lmb_1.fbx` | spell_lmb_1 | Attack1 |
| `spell_lmb_2.fbx` | spell_lmb_2 | Attack2 |
| `spell_lmb_3.fbx` | spell_lmb_3 | Attack3 |
| `spell_rmb.fbx` | spell_rmb | — (direct state) |
| `spell_air_rmb.fbx` | spell_air_rmb | — (direct state) |
| `spell_q.fbx` | spell_q | SpellQ |
| `spell_e.fbx` | spell_e | SpellE |
| `spell_f.fbx` | spell_f | SpellF |

> Note: Hit reaction clips (`hit_small`, `hit_medium`, `hit_hard`) and dash are missing — placeholder clips are generated by the Animator generator. Add FBX files later.

## Files

| Path | Purpose |
|------|---------|
| `src/Shared/Characters/BunnyData.cs` | Character definition (stats, abilities, animation names) |
| `client/Unity/Assets/Art/Characters/bunny/bunny.fbx` | Static mesh (no animation import) |
| `client/Unity/Assets/Art/Characters/bunny/Animations/*.fbx` | 14 per-animation FBX files from Mixamo |
| `data/bunny_skeleton.bin` | Baked skeleton data for hurtbox positions |
| `client/Unity/Assets/Animations/Controllers/bunny_Animator.controller` | Generated animator controller |
| `client/Unity/Assets/Art/Characters/bunny/bunny_AnimConfig.asset` | Generated animator configuration |
| `client/Unity/Assets/Resources/Characters/Bunny.prefab` | Unity prefab with Animator + controller |

## Unity Pipeline Notes

- Model FBX at `globalScale: 1` — same as Manki
- All 14 animation FBXs had clips renamed from `mixamo.com` to their filenames via `ModelImporter.clipAnimations`
- Animator generated via right-click `Assets/Art/Characters/bunny/` → `Create SlopArena Animator`
- The generator uses a **two-pass save/reload** to avoid Unity Editor NRE on AnyState transitions (pass 1: create states + regular transitions → save → reload → pass 2: add AnyState transitions)
- `applyRootMotion = false` on the prefab Animator — server-authoritative position

## Known Issues / TODOs

- [ ] Custom hit reaction animation FBX files (currently uses shared Manki `hit_light`/`hit_medium`)

- [x] E Tornado Kick: forward engage + high stun implemented
- [ ] F ult (Tempest of the Hare) needs its own animation clip — currently no dedicated clip (R now uses `spell_r`)
- [ ] Some Mixamo attack animations could be replaced with custom ones
- [ ] Carrot weapon model not attached as separate bone child
- [ ] Future: consider separating this kit from rabbit model — kit leans generic martial artist, rabbit theme could go to a different character
