---
id: "manki"
name: "Manki"
title: "The Mad Bomber Monkey"
status: "Alpha Prototype (Ready)"
archetype: "Hybrid Zoner / Melee Rushdown. Controls space with bounce bombs, sets defensive mines to cover landings, and commits with high-launch combos."
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
    name: "Air Punch"
    type: "melee"
    description: "Forward air strike with standard air-combo capability."
  - slot: "RMB"
    name: "Aerosol + Lighter"
    type: "charge"
    description: "Shake aerosol (hold) -> cone flame (release). tap = quick burst (8 dmg), hold >45 ticks = charged version (14 dmg)."
  - slot: "Air RMB"
    name: "Drop Kick"
    type: "melee"
    description: "Heavy air-to-ground kick. Downward spike knockback."
  - slot: "Q"
    name: "Round Bomb"
    type: "projectile"
    description: "Lob round bomb in arc -> explodes on impact. Poke / zone, aimable air + ground."
  - slot: "E"
    name: "Dynamite Jump"
    type: "mobility"
    description: "Rocket jump, vertical mobility + knockback on detonation (defensive + combo setup)."
  - slot: "R"
    name: "Bazooka"
    type: "ultimate"
    description: "Rise up 5m, aim a ground indicator downward, fire explosive shell."
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

**Projectile-heavy rushdown / Explosive bomber** — hybrid between explosive artillery and go-in-for-the-kill brawler.
- Poke with round bombs, zone with aerosol flame, go in with dynamite jump
- Vertical mobility is a resource — use E to launch, R to rain from above
- Gameplan: poke with Q → go in with E → ground combo → R from air for artillery burst
- E's knockback doubles as a defensive tool: detonate to break an engage
- R is a ranged artillery option from height advantage. Rise up, aim ground target, fire explosive shell. AoE for zoning, direct hits for burst damage.
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
| **Air LMB** | Simple punch | Air punch | Air combo |
| **RMB** | Aerosol + Lighter | Shake aerosol (hold) → cone flame (release) | Two-phase charge-hold attack: tap = quick burst (8 dmg), hold >45 ticks = charged version (14 dmg, longer range, wider hitbox) |
| **Air RMB** | Medium kick | Simple air kick | Medium knockback |
| **Q** | Round Bomb | Lob round bomb in arc → explodes on impact | Poke / zone, aimable air + ground |
| **E** | Dynamite Jump | Throw dynamite → explosion → self-propulsion | Rocket jump, vertical mobility + knockback on detonation (defensive + combo setup) |
| **R** | Bazooka | Rise up 5m, aim a ground indicator downward, fire a bazooka shell that explodes on ground impact | Ranged artillery from height advantage. Rise 5m from ground (less if already airborne). Fire a ballistic explosive shell toward aimed ground position. Shell explodes on entity hit or ground contact. |
| **F** | Overclock | Mad scientist inject — eyes glow red, crackling energy | Self-buff 8s: all attacks deal +3 bonus damage and +0.5m larger hitboxes (LMB, RMB, Q bombs) |

## Design Notes

- **RMB**: Two-phase hold-to-charge flamethrower. Phase 1: hold RMB → Manki shakes the bomb (charge pose, AnimIndex=0). Phase 2: release → cone-shaped flame jet (AnimIndex=1). Tap release = normal burst (8 dmg, 0.8m radius). Hold >=45 ticks = charged burst (14 dmg, 1.0m radius, 4m range). Auto-release at 120 ticks. NPC path bypasses charge phase (instant skip).
- **E**: rocket jump / area denial. On ground = plant dynamite mine (10s auto-detonate), can press E again to detonate early. Explosion launches self upward and knocks back enemies (CanHitOwner). Serves as defensive peel (detonate to break enemy engage), combo setup (bait opponent onto mine), and mobility (rocket jump). In air = drop dynamite below, propelled upward.
- **R**: Bazooka. Manki rises up ~5m (skips rise if already airborne), hovers to aim a ground indicator, then fires a ballistic explosive shell toward the aimed point. Shell explodes on entity contact or ground impact. AoE explosion for zoning. Projectile has slight gravity arc.
- **F**: Overclock. Manki injects himself with a mysterious substance (red can). Glowing red eyes, crackling energy. For 8 seconds, all his attacks deal +3 bonus damage and have +0.5m larger hitboxes. No single big hit, makes his whole kit scarier.

## Animation Names (to create)

| Key | Animation | Maps to |
|-----|-----------|---------|
| `attack_1` | Punch | LMB stage 1 |
| `attack_2` | Roundhouse / leg sweep | LMB stage 2 |
| `attack_3` | Fire uppercut | LMB stage 3 (launcher) |
| `attack_air_lmb` | Air punch | Air LMB |
| `spell_rmb_attack` | Shake aerosol | RMB charge hold (AnimIndex=0) |
| `spell_rmb_charged` | Cone flame jet | RMB release — both normal and charged (AnimIndex=1) |
| `attack_air_rmb` | Air kick | Air RMB |
| `spell_q` | Throw round bomb | Q (throw animation) |
| `spell_e_ground` | Plant dynamite on ground | E ground |
| `spell_e_air` | Drop dynamite below | E air |
| `spell_r_start` | Bazooka equip / rise up | R start (rise) |
| `spell_r_loop` | Bazooka hover aim | R aim (loop) |
| `spell_r_end` | Bazooka fire recoil | R fire (end) |
| `spell_f` | Overclock injection | F (ult) |

## Files

- `assets/characters/manki/manki.glb` — model + all animations embedded
- `Scripts/Characters/Manki/MankiAbilities.cs` — special effects
- `Shared/CharacterDefinition.cs` — enum + registry entry

## Previous Design

This character was initially designed as a **Fire Dancer / rushdown acrobat** (pure fire, full melee). The kit was redesigned in June 2026 to reposition him as a **pyromaniac/mad inventor** with explosives, while keeping a rushdown base. See git history for the old kit version.

See `docs/character-kit-design-principles.md` for design patterns and `docs/combat-systems.md` for universal combat mechanics.
