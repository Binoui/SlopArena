# Bonk

## Status

Bonk is a package-native greatsword kit admitted to the built-in roster.
It exercises the authoritative Shared compiler, trusted built-in capability
admission, cooked package loading, Character Select discovery, and match
content admission. Avatar visual/pose review remains a release prerequisite.

## Movement tuning — 2026-09-15

Bonk trades some ground speed for vertical reach: Run is reduced from 14 to
13 m/s, while `jumpForce` rises from 12 to 13 m/s. Full-jump height measures
2.24 m instead of 1.90 m through the authoritative flat-arena probe. The unchanged
0.8 air-jump multiplier also raises double-jump launch speed from 9.6 to 10.4 m/s.
Short hop, air control, dash, gravity, weight, and ability data are unchanged.

## Package ownership

The package is `client/Unity/Assets/CharacterPackages/bonk/`:

- `package.json` — package identity and attribution (`bonk`, `0.0.0-dev`, Binoui, MIT,
  SlopArena).
  `character.json` — authoritative gameplay source with the canonical sixteen slots.
  Seven normals use sword capsules from `_weapon_hilt` to `_weapon_tip`; E is the
  targeted recovery slam; F is the repeated blade storm. Unlisted slots remain no-op
  probes.
- `CharacterAssetCatalog.asset` — schema 1, 60 Hz, Bonk rig, presentation bindings,
  independent move-animation bindings, and the package-owned `BonkWeaponAttachConfig.asset`.

Bonk is admitted by `content-cooked/roster/manifest.json` as selector `Bonk`,
package `bonk`, version `0.0.0-dev`, with the exact cooked-content and package
hashes from its cooked manifest. Character Select discovers it from that roster.

## WIP gameplay kit

All timings are 60 Hz ticks. Each named normal is an interruptible,
independent `hitGroup: 0` capsule from `_weapon_hilt` to `_weapon_tip`.

| Slot | Role | Duration | IASA | Trigger / active | Radius | Damage / angle | Base / growth | Stun |
| --- | --- | ---: | ---: | --- | ---: | --- | --- | ---: |
| `ground.1` | Reverse Slash — slower forward neutral check | 35 | 30 | 13 / 11 | 0.28 | 6 / 30° | 4 / 20 | 12 |
| `ground.2` | Greatsword Swing — committed forward spacing | 42 | 37 | 16 / 12 | 0.33 | 10 / 35° | 7 / 30 | 16 |
| `ground.3` | Reverse Rising Slash — faster vertical anti-air | 38 | 33 | 13 / 12 | 0.30 | 9 / 78° | 6 / 26 | 16 |
| `ground.4` | Heavy Double Slice — grounded kill read | 58 | 52 | 20 / 13 | 0.38 | 15 / 25° | 10 / 42 | 22 |
| `air.1` | Reverse Air Slash — aerial spacing check | 46 | 41 | 18 / 16 | 0.28 | 8 / 35° | 5 / 24 | 14 |
| `air.3` | Downward Sweep — committed spike | 54 | 48 | 23 / 17 | 0.32 | 11 / -45° | 7 / 30 | 20 |
| `air.4` | Acrobatic Double Slice — aerial kill read | 66 | 59 | 28 / 18 | 0.38 | 14 / 25° | 9 / 40 | 22 |
Grounded landing/auto-cancel windows remain zero. Aerial landing lag and
auto-cancel before/after are now `air.1: 20 / 15 / 34`, `air.3: 22 / 16 / 40`,
and `air.4: 24 / 16 / 49`.

`ground.E` and `air.E` are the same `Targeted Jump Slam` contract:
`groundCursor` hold/release aim, a 240-tick cooldown, recovery semantics, and a
180-tick timeline. The trusted Shared capability clamps the horizontal target
to 1–12 m, launches at vertical speed 16, and spawns one 0.42 m capsule on
authoritative landing for 13 damage at 55°, base/growth 9/32, 20 stun ticks,
and a 6-tick hitbox. Aim yaw and distance are cached while held, so release
input cannot replace the selected direction.

`ground.F` is `Blade Storm`: a 56-tick timeline with independent hilt-to-tip
capsules at ticks 8, 16, 24, and 32 (radius 0.32, damage 2.5, angle 25°,
base/growth 2/8, 8 stun ticks, 4 active ticks), followed by a tick-44
finisher (radius 0.42, damage 12, angle 25°, base/growth 10/36, 20 stun
ticks, 6 active ticks). It has a 900-tick cooldown. `air.F` and the other
unlisted canonical slots remain no-op probes.

The capability requirement is
`slop.internal.bonk.targeted-jump-slam.v1` version `1`. It is admitted only
for the trusted built-in Bonk profile; Workshop content cannot use it.

Assets remain in the existing shared Bonk art tree, matching the FightGuy/Kistu convention,
with their Unity `.meta` files preserved:

- `Assets/Art/Characters/bonk/bonk.FBX`.
- `Assets/Art/Characters/bonk/Animations/idle.FBX`, `run.FBX`, `jump_start.FBX`,
  `jump_loop.FBX`, and `jump_end.FBX`.
- `Assets/Art/Characters/bonk/Animations/bonk_g_1.FBX` through `bonk_g_4.FBX`.
- `Assets/Art/Characters/bonk/Animations/bonk_a_1.FBX`, `bonk_a_3.FBX`, and `bonk_a_4.FBX`.
- `Assets/Art/Characters/bonk/Animations/bonk_spell_e.FBX` and `bonk_spell_f.FBX`.

The Bonk sword is `Assets/Art/Characters/bonk/Modeling_Weapon_Big_Sword.FBX` and is
referenced by `BonkWeaponAttachConfig.asset`. The package now declares `_weapon_hilt` and
`_weapon_tip` attachment points and its probe slash uses a baked hilt-to-tip capsule.
The package weapon config attaches the sword to the source rig's `hand_r` bone.
Source licensing remains an approval prerequisite before release, not before the
current built-in roster admission.

## Air 2 design — Rising Greatsword Sweep

Design agreed 2026-09-15. The animation candidate is authored and bound;
gameplay is not implemented. `air.2` still has the 30-tick, hitbox-free probe
timeline. The 42-tick specification below is the gameplay creation target,
not a claim about current runtime behavior.

Animation candidate: `Assets/Art/Characters/bonk/Animations/bonk_a2_rising_sweep.FBX`,
bound as `anim.bonk.a2`. Source frames 1–22 at 30 fps span 0.7 seconds.
Entry and handoff use the first pose of `jump_loop.FBX`, sampled on Bonk's
actual rig. The normal Blender FBX export imports as Humanoid with a
reference pose derived from Bonk's existing Avatar; no binary FBX patching
or gameplay-root correction is used.

Ability Lab resolved the binding through transient compiled content.
Its unchanged 30-tick timeline compresses the clip to 0.5 seconds
(1.4× intended speed): Lab tick 14 sampled clip time 0.326667 seconds.
Judge the intended timing from the 0.7-second source preview until the
separate gameplay timing change is implemented.

Editable source and review evidence:
`art/blender/exports/bonk-a2-37jjjrkv/bonk-a2-source.blend`,
`bonk-a2-preview.mp4`, `motion-sheet.jpg`, and `ability-lab.png`.
These local authoring artifacts include recovery copies of the prior Blender
session, original rig/import settings, and catalog. Unity checks sampled
43 times: zero root translation/rotation/scale drift and start/end bone
positions matching within 0.000001 m. Human motion/feel approval is pending.

### Role and counterplay

An aerial anti-air / juggling tool: a two-handed rising greatsword sweep
through the space in front of and above Bonk. It complements `air.1` forward
spacing, `air.3` downward spike, and `air.4` aerial kill read.

The sword tip draws one upward arc, not a full helicopter spin. Below and
behind Bonk remain vulnerable. Whiffing commits him through recovery; the
move is not an all-direction defensive bubble or a promised true-combo starter.

### Animation and timing target

All intervals below use zero-based 60 Hz gameplay ticks. Endpoints shown
as ranges are inclusive; tick 42 is the duration boundary.

| Phase | Ticks | Required pose / motion |
| --- | --- | --- |
| Startup | 0–11 | Gather the blade low and slightly across the body, with a compact, readable two-handed windup. No damaging contact. |
| Active | 12–19 | Sweep upward from in front of the chest to overhead in one continuous strike. |
| Recovery | 20–35 | Finish with the sword high, then visibly recover toward the airborne pose. No damaging contact. |
| Actionable / IASA | 36 | Bonk may act again. |
| Visual settle | 36–41 | Complete the return toward the airborne pose if uninterrupted. |
| Duration boundary | 42 | End of the 0.7-second timeline. |

Author the contact poses first: at tick 12 the blade enters useful
front-upper space; by tick 19 it has swept overhead.

First contact is at 200 ms. The active window lasts 8 ticks (about 133 ms).
At 30 fps, zero-based animation frame positions are: first contact 6,
active-window end boundary 10, IASA 18, duration boundary 21.
At 60 fps, frame positions match gameplay ticks. Preserve the 0.7-second
duration on export rather than confusing inclusive sample count with duration.

### Hitbox contract

- One moving capsule from `_weapon_hilt` to `_weapon_tip`, following the
  authoritative baked sword pose throughout ticks 12–19.
- Initial radius: 0.35 m, subject to visible-blade alignment in Ability Lab.
- One hit per opponent for the entire sweep. The moving capsule must not
  repeatedly damage an opponent as it passes through them.
- No stationary overhead sphere, extra torso hitbox, or added rear/below
  coverage. Close opponents are hit only where the sword capsule reaches.
- If implementation later splits the contact into multiple hitboxes, preserve
  the same one-hit-per-opponent contract through shared hit grouping.

### Provisional reward and landing cost

| Parameter | Initial target |
| --- | --- |
| Damage | 8 |
| Launch angle | 75° upward |
| Landing lag | 18 ticks |
| Early autocancel | Disabled |
| Late autocancel | Starts at tick 36 |

Reward intent is useful vertical lift, not strong sideways knockback.
Base knockback, growth, and actual hitstun remain undecided until the
authored move can be measured through Shared simulation. Damage, angle,
radius, and landing cost are starting values, not validated balance.
Do not claim a guaranteed follow-up or true combo without real-simulation evidence.

### Acceptance before gameplay approval

- Scrub ticks 12–19 in Ability Lab: the capsule follows the blade and the
  front-upper-to-overhead arc reads clearly at gameplay distance.
- Verify no damaging contact before tick 12 or from tick 20 onward, and
  exactly one hit per opponent across the sweep.
- Check representative front-upper and overhead targets, plus rear/below
  targets outside the blade path; there must be no invisible body coverage.
- Exercise whiff recovery, tick-36 actionability, landing lag, and the
  late-autocancel boundary through the authoritative runtime.
- Measure launch and follow-up opportunities at representative damage
  percentages; then playtest whether coverage, commitment, and reward
  distinguish it from Bonk's other aerials.

## Rig and bindings

`bonk.FBX.meta` reports `animationType: 3` (Humanoid). The catalog rig resolves to the
Bonk FBX root GameObject. The imported clips resolve through their Humanoid animation
subassets. The current headless checks produced no Unity console warnings. Clip importer
metadata still reports copied-avatar bone-length mismatch warnings; Avatar visual/pose
review remains an unverified prerequisite before gameplay use.

Intentionally bound semantic clips:

| Semantic ID | Asset | Pose track |
| --- | --- | --- |
| `anim.idle` | `Animations/idle.FBX` | `anim.idle` |
| `anim.run` | `Animations/run.FBX` | `anim.run` |
| `anim.jump` | `Animations/jump_start.FBX` | `anim.jump` |
| `anim.fall` | `Animations/jump_loop.FBX` | `anim.fall` |
| `anim.dash` | shared `Assets/Art/Characters/shared/Animations/dash.anim` | `anim.dash` |
| `anim.hit-light` | shared `Assets/Art/Characters/shared/Animations/hit_light.anim` | `anim.hit-light` |
| `anim.hit-medium` | shared `Assets/Art/Characters/shared/Animations/hit_medium.anim` | `anim.hit-medium` |
| `anim.hit-hard` | shared `Assets/Art/Characters/shared/Animations/hit_hard.anim` | `anim.hit-hard` |
| `anim.bonk.g1` … `anim.bonk.g4` | `Animations/bonk_g_1.FBX` … `bonk_g_4.FBX` | matching semantic ID |
| `anim.bonk.a1`, `anim.bonk.a3`, `anim.bonk.a4` | matching Bonk air FBX | matching semantic ID |
| remaining `anim.bonk.*` move IDs | `Animations/idle.FBX` fallback | matching semantic ID |

Each move slot owns its semantic ID. Replacing `anim.bonk.a1` no longer changes
the other move rows. The fallback bindings keep the probe cookable until the
remaining authored clips are available.

## Inspection and cook

```bash
unity pipeline list --format json
unity command --project-path client/Unity \
  sloparena.character.inspect --target bonk --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target bonk --format json
```

Observed inspection after the package retune: `success: true`, `packageId: bonk`,
sixteen resolved slots, trusted built-in profile, `dirtyOrStale: false`, and no
capability or binding diagnostics. Observed semantic cook: `success: true`,
source/cooked-source hash `f7e6ac01abe4326a6141328584d12887cce5bdc865c943279da8b3b2136d86fa`,
cooked-content hash `48e630f8c5e21172b1616df949cddbcb1a5f4268608efcbbe5cc074685a06149`,
package hash `d9aa234efeb75322fed85229eef2b24a19ede9a5dae4a3d6fa3cc1146bfee43c`.
The package is rostered as `Bonk`; a forced invalid cook preserved the last valid
cooked payloads and generated catalog before the source was restored and recooked.

Ability Lab and Character Select may discover Bonk through the verified package and
roster manifests. Compatibility remains the legacy-only path; Training and online
deployment remain release-gated.

## Findings

| Problem | Evidence | Impact | Fix or prerequisite |
| --- | --- | --- | --- |
| Editor status was hard-coded to FightGuy | `CharacterAssetCatalogEditor.OnEnable` and stale help text | Bonk could show the wrong status and diagnostics context | Status now reads only after catalog selection and names the selected package |
| Cook profile policy was duplicated | Service and catalog editor profile expressions | New packages could diverge in trust policy | Shared internal profile helper; `fightguy`, `kistu`, and `bonk` use the trusted built-in profile |
| Dependency tracking only inspected FightGuy | `CharacterCookAssetPostprocessor` | Bonk changes were not queued or isolated | Postprocessor now discovers every catalog and matches its persisted dependencies |
| Shared dash/hit clips were initially unresolved | Four `asset-catalog.clip.missing` diagnostics | Cook could not reach pose validation | Bound the existing shared clips used by FightGuy/Kistu |
| Temporary clip imports report bone-length mismatch warnings | `Animations/*.FBX.meta` `rigImportWarnings` | Pose quality is not yet approved | Re-author/import clips against the validated Bonk Avatar |
| No package creation control exists in Ability Lab | `AbilityLabPackageWorkspace.NewPackage` is the existing creation seam | Onboarding needs an editor-side API call | Add a UI control only when package onboarding is explicitly scoped |

Shared tests cover the package compiler, capability admission, authoritative E
hold/release/landing, timeout cancellation, and F's five independent contacts.
Bonk is admitted to the built-in roster and Character Select. Do not treat this
as approval for Training or online deployment until avatar visual/pose review
and the remaining release checks pass.
