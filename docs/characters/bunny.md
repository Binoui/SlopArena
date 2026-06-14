# Bunny — Kung-Fu Rabbit Assassin

> Status: Implemented (prototype — placeholder visuals)
> Replaces: (new character)
> Inspired by: Lee Sin (LoL) × rabbit — agile martial artist with a carrot-themed twist

## Concept

A white-furred rabbit in ninja-esque martial arts attire. A deadly bunny who fights with powerful kicks and a giant carrot as a weapon. What appears cute is actually a fierce close-quarters combatant — fast, technical, and hit-and-run.

The character's "silliness" comes from the **carrot** (weapon, projectile, slam) rather than being a joke character. Think **Overgrowth × Lee Sin** — serious martial arts with slight vegetable-themed absurdity.

## Archetype

**Agile melee assassin / kick-focused brawler** — rushdown with mobility tools.
- LMB = 3-hit kick combo (low → medium → high)
- RMB = carrot slam (ground AoE)
- Q = carrot boomerang (poke + mark setup for R)
- E = backflip kick (disengage / mobility)
- R = dragon kick (powerful finisher, boosted if target marked by Q)
- F = Jade Hare (ult — sustained AoE kick zone)

## Palette

| Element | Color |
|---------|-------|
| Fur | White / cream #FFF3E0 |
| Ninja outfit | Dark teal #00695C |
| Carrot | Orange #FF6D00 |
| Outfit accents | Gold #FFD54F |
| Eyes | Ruby red #B71C1C |

## Kit

| Slot | Name | Visual | Mechanic |
|------|------|--------|----------|
| **LMB** | Rabbit Combo | 3 kicks: low → medium → high kick | Melee rushdown chain, high kick launcher |
| **Air LMB** | Rising Bun | Upward kick | Air-to-air combo |
| **RMB** | Carrot Slam | Slam giant carrot on the ground | Ground pound AoE, zone control |
| **Air RMB** | Helicopter | Spinning wheel kick in air | Air combo extender, downward spike knockback |
| **Q** | Whirling Carrot | Throw spinning carrot like a boomerang | Projectile poke, marks target for R |
| **E** | Flip Kick | Backflip with extended kick | Backward mobility / disengage |
| **R** | Dragon's Kick | Powerful flying kick | Hard hit finisher; boosted (more damage + KB) if target has Q mark |
| **F** | Jade Hare (ult) | Sustained circular kick | Large AoE zone, multi-hit damage over time |

## Design Notes

- **RMB**: ground-pound with the carrot. Large AoE for controlling space. Bunny plants the carrot into the ground — shockwave effect.
- **Q**: carrot boomerang. Throws a spinning carrot in a line. If it hits an enemy, they get a visible "marked" status. Only the latest target is marked. The mark lasts until Bunny lands R on someone.
- **R**: Dragon's Kick. If the target has the Q mark, the kick consumes it for bonus damage (+50%), bonus knockback, and a flashier visual. Bunny dashes forward toward the target on cast.
- **E**: backflip kick. Moves Bunny backward. Useful for creating space after a combo. Can be used in air for horizontal evasion.
- **F**: Jade Hare — a brief windup (8 ticks), then Bunny enters a spinning kick stance for 60 ticks. Large AoE circle centered on Bunny. Each tick applies light damage + small knockback. Bunny can't move during the stance but can cancel into dodge.

## Animation Names

| Key | Animation | Maps to |
|-----|-----------|---------|
| `kick_low` | Low kick | LMB stage 1 |
| `kick_medium` | Medium kick | LMB stage 2 |
| `kick_high` | High kick | LMB stage 3, Air LMB |
| `punch_down` | Overhead carrot slam | RMB |
| `kick_wheel` | Spinning wheel kick | Air RMB |
| `throw` | Carrot throw | Q |
| `backflip` | Backflip kick | E |
| `kick_high_full` | Full flying kick | R, F |

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
| Capsule | 0.5 × 1.5m |
| Visual scale | 0.017 (≅1.7m tall) |
| HurtboxBoneScale | 0.017 |

## Hurtbox Bone Defs (7 bones)

| Bone | Radius |
|------|--------|
| Head | 0.35 |
| Spine2 | 0.40 |
| Hips | 0.40 |
| RightHand | 0.18 |
| LeftHand | 0.18 |
| RightFoot | 0.22 |
| LeftFoot | 0.22 |

## Files

- `assets/characters/bunny/bunny.glb` — model + all animations (Mixamo 23-bone rig)
- `assets/characters/bunny/bunny.tscn` — scene with AnimationTree state machine
- `data/bunny_skeleton.bin` — baked skeleton data (11 bones × 13 animations)
- `Shared/CharacterDefinition.cs` — `CharacterClass.Bunny` + `BuildBunny()`
- `Scripts/Characters/Bunny/BunnyAbilities.cs` — special effects (carrot slam, whirling carrot, flip kick, dragon kick, jade hare)
- `tools/headless_bake_bunny.gd` — bake script for generating skeleton data
- `tools/strip_root_motion.py` — Blender script to strip Hips root motion

## Baked Animations

13 animations from Mixamo GLB:

- `backflip`, `charged_punch`, `fall`, `idle`, `jump`, `kick_high`, `kick_high_full`, `kick_low`, `kick_medium`, `kick_wheel`, `punch_down`, `run`, `throw`

## Known Issues / TODOs

- [ ] Carrot weapon model not yet attached (BoneAttachment3D)
- [ ] Q mark consumed by R not yet implemented (status effect system)
- [ ] F ult movement lock + cancel not wired
- [ ] Some Mixamo attack animations could be replaced with custom ones
- [ ] Need a real character name (code-named "Bunny")
- [ ] Color palette and outfit concept art to be refined

See `docs/character-kit-design-principles.md` for design patterns and `docs/combat-systems.md` for universal combat mechanics.
