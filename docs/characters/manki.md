# Manki — Fire Monkey

> Status: In-game prototype
> Replaces: Narodin (removed)
> Inspired by: Superbvonk art style, Fire Dancer / acrobat kit

## Concept

A fire monkey — mischievous macaque with burnt-orange fur, a bright red face, and a goofy toothy grin. Wears a ripped open charcoal vest and torn red shorts. Long thin tail. Short stubby charcoal horns. Scorch marks on belly and knees.

## Art Style

Superbvonk-style low-poly faceted. Flat bold colors, no textures, vertex-colored. 3-tone shading per color.

## Palette

| Element | Color |
|---------|-------|
| Fur | Burnt orange #D84315 |
| Face/butt | Bright red #E53935 |
| Vest | Charcoal #333333 |
| Shorts | Deep red #8B0000 |
| Skin | Warm tan #C68642 |
| Horns/claws | Soot black #1A1A1A |

## Kit

Fire monkey acrobat — agile rushdown with fire-infused attacks.
- **LMB**: 3-4 hit fire punch combo, launcher on final hit
- **AirLMB**: Rising uppercut, 1-2 hits, dash-cancelable
- **RMB**: Charged heavy punch (~15 dmg charged, ~10 uncharged)
- **AirRMB**: Downward fire slam spike
- **Q**: Fire Lash — ground kick, CC/engage
- **E**: Rising Flame — uppercut launcher, vertical recovery
- **R**: Ember Burst — explosion, zone denial
- **F**: Inferno Dance (ult) — big telegraphed finisher

See `docs/character-kit-design-principles.md` for design patterns and `docs/combat-systems.md` for universal combat mechanics.

## Files

- `assets/characters/manki/anim_monkey.glb` — model + all animations embedded
- `Scripts/Characters/Manki/MankiAbilities.cs` — special effects
- `Shared/CharacterDefinition.cs` — enum + registry entry
