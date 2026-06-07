# Manki — Mad Bomber Monkey

> Status: Implemented (prototype — animations placeholder)
> Replaces: Narodin (removed)
> Inspired by: Ziggs (LoL) × rushdown brawler — pyromaniac mad inventor monkey

## Concept

A pyromaniac/inventor macaque monkey. Always tinkering with explosives — bombs, dynamite, aerosols. Wears overalls stained with gunpowder and hole-ridden work gloves. A small yellow hard hat appears during the dive bomb.

## Archetype

**Aerial bombardier / Acrobatic bomber** — hybrid between rushdown and explosive artillery.
- Uses vertical movement as a resource
- Poke with bombs, engage with dynamite/dive bomb, finish in melee
- Gameplan: ground rushdown → launch with Q/E → bombard from above → R dive bomb to return → rushdown

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
| **RMB** | Aerosol + Lighter | Shake aerosol → cone flame | Charged heavy attack, zone denial |
| **Air RMB** | Medium kick | Simple air kick | Medium knockback |
| **Q** | Round Bomb | Lob round bomb in arc → explodes on impact | Poke / zone, aimable air + ground |
| **E** | Dynamite Jump | Throw dynamite → explosion → self-propulsion | Rocket jump, vertical mobility + air jump reset |
| **R** | Dive Bomb | Put on hard hat → dive head-first → AoE explosion on impact | Fast return to ground + AoE |
| **F** | Big Boom (ult) | Large bomb / explosive barrel | TBD — large explosive burst |

## Design Notes

- **RMB**: inspired by improvised flamethrower — aerosol + lighter. During charge, Manki frantically shakes the bomb. On release, cone-shaped flame jet.
- **E**: rocket jump. On ground = plant dynamite, propelled upward. In air = drop dynamite below, propelled upward. Small explosion hitbox for visuals (~2-3 units), low enemy damage (~5), main effect is propulsion.
- **R**: Dive Bomb. Manki stops air momentum, pulls out a yellow hard hat, dives head-first toward the ground. AoE explosion on impact. After landing, the helmet disappears.
- **F**: not fully defined yet — big bomb, to be refined after the rest of the kit is implemented.

## Animation Names (to create)

| Key | Animation | Maps to |
|-----|-----------|---------|
| `attack_1` | Punch | LMB stage 1 |
| `attack_2` | Roundhouse / leg sweep | LMB stage 2 |
| `attack_3` | Fire uppercut | LMB stage 3 (launcher) |
| `attack_air_lmb` | Air punch | Air LMB |
| `rmb_loop` | Shake aerosol | RMB hold |
| `attack_heavy_release` | Cone flame jet | RMB release |
| `attack_air_rmb` | Air kick | Air RMB |
| `spell_q` | Throw round bomb | Q (throw animation) |
| `spell_e_ground` | Plant dynamite on ground | E ground |
| `spell_e_air` | Drop dynamite below | E air |
| `spell_r` | Dive bomb helmet | R |
| `spell_f` | Big bomb / barrel | F (ult) |

## Files

- `assets/characters/manki/anim_monkey.glb` — model + all animations embedded
- `Scripts/Characters/Manki/MankiAbilities.cs` — special effects
- `Shared/CharacterDefinition.cs` — enum + registry entry

## Previous Design

This character was initially designed as a **Fire Dancer / rushdown acrobat** (pure fire, full melee). The kit was redesigned in June 2026 to reposition him as a **pyromaniac/mad inventor** with explosives, while keeping a rushdown base. See git history for the old kit version.

See `docs/character-kit-design-principles.md` for design patterns and `docs/combat-systems.md` for universal combat mechanics.
