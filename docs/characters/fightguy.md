# FightGuy

FightGuy is the reference cooked character package.

## Ownership

- Editable source: `client/Unity/Assets/CharacterPackages/fightguy/character.json`
- Cooked runtime package: `content-cooked/fightguy/`
- Cooked roster admission: `content-cooked/roster/manifest.json`
- Generated client catalog: `Resources/Generated/CharacterPackages/fightguy/`
- Rig: package-owned generated catalog binding
- Collision poses: cooked `poses.bin`

The source document is editor input. Runtime consumers do not load raw source JSON,
manual animation configs, C# character factories, or standalone FightGuy skeleton bins.


## Deterministic baseline

The package root is normalized to the lowercase package ID `fightguy`. The cook
dependency hash includes project-relative source and catalog identities, so this
path migration regenerated the manifest, client binding, and roster hashes. The
authored JSON, asset catalog, runtime definition, and pose payload remain unchanged.

## Runtime path

`BuiltInContentResolver` loads the roster requirement and four-file package. A fresh
`MatchContentCatalog` admits the immutable definition, baked poses, package identity,
and hashes. Training, PvP, Ability Lab, and GameServer consume that catalog entry.
`PlayerRenderer` resolves the generated animation catalog and package rig, then plays
semantic clips directly through Animancer.

## Specials

FightGuy specials are cooked timeline and capability bindings:

- A: Ki Shot
- E: Rising Dragon
- R: Cyclone Kick
- F: Fist of Fury

Cyclone Kick's longer tuning uses 12 units/s for 60 ticks, then brakes horizontal
velocity once and releases velocity ownership during the remaining 12 recovery ticks.
The outer timeline ends at tick 72 (1.2 s); hitboxes spawn on ticks 7–48, with one
hit per opponent. Ground and air variants share this tuning. Airborne gravity resumes
when propulsion ends, and interruption preserves incoming knockback.
The capability duration bounds propulsion independently of the outer recovery timeline;
bot travel estimates use that bounded duration too. From rest on flat ground with no hits
or further input, the intended live Training trace is approximately 12 units of total
travel.
This is a local feel-test tuning; animation/gamefeel acceptance remains pending.

Fist of Fury uses `fightguy_spell_f_2` for its full two-phase presentation:
six inward-stunning punches followed by a right-foot knockback finisher.
The cooked timeline and hitbox direction are authoritative.
