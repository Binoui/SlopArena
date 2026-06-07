# Manki — Pyromane Inventeur (Fire Monkey)

> Status: Design phase (kit redesign)
> Replaces: Narodin (removed)
> Inspired by: Ziggs (LoL) × rushdown brawler — pyromaniac mad inventor monkey

## Concept

Un singe macaque pyromane/inventeur fou. Burnt-orange fur, bright red face, goofy toothy grin. Toujours en train de bricoler des explosifs — bombes, dynamite, aérosols. Porte une salopette tachée de poudre noire et des gants de chantier troués. Un petit casque jaune de chantier (apparaît pendant le dive bomb).

## Archétype

**Brawler aérien / Bombardier acrobate** — hybride entre rushdown et artilleur explosif.
- Utilise le mouvement vertical comme ressource
- Poke avec bombes, engage avec la dynamite/dive bomb, finit en mêlée
- Gameplan: rushdown au sol → s'envole avec Q/E → bombarde du ciel → R dive bomb pour revenir → rushdown

## Palette

| Element | Color |
|---------|-------|
| Fur | Burnt orange #D84315 |
| Face/butt | Bright red #E53935 |
| Salopette | Jean bleu #1565C0 |
| Casque/gants | Jaune #FDD835 |
| Horns/claws | Soot black #1A1A1A |

## Kit

| Slot | Nom | Visuel | Mécanique |
|------|-----|--------|-----------|
| **LMB** | Combo singe | 3 coups : poing → coup de pied → uppercut de feu | Rushdown mêlée, launcher sur final hit |
| **Air LMB** | Coup simple | Coup de poing aérien | Air combo |
| **RMB** | Aérosol + Briquet | Secoue l'aérosol → flamme en cone | Heavy attack chargeable, zone denial |
| **Air RMB** | Coup de pied moyen | Simple kick aérien | Knockback moyen |
| **Q** | Bombe ronde | Lance une bombe ronde en cloche → explose à l'impact | Poke / zone, aimable air + sol |
| **E** | Dynamite Jump | Balance une dynamite → explosion → auto-propulsion | Rocket jump, mobilité verticale + reset air jump |
| **R** | Dive Bomb | Met un casque → pique tête première → explosion à l'impact | Retour au sol rapide + AoE |
| **F** | Big Boom (ult) | Grosse bombe / fût explosif géant | TBD — gros burst explosif |

## Design Notes

- **RMB** : inspiré d'un lance-flammes artisanal — l'aérosol + briquet. Pendant la charge, Manki secoue frénétiquement la bombe. Au relâchement, jet de flamme en cone.
- **E** : le rocket jump. Au sol = plante la dynamite, propulsé vers le haut. En l'air = lâche la dynamite sous lui, propulsé vers le haut. Petite hitbox d'explosion pour le visuel (~2-3 unités), dégâts faibles ennemis (~5), le vrai effet c'est la propulsion.
- **R** : Dive Bomb. Manki arrête son momentum aérien, sort un casque de chantier jaune, pique tête première vers le sol. Explosion AoE à l'impact. Se relève, le casque disparaît.
- **F** : pas encore défini en détail — grosse bombe, à affiner après implémentation du reste du kit.

## Animation Names (à créer)

| Key | Animation | Maps to |
|-----|-----------|---------|
| `attack_1` | Punch | LMB stage 1 |
| `attack_2` | Roundhouse / leg sweep | LMB stage 2 |
| `attack_3` | Uppercut feu | LMB stage 3 (launcher) |
| `attack_air_lmb` | Air punch | Air LMB |
| `attack_heavy_charge` | Secoue aérosol | RMB hold |
| `attack_heavy_release` | Jet de flamme cone | RMB release |
| `attack_air_rmb` | Air kick | Air RMB |
| `spell_q` | Lance bombe ronde | Q (throw animation) |
| `spell_e_ground` | Plante dynamite sol | E ground |
| `spell_e_air` | Lâche dynamite sous lui | E air |
| `spell_r` | Dive bomb casque | R |
| `spell_f` | Grosse bombe / fût | F (ult) |

## Files

- `assets/characters/manki/anim_monkey.glb` — model + all animations embedded
- `Scripts/Characters/Manki/MankiAbilities.cs` — special effects
- `Shared/CharacterDefinition.cs` — enum + registry entry

## Previous Design

Ce personnage était initialement conçu comme un **Fire Dancer / rushdown acrobat** (feu pur, tout en mêlée). Le kit a été redessiné en juin 2026 pour le repositionner en **pyromane/inventeur fou** avec des explosifs, tout en gardant une base rushdown. Voir l'historique git pour l'ancienne version du kit.

See `docs/character-kit-design-principles.md` for design patterns and `docs/combat-systems.md` for universal combat mechanics.
