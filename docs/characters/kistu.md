---
id: "kistu"
name: "Kistu"
title: "The Kitsune Blade"
status: "Implemented (sim) — art/anim pending"
archetype: "In-your-face spacing duelist. Wins neutral with disjointed blade reach, converts hits into vertical air juggles. Agile, aggressive mid-range control — not a full run-in rushdown."
source_image: "TBD"
inspiration: "Marth (Fire Emblem / Smash) — spacing, reach, punish game, counter, exploitable recovery. Amaterasu (DKO) — fast agile sword, launcher, air-juggle payoff."
palette:
  # Direction: mystic fox spirit. Refine during model/concept pass.
  fur: "TBD (white / silver, or classic fox orange)"
  accents: "TBD (foxfire — cyan/violet or crimson)"
  blade: "TBD"
kit:
  - slot: "LMB"
    name: "Light Slash Combo"
    type: "melee"
    description: "3-4 hit slash chain. Fast, low commit, modest knockback on finisher. Close-range bread-and-butter."
  - slot: "Air LMB"
    name: "Air Slash (3 hits)"
    type: "melee"
    description: "3-hit aerial slash. Low commit, predictable near-neutral/upward KB. Doubles as manual juggle-sustain and a fall-stall (minor recovery aid)."
  - slot: "RMB"
    name: "Charged Spin"
    type: "charge"
    description: "Tap = quick horizontal spacing poke. Hold = charged spinning vertical slice = the KILL move (big horizontal knockback toward blast zone at high %). Slow, telegraphed, high reward."
  - slot: "Air RMB"
    name: "Falling Slash"
    type: "melee"
    description: "Committed downward slash. Strong, predictable DOWNWARD knockback. Edgeguard / off-stage finisher — deliberately the opposite of the juggle."
  - slot: "Q"
    name: "Counter"
    type: "counter"
    description: "Marth-style parry window. If struck during the window, riposte LAUNCHES the attacker (knockback, no lingering stun -> can lead into a juggle). Read-based answer to being rushed inside the blade."
  - slot: "E"
    name: "Charged Dash Slash"
    type: "mobility"
    description: "Forward dash + slash. Tap = short reposition, hold = full gap-close. Distance scales with charge. Primary horizontal recovery. NO stun (differentiates from FightGuy Cyclone Kick)."
  - slot: "R"
    name: "Rising Slash"
    type: "mobility"
    description: "Multi-charge homing rising slash. THE SIGNATURE. Launches grounded enemies, re-launches airborne ones. Charge refunds on hit -> sustains the juggle as long as you keep connecting. Whiffing in empty air gives only capped height -> also serves as (honestly exploitable) vertical recovery-from-below."
  - slot: "F"
    name: "Blade Flurry"
    type: "ult"
    description: "Committed moving multi-slash flurry (forward/rising movement) that ends in a hard launch. Telegraphed startup, dodgeable, heavily punishable on whiff. Kept intentionally simple — a solid finisher, not a showstopper. Foxfire + tails visual."
---

# Kistu — The Kitsune Blade
> **Legacy implementation record.** The canonical kit contract is now eight normals on `1 / 2 / 3 / 4` (grounded and aerial) plus specials on `A / E / R / F`. `LMB` and `RMB` are camera controls, not attacks. This file records the current simulation implementation and is not a template for new kit design.
> Normals are considered working for now. Current design work is limited to replacing and refining the four specials under the `A / E / R / F` contract.



> Status: Implemented in Shared sim (all 8 slots + counter/charge-stock infra, 18 passing tests). Model/animation/VFX assets pending; plays now with a placeholder (FightGuy prefab, T-pose). See `docs/plans/2026-07-27-kistu-implementation.md`.
> Inspired by: **Marth** (spacing, reach, punish, counter, exploitable recovery) × **Amaterasu** (DKO — fast agile sword, launcher, air-juggle payoff).

## Concept

A **kitsune (fox spirit) swordswoman** — agile, precise, trickster-elegant. Where **FightGuy** is a close-range fists-and-ki execution brawler and **Manki** is an explosive zoner, Kistu owns the **mid-range sword game**: a disjointed blade used aggressively to control space and stay glued to the opponent at the edge of its reach, then convert a clean hit into a vertical air juggle.

Fox-spirit theme gives a distinct roster silhouette (monkey / human / fox) and natural VFX flair — **foxfire** trails on slashes, tails flourishing on spins and the counter riposte.

*Species/gender chosen (female kitsune), name locked (**Kistu**); exact palette and tail count still to refine — see Open Decisions.*

## Archetype

**In-your-face spacing duelist.**
- Neutral: out-space with **disjointed blade reach** and aggressive mid-range pressure. Uniform hit quality — **no positional sweetspot / tipper** (positional sweetspots are unreadable in 3D alongside warp + lunge).
- Payoff: convert a hit into **launch -> air juggle -> finish** (the "fun" of the kit).
- Mobility: agile, for **repositioning and spacing**, not stealth/escape. Not a full run-in rushdown — a pressure-at-range fighter.
- **Designed weakness:** weak once an opponent gets *inside* the blade (classic Marth flaw) and an **honestly exploitable recovery** (see below).

## Design Pillars

### Predictable knockback, emergent combos
Every ability has **consistent, learnable knockback** (angle + magnitude, scaling with %). Combos are **discovered by the player**, not scripted into the kit. Smash's philosophy, not DKO's fixed launch-chains. No ability is wired to "feed into" another — R reliably launches up, aerials have honest KB, and the juggle *emerges* because the values are readable.

> This pillar is broader than one character — it describes the game's intended combat feel. Consider promoting it to `docs/systems/combat-systems.md`.

### Recovery is a system, not one move
Recovery is spread across slots and is deliberately **functional-but-exploitable** (the character's signature weakness):
- **E** (charged dash) — horizontal distance, scales with charge.
- **Air LMB** (3-hit) — stall / slight drift, buys airtime.
- **R** (rising slash) — capped vertical height when whiffing in empty air (the flaw); only "extends" when it actually connects with an enemy.

## Kit

| Slot | Name | Role | Mechanic |
|------|------|------|----------|
| **LMB** | Light Slash Combo | Light attack | 3-4 hit slash chain, modest KB finisher |
| **Air LMB** | Air Slash (3 hits) | Aerial / juggle-sustain | 3-hit air slash, predictable upward KB, fall-stall |
| **RMB** | Charged Spin | Heavy / **kill move** | Tap = horizontal poke; Hold = charged spin = big horizontal launch (blast-zone kill) |
| **Air RMB** | Falling Slash | Aerial heavy / spike | Hold to charge: tap = quick slash (9 dmg); charged = heavier slash (13 dmg). Strong downward KB, edgeguard/finisher |
| **Q** | Counter | Counter / read | Parry window -> riposte **launches** attacker (knockback, no lingering stun) |
| **E** | Charged Dash Slash | Mobility / gap-close | Tap = short reposition, Hold = full gap-close; horizontal recovery; no stun |
| **R** | Rising Slash | **Signature** launcher + juggle + vertical recovery | Multi-charge homing uppercut; charge refunds on hit -> sustains juggle; capped self-height on whiff |
| **F** | Blade Flurry | Burst / finisher | Committed moving multi-slash flurry, ends in a hard launch; telegraphed, punishable on whiff |

## Gameplan

Control mid-range with LMB pokes and the disjointed blade -> land a hit or gap-close with E -> **R** to launch -> chase with double-jump/dash + Air LMB, re-launching with R (charges refund on hit) -> cash out with **RMB (charged)** for the kill at high %, or **Air RMB** to spike off-stage. **Q (Counter)** punishes opponents who try to rush inside your range.

## Weaknesses
- **Inside the blade:** loses to characters who get past the reach and pressure up close.
- **Recovery:** functional but exploitable — mostly horizontal (E) + capped vertical (R on whiff). Vulnerable to edgeguarding.
- **Commitment:** RMB charged spin and F are telegraphed; whiffing is heavily punishable.
- **Counter is a read:** whiffing Q leaves a vulnerable window.

## Open Decisions (implementation-time only)

Kit is fully specified. Remaining items are tuning/art, not design:

1. **Palette** — species/gender/name locked (female kitsune, **Kistu**). Open: exact palette (foxfire color, fur color), tail count.
2. **Numbers** — all damage / KB / charge counts / CDs / charge-refund timing TBD; tune in implementation against `character-kit-design-principles.md` baselines.
3. **Q = Counter** — locked as v1 but flagged "a bit lazy"; revisit after playtest depending on how oppressive rushdown feels in practice.

## Animation Needs (soft constraint)

Standard katana motions only — no exotic out-of-the-box motions. All of these are common in stylized katana packs:
- Ground light combo (multi-slash chain) — LMB
- 3-hit air slash — Air LMB
- Charged spin / spinning vertical slice — RMB (hold) + candidate for F
- Downward falling slash — Air RMB
- Counter guard + riposte — Q
- Forward dash slash / lunge — E
- Rising / uppercut slash — R (the one motion to specifically confirm the pack has)

## Files (to create when built)
- `src/Shared/Characters/KistuData.cs` — character definition (stats, abilities, animation names)
- `src/Shared/Abilities/KistuRisingSlash.cs` — R (multi-charge, charge-refund-on-hit)
- `src/Shared/Abilities/KistuCounter.cs` — Q
- Charged Spin (RMB) + Charged Dash (E) — likely reuse `ChargeAttackAbility.cs`
- `src/Shared/CharacterDefinition.cs` — enum + registry entry

See `docs/characters/character-kit-design-principles.md` for design patterns and `docs/systems/combat-systems.md` for universal combat mechanics.
