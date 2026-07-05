---
id: "manki"
name: "Manki"
title: "The Mad Bomber Monkey"
status: "Alpha Prototype (Ready)"
archetype: "Hybrid Zoner / Skirmisher. Controls space with bounce bombs, closes gaps with grapple, pokes with FPS bazooka, and commits with high-launch combos."
source_image: "manki_action.png"
palette:
  fur: "#D84315"
  face: "#E53935"
  overalls: "#1565C0"
  accents: "#FDD835"
  horns: "#1A1A1A"
kit:
  - slot: "LMB"
    name: "Monkey Combo"
    type: "melee"
    description: "3 hits: punch -> kick -> fire uppercut. Launcher on final hit."
  - slot: "Air LMB"
    name: "Air Kick"
    type: "melee"
    description: "2-hit air kick combo. First kick lunges toward target, second kick has higher knockback."
  - slot: "RMB"
    name: "Aerosol + Lighter"
    type: "charge"
    description: "Shake aerosol (hold) -> cone flame (release). tap = quick burst (8 dmg), hold >45 ticks = charged version (14 dmg)."
  - slot: "Air RMB"
    name: "Knuckle Spike"
    type: "melee"
    description: "Slow windup (16 tick startup) downward spike punch. High knockback, spikes opponents straight down."
  - slot: "Q"
    name: "Round Bomb"
    type: "projectile"
    description: "Lob round bomb in arc -> explodes on impact. Poke / zone, aimable air + ground."
  - slot: "E"
    name: "Grapple Gun"
    type: "mobility"
    description: "Fire a tether in aim direction. On enemy hit: reel toward them, 3 damage, no stun. On terrain hit: reel to impact point."
  - slot: "R"
    name: "Bazooka"
    type: "artillery"
    description: "Fire a rocket in camera direction. Short cast, then fire. Projectile arcs with gravity, explodes on contact. Self-rocket-jump via explosion (4 self-damage)."
  - slot: "F"
    name: "Overclock"
    type: "buff"
    description: "Mad scientist inject. Self-buff 8s: all attacks deal +3 bonus damage and +0.5m larger hitboxes (LMB, RMB, Q)."
---

# Manki — Mad Bomber Monkey

> Status: Implemented (prototype — animations placeholder)
> Replaces: Narodin (removed)
> Inspired by: Ziggs (LoL) × rushdown brawler — pyromaniac mad inventor monkey

## Concept

A pyromaniac/inventor macaque monkey. Always tinkering with explosives — bombs, dynamite, aerosols. Wears overalls stained with gunpowder and hole-ridden work gloves.

## Archetype

**Explosive all-rounder / Grapple-bazooka hybrid** — mobile skirmisher with flexible approach options.
- Poke with round bombs, zone with aerosol flame, control space with Q pressure
- Vertical mobility via rocket jump (R at feet), horizontal mobility via grapple (E to walls/enemies)
- Gameplan: poke with Q → close gap with grapple → ground combo → rocket jump for air follow-up
- E grapple is a gap closer AND escape tool (reel toward enemy for engage, reel to terrain for disengage)
- R is fast fire-and-forget poke with rocket jump utility; aim at feet for vertical launch, aim at distant enemies for explosive poking
- F is a "win neutral" steroid, not a single big hit

## Palette

| Element | Color |
|---------|-------|
| Fur | Burnt orange #D84315 |
| Face/butt | Bright red #E53935 |
| Overalls | Jean blue #1565C0 |
| Helmet/gloves | Yellow #FDD835 |
| Horns/claws | Soot black #1A1A1A |

## Kit

| Slot | Name | Visual | Mechanic |
|------|------|--------|----------|
| **LMB** | Monkey Combo | 3 hits: punch → kick → fire uppercut | Melee rushdown, launcher on final hit |
| **RMB** | Aerosol + Lighter | Shake aerosol (hold) → cone flame (release) | Two-phase charge: tap = quick burst (8 dmg), hold >45 ticks = charged (14 dmg) |
| **Air LMB** | Air Kick (2 hits) | Two air kicks | 2-hit combo, first kick lunges, second has higher KB |
| **Air RMB** | Knuckle Spike | Double knuckle punch down | Slow windup (16 tick startup), downward spike, high KB |
| **Q** | Round Bomb | Lob round bomb in arc → explodes on impact | Poke / zone, aimable air + ground |
| **E** | Grapple Gun | Fire tether in aim direction | On enemy hit: reel + 3 dmg no stun. On terrain: reel to point. Gap closer + escape |
| **R** | Bazooka | Short cast → fire rocket in camera direction | FPS-style fire-and-forget. Projectile arcs, explodes on impact. Rocket jump (4 self-dmg) |
| **F** | Overclock | Mad scientist inject — eyes glow red, crackling energy | Self-buff 8s: all attacks deal +3 bonus damage and +0.5m larger hitboxes (LMB, RMB, Q bombs) |

## Design Notes
- **Air LMB**: Air Kick — 2-hit combo via `AirLmbCombo` (generic `StageChainAbility` subclass, shared by all characters). First kick (16 ticks, 4 dmg, lunge) chains to second kick (18 ticks, 6 dmg, higher KB). Buffer input during stage 1 to chain.
- **Air RMB**: Knuckle Spike — slow windup (16 tick startup, 30 total). Capsule hitbox straight down (OffY=-0.5 to -1.5). 10 damage, high knockback, -12 upward KB (downward spike). Punish tool for reads.
- **E**: Grapple Gun. Fire a tether in AimYaw/AimPitch direction. On entity hit: anchor to target, 3 flat damage, no knockback/stun, reel toward them at reel speed. On terrain hit: anchor at impact point, reel there. Arrival within 0.5m threshold ends the ability. No hold-to-aim — fires immediately on press. Functions as gap closer (enemy hit) and escape tool (terrain hit).
- **R**: Bazooka (FPS-style). Short cast (20 ticks), fire a rocket projectile in camera direction (AimYaw/AimPitch). Projectile has gravity (15 m/s²), speed 40 m/s, max flight 45 ticks. Explodes on entity hit or ground contact with 3m AoE. CanHitOwner=true on explosion — aim at feet for rocket jump (4 self-damage, upward knockback). No rise, no hover, no hold-to-aim. 240 tick cooldown (4s).
- **F**: Overclock. Manki injects himself with a mysterious substance (red can). Glowing red eyes, crackling energy. For 8 seconds, all his attacks deal +3 bonus damage and have +0.5m larger hitboxes. No single big hit, makes his whole kit scarier.

## Animation Names (to create)

| Key | Animation | Maps to |
|-----|-----------|---------|
| `attack_1` | Punch | LMB stage 1 |
| `attack_2` | Roundhouse / leg sweep | LMB stage 2 |
| `attack_3` | Fire uppercut | LMB stage 3 (launcher) |
| `spell_lmb_air` | Air kick | Air LMB (both stages) |
| `spell_rmb_charged` | Shake aerosol | RMB charge hold (AnimIndex=0) |
| `spell_rmb_attack` | Cone flame jet | RMB release — both normal and charged (AnimIndex=1) |
| `spell_rmb_air` | Double knuckle spike | Air RMB |
| `spell_q_start` | Hold round bomb | Q aim (start/loop) |
| `spell_q_loop` | Hold round bomb | Q aim (loop) |
| `spell_q_end` | Throw round bomb | Q throw |
| `spell_e` | Arm thrust / fire | E Grapple Gun |
| `spell_r` | Bazooka cast | R Bazooka |
| `spell_f` | Overclock injection | F (ult) |

## Files
- `Shared/Abilities/MankiBazooka.cs` — FPS rocket launcher (R)
- `Shared/Abilities/MankiGrapple.cs` — Grapple Gun (E)
- `Shared/Abilities/MankiOverclock.cs` — Self-buff (F)
- `Shared/Abilities/AirLmbCombo.cs` — Generic air LMB combo (LmbCombo pattern, airborne-only stages)
- `Shared/CharacterDefinition.cs` — enum + registry entry

## Previous Design

This character was initially designed as a **Fire Dancer / rushdown acrobat** (pure fire, full melee). The kit was redesigned in June 2026 to reposition him as a **pyromaniac/mad inventor** with explosives, while keeping a rushdown base. See git history for the old kit version.

See `docs/character-kit-design-principles.md` for design patterns and `docs/combat-systems.md` for universal combat mechanics.
