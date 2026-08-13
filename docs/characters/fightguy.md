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

11-slot kit (issue #117 design, v2). Two tiers: the **universal normal schema** (LMB/RMB +
keys 1-4 — same roles for every character) and the **ability tier** (Q E R F — the
identity-defining moves). Ground/air: LMB/RMB have required air variants; keys 1-4 have
**distinct air variants** (melee frame-data pass, 2026-08-12 — frame data per
`docs/research/melee-frame-analysis.md`); ability slots work both unless noted.
Q = slot 11 (key "Q" position — the physical A key on AZERTY).

| Slot | Key | Move | Anim | Description | Notes |
|---|---|---|---|---|---|
| **LMB** | LMB | Dragon Jab | `spell_lmb_1` | Fast low-kick jab | 0 CD, neutral poke, lunge-forward |
| **AirLMB** | LMB (air) | Rising Kick | `spell_lmb_3` | Rising two-hit airborne uppercut | launcher into air combos |
| **RMB** | RMB | Uppercut | `spell_rmb` | Charged uppercut — hold to charge, release to strike | more charge = more damage/stun, launcher |
| **AirRMB** | RMB (air) | Helicopter | `spell_rmb_air` | Hold to charge aerial spinning heel drop; tap = quick spike, charged = heavy spike | spikes downward |
| **Slot1** | 1 | Low Kick | `spell_g_1` | Fast low right-foot kick — jab-class poke | startup 2, ~17 total, IASA 13 |
| **AirSlot1** | 1 (air) | Double Punch | `spell_a_1` | Left then right punch — fast air poke | two hitboxes (trig 4/13), ~33 total |
| **Slot2** | 2 | Roundhouse | `spell_g_2` | Roundhouse left kick — mid-range spacing normal | startup 8, ~29 total |
| **AirSlot2** | 2 (air) | Floating Kick | `spell_a_2` | Long static kick — nair-style lingering air hitbox | 12-tick active window, ~42 total |
| **Slot3** | 3 | Double Uppercut | `spell_g_3` | Low right-hand starter + rising right uppercut — anti-air launcher | hits at 12/20, ~39 total |
| **AirSlot3** | 3 (air) | High Kick | `spell_a_3` | High-reaching left-foot kick — aerial juggle launcher | startup 15, ~44 total |
| **Slot4** | 4 | Tornado Kick | `spell_g_4` | Spinning right-foot tornado — 360° get-off-me | startup 18, 10-tick ring, ~49 total |
| **AirSlot4** | 4 (air) | Air Tornado | `spell_a_4` | Same tornado kick as ground key 4 | identical frame data |
| **Q** | Q | Ki Shot | `spell_q` | Aimed ki projectile; marks target 5s on hit | on the Q key (slot 11); AZERTY physical-A |
| **E** | E | Rising Dragon | `spell_r_loop` | Upward mobility — rising kick: anti-air on ground, recovery burst in air (FloatWindow reset) | the up-B analog, `IsRecoveryMove`, ~4s CD |
| **R** | R | Cyclone Kick | `spell_e` | Forward spinning kick ~10m; stuns enemies passed through | engage, moved from E |
| **F** | F | Tempest | `spell_f` | Spin in place, pull enemies inward 1.5s, final launcher kick | ult, ground-only |

> Key 5: empty in demo. Dragon's Kick: cut (redundant with Cyclone — both forward kicks);
> Ki Shot marks remain as a setup hook. Status: slots LMB/F implemented; the rest designed in
> `docs/plans/issue-117-kit-expansion.md` — implementation in flight.

## Stats

| Stat | Value |
|---|---|
| Walk Speed | 10 m/s |
| Sprint Speed | 14 m/s |
| Dash Speed | 32 m/s |
| Air Acceleration | 16 m/s² |
| Jump Force | 12 m/s |
| Gravity | 36 m/s² |
| Max Jumps | 2 |
| Jump Squat | 4 ticks (~67ms) |
| Float Window / Fall Ramp | 35 / 10 ticks |
| Max Fall Speed | 48 m/s |
| Dash Duration / Cooldown | 18 / 48 ticks |
| Capsule (Radius × Height) | 0.35 × 1.7 m |
| Hurtbox Radius | 1.0 m |

## Gameplay

### Strengths
- **Complete normal tier** — jab (LMB), spacing (1), anti-air (2), punish (3), get-off-me (4)
- **Projectile pressure** — Ki Shot (Q) zones and marks; camera-aimed
- **Strong engage** — Cyclone Kick (R) stun-lunges into follow-ups
- **Excellent air game** — Rising Kick (AirLMB) launches, Helicopter (AirRMB) spikes
- **Upward mobility** — Rising Dragon (E) doubles as anti-air and the once-per-life recovery

### Weaknesses
- **Commit-heavy** — Cyclone (R) and Tempest (F) are all-in; whiffs are punished
- **No zoning outside Ki Shot** — the normal tier is melee
- **Recovery is one-shot** — Rising Dragon (E) has a long cooldown; off-stage mistakes cost the stock
- **No defensive/armor tier in demo** — Tornado Kick (4) is the only escape, and it's a normal, not a save

### Combos
1. Cyclone Kick (R) stun → jab (LMB) → Low Kick (1) — close-range burst
2. Double Uppercut (3) launch → Rising Kick (AirLMB) or High Kick (air 3) — air combo
3. Roundhouse (2) spacing → whiff punish on the recovery
4. Tornado Kick (4) / Air Tornado (air 4) — get-off-me reset when pressured
5. Ki Shot (Q) marks — setup hook for future execute synergy
6. Tempest (F) → hold enemies in AoE → all abilities off cooldown

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
