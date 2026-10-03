# Adding a Character

A character is a package, not a registry factory. New package work must have an approved
kit and presentation specification before it becomes roster content.

## 1. Create authoring-ready source

Create `client/Unity/Assets/CharacterPackages/<package>/` through
`AbilityLabPackageWorkspace.NewPackage`. The pipeline writes an authoring-ready source
template with sane movement, capsule, hurtbox, presentation, thirty-tick timelines, and
one unique `anim.move.*` semantic ID per canonical slot. The catalog receives matching
empty binding rows.

Keep the three authoring modules separate:

- `package.json` owns identity, version, creator, license, attribution, and dependencies.
- `character.json` owns gameplay source, presentation IDs, and the canonical sixteen-slot
  grid, capture geometry/anchors, and standardized defense animation roles.
- `CharacterAssetCatalog.asset` owns Unity rig and clip bindings.

The starter values are editable defaults, not approved gameplay balance. Replace them with
the character's approved kit data before roster admission. Import package-local source assets
with their `.meta` files. Record licensing and stop if a source asset is not redistributable.

`character.json` uses `authoringSchemaVersion: 3`. Author a positive finite
`movement.airDodgeSpeed` independently of the historical `dashSpeed`; it is
the forward air dodge's horizontal speed for ten simulation ticks. The
current admitted fighters use 11 m/s except Wibou at 12.1 m/s. Older source
and cooked schema versions cannot be admitted without migration and a recook.

## 2. Validate and bind assets

For generated-model cleanup or an existing-model replacement, follow the
[character model polish guide](character-model-polish.md) before final integration.
It separates surface repairs from rig changes and defines visual, retargeting and
cooked-pose acceptance.

Import the rig as Humanoid when the character is an ordinary humanoid fighter. Inspect the
Avatar before binding clips. Reject invalid orientation, scale, root motion, required-bone,
or non-finite-pose diagnostics. Do not remap a bad rig at runtime or own a standalone
skeleton binary.

Bind each required semantic animation ID to the exact imported clip in
`CharacterAssetCatalog.asset`. Assign one unique deterministic pose-track ID per semantic
ID. Confirm catalog package ID, schema version, sample rate, rig, and binding paths. Do not
use another character's clip as a fallback.

Optional `presentation.crouch` and `presentation.slide` IDs require real bound clips and
distinct deterministic pose-track IDs when nonempty. They may share one static low clip.
The renderer and authoritative hurtboxes hold its frame zero; enter/exit uses zero fade.
Author planted feet and a lowered articulated skeleton without changing the model root,
stage capsule or pushbox. Low-track baking inverts the renderer's root/visual-scale mapping
instead of subtracting the sampled hips; other tracks retain their existing convention.
Absent legacy bindings remain upright and cannot enable CrouchBrace. Never fabricate
duck protection with a shrunken hurtbox or a runtime offset.

Every package also authors a finite `shieldRadius` in meters, greater than
half its `capsuleHeight`. Shared uses that sphere for shielded attack contact,
centered on the capsule origin; the held VFX uses exactly the same radius.
Choose a radius that encloses the character's defense pose without hiding
the fighter. The value is cooked and hashed, not a Unity collider setting.

Every package must define `captureGeometry` in `character.json`: positive `reach`,
`width`, and `height`, a vertical `offsetY`, and local-space `attackerAnchor` and
`victimAnchor` positions in meters. Local +Z is fighter-forward. Keep the capture volume
short and grounded around the character's authored body size; place both anchors at
plausible torso height and forward of the character origin. The per-character values
are authoritative package content and flow through validation, cooking, and hashes;
never edit generated `character.runtime.json` as their source.

Ability Lab exposes the same `captureGeometry` under **Moves → Ground → Grab** as
numeric volume and restraint-anchor fields, with a Scene-view wire preview.
Grab is an editor-only selector, not a seventeenth canonical ability slot.
Changes participate in workspace undo and live draft validation; **Save** crosses
the normal package cook boundary. The active in-match grab Gizmo reads the
cooked value, never a separate Inspector copy.

For PvP, verify the cooked result and refresh the admitted roster pin after
saving; the transient Ability Lab preview does not change a pinned match.

The defense presentation roles are `presentation.shield`, `grab`, `grabbed`,
`throwForward`, and `airDodge`. They may be empty until real clips are authored. Once a
role has a semantic animation ID, the package cooker requires its normal asset-catalog
binding and deterministic pose track; do not map a role to a generic fallback clip.

## 3. Author the canonical grid

Persist these sixteen slot IDs, in ground-then-air order:

`ground.1`, `ground.2`, `ground.3`, `ground.4`, `ground.A`, `ground.E`, `ground.R`,
`ground.F`, then the same eight IDs under `air.`. Physical controls are input adapters,
not alternate move identity. Author fixed timelines in 60 Hz ticks with engine-owned
operations. Define damage, hitboxes, recovery, movement, and capability values only from the
approved kit specification.

`allowSlideCarry` defaults false. Only grounded normals 1–4 may opt in, without aiming,
recovery designation, positive warp range, SetVelocity, ForwardLunge, SetAimState or
StartCapability operations. Conflicts fail compilation with
`slot.slide-carry.motion-conflict`; aliases inherit the resolved target value.

### Optional attack hit VFX

Add an optional slot field such as:

```json
{"hitPresentationId": "presentation.fightguy.ki-shot.hit"}
```

Declare the ID in the document's `presentationIds` and bind it to an owned prefab
in `CharacterAssetCatalog.asset`. Ground/air aliases inherit the resolved target's
hit ID; explicit slots may use different IDs. Omit the field to retain the shared
light/medium/heavy/launch graphics. These are prefab bindings, not animation IDs
or pose tracks; the compiler and package admission require declared, bound IDs.

An override replaces only the hit visual. Shared accepted contacts supply the
captured slot/air identity and world contact position; shields, invincibility,
counters and misses do not emit custom hit events. PvP displays the confirmed
server event once, not predicted contact effects. Sounds, Hitstop and controller
feedback retain their existing behavior. Hit one-shots use the dispatcher's
150-tick cleanup limit; tune particle size/color on the owned prefab and keep its
emission finite.


## 4. Inspect before cook

Inspection is read-only and must happen before cooking:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
```

Require the expected package identity, sixteen-slot projection, source status, structured
source/catalog diagnostics, and exact dependency list. Repair invalid or stale inputs
before continuing.

## 5. Cook and verify four payloads

`SAVE + COOK` is the only authoritative cook path:

```bash
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

A successful cook atomically produces exactly these payloads under
`content-cooked/<package>/`:

- `manifest.json` — identity, versions, dependencies, capabilities, and hashes;
- `character.runtime.json` — normalized Shared runtime definition;
- `poses.bin` — deterministic pose payload;
- `client.bindings` — generated semantic client bindings.

The generated catalog is a regenerable cache, not a source of gameplay truth. Require
matching source, cooked-content, package, payload, and dependency hashes. A failed cook
returns semantic `success: false` and preserves the last valid artifact, generated cache,
and cook status. It must not promote invalid drafts.

Generated packages require runtime API `1.2.0` (maximum `1.x`); schema version remains 1
for additive fields. The loader admits known minima `1.0.0`, `1.1.0`, and `1.2.0`.
Runtime API 1.2.0 adds the timed gravity-window timeline operation. Older runtimes must
reject packages that require it.
Semantic animation IDs and pose-track IDs are distinct namespaces: validated client
bindings map loaded pose tracks to runtime semantic lookup IDs.

## 6. Admit exact content

Only after a successful cook, complete presentation assets, and kit regression proof, add
the exact package requirement to `content-cooked/roster/manifest.json` and the corresponding
selector/admission path. Verify manifest identity, version, cooked hash, package hash,
capability versions, and client/server compatibility. A source-only package may be
discoverable in Ability Lab while remaining unrostered.

## 7. Exercise runtime surfaces

Open the package in Ability Lab and verify semantic bindings, canonical slot projection,
preview identity, and diagnostics. Then exercise Training and a local match with the same
cooked package. Server simulation remains authoritative; Unity presentation resolves the
cooked semantic IDs and never decides hit results, damage, timing, or admission.

## 8. Add regression coverage

Once gameplay is specified and the package cooks successfully, add focused compiler,
catalog, and package tests. Add `KitScenario` golden snapshots for authored damage,
knockback, timing, recovery, and capability behavior. Do not add gameplay goldens for a
source-only probe with invented values.

## Prohibitions

Do not add `Build<Name>`, registry factories, legacy adapter branches, raw runtime JSON
loaders, manual animation configs, standalone skeleton ownership, or a second persisted
move mapping. Nilus compatibility and character/slot factory dispatch have been removed.
