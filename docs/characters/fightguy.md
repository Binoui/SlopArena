---
id: "fightguy"
name: "FightGuy"
title: "Martial Arts Champion"
status: "Alpha Core (Ready)"
archetype: "High-Agility Melee Brawler / Execution Specialist. Uses ki projectiles to apply marks, triggering high-damage pursuit finishers."
source_image: "fightguy_action.png"
palette:
  skin: "#F5D0A9"
  gi: "#1A237E"
  belt: "#C62828"
  headband: "#C62828"
---

# FightGuy — Martial Arts Champion

> Status: Implemented (Unity — initial)
> Prefab: `Resources/Characters/FightGuy.prefab`
> Animator: `fightguy_Animator.controller`
> AnimConfig: `fightguy_AnimConfig.asset`
> Skeleton: `data/fightguy_skeleton.bin`
> Inspired by: Ryu (Street Fighter) × Lee Sin (LoL) — traditional martial artist with ki-infused techniques

## Concept

A disciplined martial artist who channels inner ki for devastating attacks. Trained in a remote mountain temple, FightGuy combines precise strikes with projectile-based zone control. What appears as simple martial arts is infused with explosive ki energy — a technical fighter who excels at setting up targets with Ki Shot marks, then executing with Dragon's Kick.

FightGuy's theme is **martial arts mastery** — every ability channels ki through traditional fighting stances. Think **Ryu × Lee Sin** — serious martial arts with explosive ki effects.

## Abilities

| Slot | Name | Animations | Description | Notes |
|---|---|---|---|---|
| **LMB** | Dragon Combo | `spell_lmb_1/2/3` | Three-hit punch-kick chain | Stage 3: strong launcher |
| **AirLMB** | Rising Kick | `spell_lmb_3` | Airborne rising kick | Launches enemies upward |
| **RMB** | Heavy Strike | `spell_rmb` | Ki-infused ground slam | Shockwave AoE |
| **Q** | Ki Shot | `spell_q` | Aimed ki projectile | Hold to aim; marks targets for 5s on hit |
| **E** | Cyclone Kick | `spell_e` | Forward lunge ~10m. Dual hitboxes (body + foot). Stuns enemies passed through. | High stun, risk-reward lunge |
| **R** | Dragon's Kick | `spell_r_loop/attack/end` | Powerful flying kick | Hard hit finisher; boosted if target has Q mark (mark lasts ~5s) |
| **F** | Tempest (ult) | `spell_f` | Rapid spin in place, cyclone visual | Kicks pull nearby enemies toward center over 1.5s, then a final launcher kick. FightGuy cannot move during it. Big damage + knockback if enemy is caught |

## Stats

| Stat | Value |
|---|---|
| Walk Speed | 10 m/s |
| Sprint Speed | 14 m/s |
| Dash Speed | 32 m/s |
| Air Acceleration | 16 m/s² |
| Jump Force | 14 m/s |
| Gravity | 34 m/s² |
| Max Jumps | 2 |
| Dash Duration | 8 ticks (~130ms) |
| Dash Cooldown | 48 ticks (~800ms) |
| Visual Scale | 2.0 |
| Hurtbox Bone Scale | 2.0 |
| Capsule (Radius × Height) | 0.35 × 1.7 m |
| Hurtbox Radius | 1.0 m |

## Gameplay

### Strengths
- **Zone control** with Ki Shot (Q) — forces opponents to respect the projectile
- **High burst damage** from Dragon's Kick (R) executing marked targets
- **Strong engage** with Cyclone Kick (E) into follow-ups
- **Excellent air game** — Rising Kick (AirLMB) launches into combos

### Weaknesses
- **Slow projectiles** — Ki Shot has predictable arc, can be dodged
- **Commit-heavy** — Cyclone Kick and Dragon's Kick are all-in
- **No ranged poke** without Ki Shot mark setup
- **Tempest locks in place** — vulnerable if enemies aren't inside the pull

### Combos
1. Q (Ki Shot) → wait for mark → R (Dragon's Kick) → LMB chain — optimal execution punish
2. E (Cyclone Kick) → LMB chain → AirLMB (Rising Kick) — close-range burst
3. RMB (Heavy Strike) → follow-up pressure — ground control
4. F (Tempest) → hold enemies in AoE → all abilities off cooldown

## Unity Pipeline Notes

### FBX Processing
- Source model: Tripo-generated humanoid, exported as GLB, converted to FBX
- All 14 animation FBXs from Mixamo (retargeted to humanoid skeleton)
- Animator generated via `Assets/Art/Characters/fightguy/` → `Create SlopArena Animator`
- The generator uses a **two-pass save/reload** to avoid Unity Editor NRE on AnyState transitions
- `applyRootMotion = false` on the prefab Animator — server-authoritative position

### Key Files

| File | Purpose |
|---|---|
| `src/Shared/Characters/FightGuyData.cs` | Character definition (stats, abilities, animation names) |
| `client/Unity/Assets/Art/Characters/fightguy/fightguy.fbx` | Static mesh (no animation import) |
| `client/Unity/Assets/Art/Characters/fightguy/Animations/*.fbx` | 14 per-animation FBX files from Mixamo |
| `data/fightguy_skeleton.bin` | Baked skeleton data for hurtbox positions |
| `client/Unity/Assets/Resources/Characters/FightGuy.prefab` | Unity prefab with Animator + controller |
| `client/Unity/Assets/Art/Characters/fightguy/fightguy_AnimConfig.asset` | Animation configuration |
