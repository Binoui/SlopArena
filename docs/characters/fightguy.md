# FightGuy

FightGuy is the reference cooked character package.

## Local cooldown tuning — 2026-10-03

Ground and aliased air special cooldowns: A **120 ticks / 2 s**, E **240 / 4 s**,
R **240 / 4 s** (previously 360 / 6 s), F **720 / 12 s** (previously 1200 / 20 s).
Cyclone Kick and Fist of Fury return sooner without changing their hit reward
or commitment. Normals retain zero cooldown. These are playtest tuning values,
not a match-balance verdict; timers begin on ability completion/cancellation.
Current source compilation and Shared ground/air cooldown-expiry probes passed.
Persisted cooked packages and roster pins are unchanged.

## Local gamefeel tuning — 2026-10-02

FightGuy is the all-round reference for the differentiated roster. Source-only
Editor tuning sets Run to 12 m/s, air cap to 7 m/s, jump/short-hop impulses to
10.8/6.5 m/s, gravity to 28 m/s², and fall/fast-fall caps to 12/18 m/s.
Air acceleration remains 16 + 3.2 m/s².

The source-compiled Shared probe measures full hop **44 ticks / 1.993 m** and
short hop **25 ticks / 0.701 m**. These are flat-arena airborne intervals, not
input-to-landing totals or a human feel verdict.

Normals use approximately 1.15× their previous durations. Contact windows,
IASA and auto-cancel thresholds are retimed with the clips; landing lag,
geometry, damage and specials are unchanged. Grounded recovery has at least
four inactive ticks before another ability, or eight for ground.4.

| Normal | Duration | IASA | Hit trigger / active length |
|---|---:|---:|---|
| ground.1 | 29 | 16 | 7 / 6 |
| ground.2 | 29 | 26 | 6 / 6 |
| ground.3 | 33 | 28 | 8 / 7 |
| ground.4 | 69 | 64 | 12 / 8 |
| air.1 | 38 | 33 | 7 / 6; 18 / 6 |
| air.2 | 48 | 41 | 8 / 6; 14 / 23 |
| air.3 | 51 | 48 | 16 / 7 |
| air.4 | 62 | 57 | 23 / 8 |

All entries are authored 60-Hz ticks. Real Shared hitbox-lifetime and IASA-edge
checks passed. Cooked packages/roster pins remain unchanged; live Lab/Training
alignment verification is pending the retained Editor owner's window.


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

- A: Ki Shot — public `spawnProjectile` at tick 8, shared with Wibou's shuriken
- E: Rising Dragon
- R: Cyclone Kick
- F: Fist of Fury

Ground and air A (physical Q) start immediately and fire Ki Shot after 8 startup
ticks. There is no aim stance, crosshair, held-key delay, or release phase. Shared
aims from the launch position toward the current server-selected target's torso,
falling back to the nearest eligible opponent within the existing 20 m horizontal
targeting range. With no eligible target, the shot fires level along combat facing.
The projectile does not home after launch; its authored speed, gravity, damage and
cooldown are unchanged.

Ki Shot has no native capability or internal capability requirement. Its projectile
parameters and spawn tick are editable in Ability Lab's **Selected effect** fields.
The enclosing timeline still lasts 45 ticks; projectile launch does not end recovery.
Dragon Beam is retired: its capability, parameter codecs and runtime factory have
been removed. Ground/air F execute Fist of Fury; unsupported beam IDs fail admission.


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
seven inward-stunning punches followed by a right-foot knockback finisher.
Punches use horizontal, zero-growth pull so damage percent cannot eject the victim.
Their fixed Hitstun covers the remaining timeline through the kick's active window;
the stronger inward pull keeps late catches in reach despite outward defensive drift.
Only the tick-88 right-foot hit launches outward. Ground and air F share this timeline.
The cooked timeline and hitbox direction are authoritative.

## Ki Shot projectile visual

Ground and air A (physical Q) use the game-owned
`Assets/CharacterPackages/fightguy/Presentation/FightGuyKiShot.prefab`, copied and
fully unpacked from Cartoon FX Remaster's `CFXR3 Iceball A + Ice Trail`.
`Resources/VFXConfigs/ProjectileVisuals.asset` binds both variants at scale 0.5.
Vendor materials, textures, and the effect script remain shared dependencies;
the vendor prefab is unchanged. Automatic clearing and camera shake are disabled
on the owned copy: `ProjectileVFXManager` controls placement and despawn from
Shared hitboxes in Training and server snapshots in PvP. Gameplay remains unchanged.

`PlayerRenderer` starts that same visual at `bone.right-hand` during Aiming.
The manager follows the animated hand in world space, without inheriting rig
scale, and retains it through firing startup. When Shared/the server supplies
the projectile, ownership transfers to the projectile entry and it follows the
authoritative position instead of the hand. The authored projectile origin is
unchanged; this is a visual handoff, not a hand-driven gameplay spawn.
Cancel, interruption, stock changes, model replacement, and renderer disable
clear held visuals. Consumed aim records prevent duplicate visuals from late aim
states; a projectile snapshot can transfer the visual before the release-state
packet arrives.

## Ki Shot hit visual

`ground.A` authors `hitPresentationId: presentation.fightguy.ki-shot.hit`;
`air.A` inherits it through its existing alias. The source asset catalog binds
that ID to `Presentation/FightGuyKiShotHit.prefab`, an owned copy of
`CFXR3 Hit Ice A (Air)` at root scale 0.25 with camera shake disabled.

Shared emits a `HitContact` presentation only after accepting the hit, at the
resolved contact point. Training consumes it directly; PvP waits for the server
event and deduplicates repeated delivery. It replaces the default hit graphic
and the projectile's redundant contact burst, not damage, Hitstop, sounds or
controller feedback. Blocks use shield feedback, and expiry retains its existing
burst without emitting an Ice hit. Other attacks keep their default graphics.

The previously unbound `presentation.cyclone-kick.start` declaration and R
timeline event were removed with user approval. R's gameplay is unchanged.
