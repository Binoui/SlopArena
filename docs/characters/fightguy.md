---
id: "fightguy"
name: "FightGuy"
title: "Martial Arts Champion"
status: "Design target"
archetype: "Technical melee brawler. Uses precise martial-arts normals, ki pressure, recovery movement, and committed playmaking attacks."
source_image: "fightguy_action.png"
palette:
  skin: "#F5D0A9"
  gi: "#1A237E"
  belt: "#C62828"
  headband: "#C62828"
---

# FightGuy — Martial Arts Champion

> Design contract: eight normals (`1 / 2 / 3 / 4`, grounded and aerial) plus four specials (`A / E / R / F`). The mouse buttons are camera controls, not attacks.
> Normals are considered working for now. Current design work is limited to the four specials and their interactions.

> Implementation status is tracked separately; this document is the canonical kit design.

**Prefab:** `Resources/Characters/FightGuy.prefab`
**AnimConfig:** `fightguy_AnimConfig.asset`
**Skeleton:** `data/fightguy_skeleton.bin`
**Inspired by:** Ryu (Street Fighter) × Lee Sin (LoL) — traditional martial artist with ki-infused techniques

## Concept

A disciplined martial artist who channels inner ki for devastating attacks. FightGuy combines precise strikes with projectile pressure and explosive movement. His normal attacks must be useful without relying on abilities; his specials create the unusual situations that make him feel like a ki-trained martial artist.

FightGuy's theme is **martial arts mastery**: clean fundamentals first, then ki-infused techniques that bend the ordinary rules of combat.

## Input contract

- `1 / 2 / 3 / 4` are the four normal inputs. Each has one grounded and one aerial attack, for eight normals total.
- `A / E / R / F` are the four special inputs.
- `LMB` and `RMB` control the camera. They are not attack slots and must not appear in kit power budgets.
- Normal attacks are single moves, not automatic LMB chains and not charged RMB attacks.

## Kit

FightGuy is designed around eight normals and four specials. Every normal has a grounded and aerial variant. The four specials are available from the same input model across the roster, but each has a distinct job in this kit.

### Normals — `1 / 2 / 3 / 4`

| Input | Grounded move | Aerial move | Primary job | Design intent |
|---|---|---|---|---|
| **1** | Low Kick | Double Punch | fast poke / spacing reset | Quick low kick checks close approaches. The aerial version is a safe two-hit air poke. |
| **2** | Straight Punch | Floating Kick | anti-air / air control | Grounded punch checks the front at mid range. The aerial kick covers the body and rewards early contact. |
| **3** | Sweeping Kick | High Kick | launcher / vertical conversion | Grounded sweep lifts rather than sends far. The aerial kick controls space above FightGuy and sends upward. |
| **4** | Double Kick | Air Smash | punish / kill confirm | Grounded double kick is the slow, high-commitment punish normal. Air Smash is a late horizontal aerial kill read. |

Normals should remain understandable, repeatable and useful in neutral, combos, juggling, edgeguarding and kill confirms. They use ordinary hitbox, startup, recovery and knockback rules; they do not require a mark, resource or special state to function.

### Specials — `A / E / R / F`

| Input | Move | Primary job | Description | Cooldown direction |
|---|---|---|---|---|
| **A** | Ki Shot | signature pressure | A fast, camera-aimed ki projectile shared on the ground and in the air. It has no mark, charge state or required follow-up; its value is immediate ranged pressure and forcing movement. | Short |
| **E** | Rising Dragon | mobility / recovery | A rising punch that attacks on the ground and becomes FightGuy's recovery-capable burst in the air. It resets the recovery float window only when used as an aerial recovery action. | Short-to-medium; recovery availability is the constraint |
| **R** | Cyclone Kick | playmaking engage | A committed forward spinning kick that carries FightGuy through the opponent's space. A clean hit applies a short stun for a follow-up; damage and knockback stay moderate so it is not the kit's kill move. It must be punishable on a whiff. | Medium |
| **F** | Dragon Beam | power / situation change | Press starts a visible fixed startup; the beam fires automatically after the telegraph. The same large camera-aimed beam works on the ground and in the air. It changes the immediate neutral situation through range and threat rather than a generic buff; the beam must be dodgeable and punishable during its commitment. | Long, roughly 15–30 seconds as a starting range |

`A` establishes the ki identity through a clean projectile, not a secondary resource. `E` makes recovery part of that identity through a rising punch without being recovery-only. `R` creates the main neutral-to-combo conversion. `F` is the largest expression of FightGuy's martial-arts control, not a generic stat buff.

The special concepts are now fixed: pure Ki Shot, implemented Rising Dragon rising punch, Cyclone Kick as the playmaking engage, and Dragon Beam as the power special. Remaining work is behavior and number design, not slot reassignment.

## Intended gameplan

1. Use `1` and `2` to check approaches and maintain ordinary neutral.
2. Use `3` to lift opponents and begin aerial pressure when the read is correct.
3. Use `4` as a committed punish or kill confirm, not as a default neutral button.
4. Use `A` to add camera-aimed ki pressure and establish the character's signature threat.
5. Use `E` to recover, reposition, or anti-air without making every recovery move identical to a vertical Up-B.
6. Use `R` to force a high-value engage when the opponent commits or a ki opening appears.
7. Use `F` to threaten a major camera-aimed beam, accepting its telegraph, long cooldown and punish window.
 
## Strengths

- Strong fundamentals: all eight normals have distinct neutral, anti-air, launcher, punish and aerial roles.
- Reliable close-range pressure without requiring a special resource.
- Ki Shot gives FightGuy a clear signature tool and limited ranged influence.
- Rising Dragon gives him an identity-defining recovery option with offensive use.
- Cyclone Kick and Dragon Beam can convert correct reads into large positional advantages.

## Weaknesses

- The normal kit is commitment-based; the strongest attacks are punishable on whiff.
- Ki Shot is the only dedicated ranged pressure tool, so FightGuy should not win a prolonged projectile war.
- Rising Dragon availability is a recovery risk when used carelessly or spent offensively.
- Cyclone Kick and Dragon Beam are readable commitments rather than safe panic buttons.
- No generic defensive mechanic is required; survival depends on movement, timing, normals and the universal Burst/Dodge systems.

## Design constraints

- The eight normals must be viable without `A / E / R / F`.
- `A` uses one shared ground/air Ki Shot variant; it is pressure, not a recovery system.
- `F` uses one shared ground/air Dragon Beam variant; it is not a second recovery system.
- `R` and `F` must create situations that normals cannot easily create, with visible commitment and whiff punishment.
- `F` must express FightGuy's martial-arts fantasy through a concrete beam threat, not only larger numbers or a generic buff.
- Grounded and aerial variants are separate moves with separate authored timing, geometry and knockback, even when they share a theme.

## Open tuning questions

- Dragon Beam's fixed startup, beam width, range, damage band, knockback and punish window.
- Cyclone Kick's carry distance, short-stun duration, damage/knockback band and punish window.
- Air Smash's landing lag and whether High Kick or Floating Kick should be the primary aerial conversion starter.

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

## Asset and implementation references

| File | Purpose |
|---|---|
| `src/Shared/Characters/FightGuyData.cs` | Character definition and eventual ability data |
| `client/Unity/Assets/Art/Characters/fightguy/fightguy.fbx` | Static mesh |
| `client/Unity/Assets/Art/Characters/fightguy/Animations/*.fbx` | Animation source files |
| `data/fightguy_skeleton.bin` | Baked skeleton data for hurtbox positions |
| `client/Unity/Assets/Resources/Characters/FightGuy.prefab` | Unity prefab |
| `client/Unity/Assets/Art/Characters/fightguy/fightguy_AnimConfig.asset` | Animation configuration |
