---
name: sloparena-character-workflow
description: "Package-native workflow for designing, authoring, cooking, validating, and previewing SlopArena characters. Covers Character Packages, asset catalogs, deterministic cooking, generated bindings, admission, hashes, and Ability Lab."
category: game-dev
---

# SlopArena Character Workflow

Use this skill when designing or implementing a playable character. The canonical authoring guide is [`docs/characters/adding-a-new-character.md`](../../../docs/characters/adding-a-new-character.md). New characters are packages, not registry factories.

## Authority and boundaries

A Character Package contains exactly one playable character:

```text
client/Unity/Assets/CharacterPackages/<package>/
├── package.json                 # identity, version, dependencies, creator, license
├── character.json               # gameplay source and canonical slots
└── CharacterAssetCatalog.asset  # package-local Unity asset bindings
```

`package.json` owns package metadata. `character.json` owns gameplay semantics: movement, hurtboxes, animation IDs, fixed timelines, typed operations, timings, and parameters. `CharacterAssetCatalog.asset` owns imported rig and clip bindings. Do not duplicate one fact across these modules.

The authoritative pipeline is:

```text
source package
    │
    ├── Shared CharacterPackageCompiler
    ├── Unity asset catalog and pose cook
    └── package assembler and hash calculation
          │
          ▼
content-cooked/<package>/
  manifest.json
  character.runtime.json
  poses.bin
  client.bindings
```

Raw JSON is cook input. Runtime consumers load the immutable cooked package through the Match Content Catalog. The generated client catalog is a regenerable cache, not a second source of gameplay truth.

## Canonical move grid

Every package resolves the sixteen canonical slots in this order:

```text
ground.1  ground.2  ground.3  ground.4  ground.A  ground.E  ground.R  ground.F
air.1     air.2     air.3     air.4     air.A     air.E     air.R     air.F
```

Use package slot IDs as persisted identity. Ability Lab labels are projections of that identity. `LMB`, `RMB`, `Q`, and other physical controls are input adapters, not alternate package slots. Ground/air aliases may appear once in authoring and are expanded by the compiler; runtime data is explicit.

## Workflow

### 1. Design the kit

Define the fighter's role, counterplay, movement profile, eight normals, four specials, recovery choice, and presentation needs. Author timing in 60 Hz ticks. Prefer engine-owned deterministic primitives and fixed timelines over bespoke simulation code.

### 2. Create source

Start from the minimal universal package template, not a copied existing fighter. Use stable lowercase semantic IDs (`anim.*`, package-local bones, and package-owned presentation IDs). Declare every built-in capability requirement and every referenced animation or bone.

Community/Workshop profiles may use approved versioned primitives only. Trusted built-in profiles may use explicitly admitted temporary `slop.internal.*` capabilities; the package cannot grant itself that privilege.

### 3. Bind Unity assets

Import the rig and clips through Unity. Bind them in `CharacterAssetCatalog.asset` with one semantic animation ID and one deterministic pose-track ID per required animation. The Unity cook stage bakes pose data from the exact clip, rig, import settings, and catalog binding. Do not hand-coordinate a standalone pose file with a clip.

Use Humanoid for ordinary humanoid fighters when built-in retargeting helps. Custom rigs remain valid when they provide the required package-owned animation data. Keep art and naming rules in [`docs/contributing/conventions.md`](../../../docs/contributing/conventions.md).

### 4. Inspect, cook, and repair

The supported agent loop is direct source editing plus typed Unity CLI commands:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

`inspect` reports the canonical 16-slot projection, source/cooked hashes, status, stale reasons, and diagnostics. `cook` validates the Shared source, resolves Unity bindings, bakes poses, writes generated bindings, assembles the immutable package, and reports source, cooked-content, and package hashes.

A semantic failure has a structured result with `success: false`; a failed cook preserves the last valid cooked artifact and status. Repair diagnostics and retry. Do not edit generated runtime output to hide a source or catalog error.

### 5. Admit and verify

The package assembler writes an immutable manifest under `content-cooked/<package>/`. The manifest pins package ID, version, cooked-content hash, package hash, schema/API compatibility, dependencies, and capability requirements. Built-in roster admission pins the exact package requirement in `content-cooked/roster/manifest.json`.

Validation must fail closed for missing or mismatched source, catalog, pose payload, capability, dependency, schema, or hash. A Match Content Catalog pins the verified package set per match; a later recook cannot change a running match.

### 6. Preview through the real path

Ability Lab edits drafts and previews valid drafts through the in-memory cooked definition and the same interpreter used by Training, PvP, and GameServer. Invalid drafts may show a clearly non-authoritative editing pose only; they must never silently become match content.

Check the package in Ability Lab, then exercise Training. For online content, verify server admission and exact client/server hash agreement. Presentation resolves semantic IDs to generated package bindings and plays through Animancer; presentation never feeds back into simulation.

## Legacy compatibility

Manki, Kistu, and Nilus are legacy implementation cases behind `LegacyCharacterCatalogAdapter` until their package migrations. The legacy path is **modification-only compatibility**, not a template for new work.

For legacy maintenance, preserve its existing registry and baked-data contracts unless the migration task explicitly changes them. For new work, do not:

- add `Build<Name>`, `BuildRegistry`, `CharacterRegistry`, or `(CharacterClass, slot)` dispatch;
- copy `MankiData` or another legacy character definition;
- load raw authoring JSON at runtime;
- add registry overrides or manual FightGuy animation-config ownership;
- make a standalone skeleton `.bin` the runtime owner of a new package;
- introduce a second persisted slot mapping or a client-only gameplay path.

## Verification checklist

- source package has one owner for every authored fact;
- compiler resolves all sixteen slots and rejects invalid IDs, units, references, and budgets;
- catalog bindings match required semantic IDs and exact imported assets;
- cook succeeds and replaces the prior artifact atomically;
- manifest, runtime definition, pose payload, generated bindings, and hashes agree;
- Ability Lab and Training consume the cooked definition;
- Shared/server authority remains unchanged and presentation is client-only.
