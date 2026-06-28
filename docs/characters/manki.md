# Manki — Mad Bomber Monkey

> Status: Implemented (prototype — animations placeholder)
> Replaces: Narodin (removed)
> Inspired by: Ziggs (LoL) × rushdown brawler — pyromaniac mad inventor monkey

## Concept

A pyromaniac/inventor macaque monkey. Always tinkering with explosives — bombs, dynamite, aerosols. Wears overalls stained with gunpowder and hole-ridden work gloves. A small yellow hard hat appears during the dive bomb.

## Archetype

**Projectile-heavy rushdown / Explosive bomber** — hybrid between explosive artillery and go-in-for-the-kill brawler.
- Poke with round bombs, zone with aerosol flame, go in with dynamite jump
- Vertical mobility is a resource — use E to launch, R to return
- Gameplan: poke with Q → go in with E → ground combo → R from air for burst
- E's knockback doubles as a defensive tool: detonate to break an engage
- R is a risky recovery option — high damage, easier to land from air, but opponent can dash-invuln through it
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
| **RMB** | Aerosol + Lighter | Shake aerosol → cone flame | Charged heavy attack, zone denial |
| **Air RMB** | Medium kick | Simple air kick | Medium knockback |
| **Q** | Round Bomb | Lob round bomb in arc → explodes on impact | Poke / zone, aimable air + ground |
| **E** | Dynamite Jump | Throw dynamite → explosion → self-propulsion | Rocket jump, vertical mobility + knockback on detonation (defensive + combo setup) |
| **R** | Dive Bomb | Put on hard hat, jetpack up → target zone → dive head-first → AoE explosion | Air-to-ground slam, big damage, risky recovery. Opponent can dash-invuln through it |
| **F** | Overclock | Mad scientist inject — eyes glow red, crackling energy | Self-buff 8s: all attacks (LMB, RMB, Q bombs) gain enhanced explosive effects — bigger AoE or bonus damage ticks |

## Design Notes

- **RMB**: inspired by improvised flamethrower — aerosol + lighter. During charge, Manki frantically shakes the bomb. On release, cone-shaped flame jet.
- **E**: rocket jump. On ground = plant dynamite, propelled upward. In air = drop dynamite below, propelled upward. Explosion has knockback — serves as defensive peel (detonate to break enemy engage) and combo setup. Hitbox needs to be bigger than visual-only.
- **R**: Dive Bomb. Manki stops air momentum, pulls out a yellow hard hat, dives head-first toward the ground. AoE explosion on impact. High damage but telegraphed — opponent can dodge with dash invincibility. Risky recovery option.
- **F**: Overclock. Manki injects himself with a mysterious substance (red can). Glowing red eyes, crackling energy. For 8 seconds, all his attacks gain bonus explosive effects — LMB hits have small explosions, RMB flame cone is bigger, Q bombs explode with larger radius or bonus damage. No single big hit, makes his whole kit scarier.

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
| `spell_f` | Overclock injection | F (ult) |

## Files

- `assets/characters/manki/manki.glb` — model + all animations embedded
- `Scripts/Characters/Manki/MankiAbilities.cs` — special effects
- `Shared/CharacterDefinition.cs` — enum + registry entry

## Previous Design

This character was initially designed as a **Fire Dancer / rushdown acrobat** (pure fire, full melee). The kit was redesigned in June 2026 to reposition him as a **pyromaniac/mad inventor** with explosives, while keeping a rushdown base. See git history for the old kit version.

See `docs/character-kit-design-principles.md` for design patterns and `docs/combat-systems.md` for universal combat mechanics.
