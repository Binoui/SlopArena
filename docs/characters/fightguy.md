# FightGuy

FightGuy is the reference cooked character package.

## Ownership

- Editable source: `client/Unity/Assets/CharacterPackages/FightGuy/character.json`
- Cooked runtime package: `content-cooked/fightguy/`
- Cooked roster admission: `content-cooked/roster/manifest.json`
- Generated client catalog: `Resources/Generated/CharacterPackages/FightGuy/`
- Rig: package-owned generated catalog binding
- Collision poses: cooked `poses.bin`

The source document is editor input. Runtime consumers do not load raw source JSON,
manual animation configs, C# character factories, or standalone FightGuy skeleton bins.

## Runtime path

`BuiltInContentResolver` loads the roster requirement and four-file package. A fresh
`MatchContentCatalog` admits the immutable definition, baked poses, package identity,
and hashes. Training, PvP, Ability Lab, and GameServer consume that catalog entry.
`PlayerRenderer` resolves the generated animation catalog and package rig, then plays
semantic clips directly through Animancer.

## Specials

FightGuy specials are cooked timeline capability bindings:

- A: Ki Shot
- E: Rising Dragon
- R: Cyclone Kick
- F: Dragon Beam

The cooked timeline and capability requirement are authoritative. Changes require a
new deterministic cook, manifest/hash verification, differential tests, and a new
match/catalog load.
