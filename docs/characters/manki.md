---
id: "manki"
name: "Manki"
title: "The Mad Bomber Monkey"
status: "Package-native cooked character"
archetype: "Hybrid Zoner / Recovery Skirmisher. Controls space with bounce bombs, launches from danger with Jetpack Boost, pokes with FPS bazooka, and commits with Aerosol Inferno."
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
    name: "Jetpack Boost"
    type: "recovery"
    description: "Compress for 3 ticks, then explode upward at 15 m/s with up to 3.5 m/s horizontal launch. Drift returns at the apex."
  - slot: "R"
    name: "Bazooka"
    type: "artillery"
    description: "Fire a rocket in camera direction. Short cast, then fire. Projectile arcs with gravity, explodes on contact. Self-rocket-jump via explosion (4 self-damage)."
  - slot: "F"
    name: "Aerosol Inferno"
    type: "area denial"
    description: "Commit to a forward aerosol inferno zone that catches opponents above and in front of Manki."
---

# Manki — Mad Bomber Monkey


> Status: Package-native authoring source cooked and rostered. The canonical kit contract is the package-native 16-entry grid: grounded and aerial variants of `1 / 2 / 3 / 4 / A / E / R / F`. `LMB` and `RMB` are camera controls, not persisted move identities.
> Package source: `client/Unity/Assets/CharacterPackages/manki/`; cooked runtime: `content-cooked/manki/`.
> Inspired by: Ziggs (LoL) × rushdown brawler — pyromaniac mad inventor monkey

## Concept

A pyromaniac/inventor macaque monkey. Always tinkering with explosives — bombs, dynamite, aerosols. Wears overalls stained with gunpowder and hole-ridden work gloves.

## Archetype

**Explosive all-rounder / Jetpack-bazooka hybrid** — mobile skirmisher with flexible approach options.
- Poke with round bombs, zone with aerosol flame, control space with Q pressure
- Recover vertically with Jetpack Boost (E), then regain air drift at the apex
- Gameplan: poke with Q → commit with ground combo → Jetpack Boost to recover → rocket jump for air follow-up
- E is a 3-tick vulnerable compression followed by one small ignition hitbox; horizontal input is sampled once at ignition
- R is fast fire-and-forget poke with rocket jump utility; aim at feet for vertical launch, aim at distant enemies for explosive poking
- F is a committed area-denial finisher with a tall forward hitbox

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
| **Air RMB** | Knuckle Spike | Double knuckle punch down (hold to charge) | Two-phase charge: tap = quick spike (10 dmg), hold >45 ticks = charged spike (14 dmg). Downward, high KB |
| **Q** | Round Bomb | Lob round bomb in arc → explodes on impact | Poke / zone, aimable air + ground |
| **E** | Jetpack Boost | Compression → ignition launch | 3 vulnerable startup ticks; 15 m/s vertical, normalized 3.5 m/s horizontal cap; 1.25m ignition sphere, 4 damage, 75° / 2+8 KB, 8 stun, 4 ticks; no ascent steering; air drift returns at apex |
| **R** | Bazooka | Short cast → fire rocket in camera direction | FPS-style fire-and-forget. Projectile arcs, explodes on impact. Rocket jump (4 self-dmg) |
| **F** | Aerosol Inferno | Aerosol can vents a tall orange flame column | Forward area denial; 15 damage, 55° launch, 30 ticks stun |

## Design Notes
- **Air LMB**: Air Kick — 2-hit combo via `AirLmbCombo` (generic `StageChainAbility` subclass, shared by all characters). First kick (16 ticks, 4 dmg, lunge) chains to second kick (18 ticks, 6 dmg, higher KB). Buffer input during stage 1 to chain.
- **Air RMB**: Knuckle Spike — hold-to-charge via `AirChargeAttack` (shared charge lifecycle, `ChargeHoldTicks=45`). Pressing mid-ascent stops the climb and the charge hovers in place (deliberately unlike air LMB, which keeps momentum). Tap (release before threshold) = the original spike (16 tick startup, 30 total, capsule straight down OffY=-0.5 to -1.5, 10 damage); charged = bigger knuckle (radius 1.0, 14 damage, 40t stun). Spike knockback (downward). Punish tool for reads.
- **E**: Jetpack Boost. On activation, Manki compresses for 3 vulnerable ticks while preserving vertical fall velocity and clearing horizontal velocity. On ignition, the authoritative simulation samples the current movement stick, normalizes it when above 0.001 magnitude, and launches at `VY=15` with horizontal speed `3.5` along that direction. A single owner-centered sphere (radius 1.25, damage 4, 75° angle, base 2, growth 8, stun 8, duration 4) resolves the ignition hit. The ascent is unsteerable; gravity is active immediately, and normal air drift/actions return when `VY <= 0`. Cooldown: 210 ticks. The move is a recovery move and aliases `air.E` to `ground.E`.
- **R**: Bazooka (FPS-style). Short cast (20 ticks), fire a rocket projectile in camera direction (AimYaw/AimPitch). Projectile has gravity (15 m/s²), speed 40 m/s, max flight 45 ticks. Explodes on entity hit or ground contact with 3m AoE. CanHitOwner=true on explosion — aim at feet for rocket jump (4 self-damage, upward knockback). No rise, no hover, no hold-to-aim. 240 tick cooldown (4s).
- **F**: Aerosol Inferno. A tall forward aerosol flame column starts after the commitment window. It is an area-denial finisher, not a self-buff.

## Animation Inventory

| Key | Animation | Maps to |
|-----|-----------|---------|
| `anim.manki.g1` | Grounded monkey punch | Ground `1` |
| `anim.manki.a1` | Air kick | Air `1` |
| `anim.manki.g2` | Straight punch | Ground `2` |
| `anim.manki.a2` | Air swing | Air `2` |
| `anim.manki.g3` | Sweeping kick | Ground `3` |
| `anim.manki.a3` | High kick | Air `3` |
| `anim.manki.g4` | Double kick | Ground `4` |
| `anim.manki.a4` | Air smash | Air `4` |
| `anim.manki.ga` | Round bomb | Ground `A` |
| `anim.manki.ge` | Jetpack Boost | Ground `E` and Air `E` alias |
| `anim.manki.gr` | Bazooka | Ground `R` |
| `anim.manki.gf` | Aerosol Inferno | Ground `F` and Air `F` alias |

## Files
- `client/Unity/Assets/CharacterPackages/manki/package.json` — package identity and license
- `client/Unity/Assets/CharacterPackages/manki/character.json` — canonical authoring source and 16-slot projection
- `client/Unity/Assets/CharacterPackages/manki/CharacterAssetCatalog.asset` — Unity presentation bindings
- `client/Unity/Assets/CharacterPackages/manki/Presentation/MankiAerosolInferno.prefab` — F presentation binding
- `content-cooked/manki/manifest.json` — cooked package manifest and hashes
- `content-cooked/manki/character.runtime.json` — normalized runtime definition
- `content-cooked/manki/poses.bin` — deterministic cooked pose data
- `content-cooked/manki/client.bindings` — generated client bindings

## Previous Design

This character was initially designed as a **Fire Dancer / rushdown acrobat** (pure fire, full melee). The kit was redesigned in June 2026 to reposition him as a **pyromaniac/mad inventor** with explosives, while keeping a rushdown base. See git history for the old kit version.

See `docs/characters/character-kit-design-principles.md` for the canonical kit contract and `docs/systems/combat-systems.md` for universal combat mechanics.
