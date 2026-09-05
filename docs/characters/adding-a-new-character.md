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
  grid.
- `CharacterAssetCatalog.asset` owns Unity rig and clip bindings.

The starter values are editable defaults, not approved gameplay balance. Replace them with
the character's approved kit data before roster admission. Import package-local source assets
with their `.meta` files. Record licensing and stop if a source asset is not redistributable.

## 2. Validate and bind assets

Import the rig as Humanoid when the character is an ordinary humanoid fighter. Inspect the
Avatar before binding clips. Reject invalid orientation, scale, root motion, required-bone,
or non-finite-pose diagnostics. Do not remap a bad rig at runtime or own a standalone
skeleton binary.

Bind each required semantic animation ID to the exact imported clip in
`CharacterAssetCatalog.asset`. Assign one unique deterministic pose-track ID per semantic
ID. Confirm catalog package ID, schema version, sample rate, rig, and binding paths. Do not
use another character's clip as a fallback.

## 3. Author the canonical grid

Persist these sixteen slot IDs, in ground-then-air order:

`ground.1`, `ground.2`, `ground.3`, `ground.4`, `ground.A`, `ground.E`, `ground.R`,
`ground.F`, then the same eight IDs under `air.`. Physical controls are input adapters,
not alternate move identity. Author fixed timelines in 60 Hz ticks with engine-owned
operations. Define damage, hitboxes, recovery, movement, and capability values only from the
approved kit specification.

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
Nilus remains behind `LegacyCharacterCatalogAdapter` until its migration; its legacy files are modification-only compatibility, not templates for new packages.
