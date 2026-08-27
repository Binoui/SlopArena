# FightGuy Character Authoring/Cooking Cutover

**Status:** Planned — confirmed 2026-08-26  
**Decision:** [ADR-0029](../adr/0029-character-authoring-and-cooking.md)  
**Scope:** FightGuy vertical slice; Manki, Kistu, and Nilus remain temporary legacy source inputs behind one catalog-builder adapter.

Execution detail:
- [Phase 3 completion plan](../../PHASE_3_COMPLETION_PLAN.md)
- [Phase 4 authoritative-events plan](../../PHASE_4_AUTHORITATIVE_PLAN.md)


## Goal

Make FightGuy prove the complete Character Package path:

```text
Ability Lab + imported Unity assets
        │
        ▼
Character Package source
(package.json + character.json + Character Asset Catalog)
        │
        ▼
Pure gameplay compile + Unity asset/pose cook + package assembly
        │
        ▼
Committed immutable cooked package
        │
        ▼
Match Content Catalog
        ├── Ability Lab authoritative preview
        ├── Training / local simulation
        ├── PvP client
        ├── GameServer
        └── reports and tests
```

The cutover is complete only when no FightGuy consumer can read raw authoring JSON, `BuildFightGuy`, a mutable global override, or an animation clip key outside the cooked package contract.

## Current correction

The immediate R animation regression is repaired before the architectural work:

- `content/characters/fightguy/character.json` now maps both serialized R entries to `spell_r`.
- `CharacterContentSerializerTests.LoadFile_FightGuyRUsesDedicatedAnimation` is a temporary current-schema guard.
- The guard must be deleted during the atomic cutover and replaced by cooker validation that R references a complete `Animation Definition`.

## Non-goals

- Migrating Manki, Kistu, or Nilus authoring source.
- Hurtbox editing in Ability Lab; existing FightGuy hurtbox data migrates and remains visible.
- Wrapping FBX/GLB import or rig configuration; Unity import remains upstream.
- A general gameplay graph, creator variables, expression language, or authored transition predicates.
- Publishing to Steam Workshop. The cooked package must satisfy the already-decided identity/hash model, but upload/distribution UI remains outside this slice.
- Removing every temporary FightGuy native capability. Remaining exceptions must be explicit, trusted, versioned, and carry migration paths.

## Module seams

### Character package cooking

**External interface:** one package source in, either one complete immutable package or structured diagnostics out.

The caller must not coordinate serializers, alias expansion, operation validation, pose baking, source hashing, package hashing, or generated client bindings. Those are implementation details behind the cooking module.

Proposed internal seams:

1. `CharacterPackageCompiler` in Shared — pure authoring JSON → normalized Cooked Character Definition plus gameplay diagnostics.
2. `UnityCharacterAssetCooker` in Editor code — Character Asset Catalog + imported assets → Animation Definitions, deterministic pose payload, and private client-binding payload.
3. `CharacterPackageAssembler` — validates cross-payload references, computes source/package hashes, writes deterministic manifest/payload bytes atomically.

Ability Lab and CI cross the same cooking interface. Tests exercise the pure compiler directly and the complete cooker through its external interface.

### Ability timeline runtime

**External interface:** start, tick, and cancel one cooked ability timeline against simulation state.

The runtime hides operation dispatch, authored same-tick ordering, active stateful primitive lifetime, interruption cleanup, budgets, and presentation-event identity. FightGuy data never calls a character/slot factory.

Only operations required by the hotfixed FightGuy definition enter the first public catalog. Temporary native behavior is registered by explicit `slop.internal.*.v1` capability ID and accepted only under the trusted built-in cook profile.

### Match content

**External interface:** resolve a compact match-local `Content Handle` to an immutable Cooked Character Definition and its exact package requirement.

`MatchContentCatalogBuilder` is the only temporary seam that sees both systems:

- cooked FightGuy package; and
- immutable snapshots produced from legacy Manki/Kistu/Nilus definitions.

Simulation, rendering, UI, reports, and tests receive catalog entries or resolved definitions. They do not branch on whether content came from cooked or legacy source.

## Target source and artifact layout

```text
client/Unity/Assets/CharacterPackages/FightGuy/
├── package.json
├── character.json
├── CharacterAssetCatalog.asset
└── package-owned source assets or references

content-cooked/fightguy/
├── manifest.json
├── character.runtime.json
├── poses.bin
└── client.bindings          # private cooked payload, engine format
```

A generated Unity runtime catalog lives under a generated/gitignored `Assets` path and materializes `client.bindings`; it is never authoritative. StreamingAssets and server publish directories receive staged copies from `content-cooked/fightguy/`.

## Phase 1 — Freeze the hotfixed baseline

1. Preserve the hotfixed current JSON definition as the differential baseline fixture.
2. Add a harness that can run a definition through every FightGuy slot with deterministic input sequences and record:
   - CharacterState transitions and timers;
   - velocities and movement-resource fields;
   - spawned hitboxes/projectiles;
   - damage/knockback-facing data;
   - ability lifecycle completion/interruption; and
   - presentation events once introduced.
3. Cover ground and air aliases, natural completion, hitstun interruption, death interruption, and Burst cancellation where applicable.
4. Keep the baseline loader test-only. It must not become a second production fallback.

**Gate:** the harness is deterministic and red-capable by changing one known FightGuy timing, animation ID, or hitbox value.

## Phase 2 — Define source and cooked schemas

Create source-only models for:

- package manifest metadata;
- Character movement and presentation metadata;
- slots and source aliases;
- fixed stages/timelines;
- typed timeline operations;
- package-local semantic animation/presentation IDs; and
- trusted built-in capability requirements.

Create separate immutable cooked models for:

- expanded explicit slots;
- normalized typed operations;
- resolved Animation Definition IDs;
- operation/capability version requirements;
- deterministic budgets; and
- runtime compatibility/schema metadata.

Constraints:

- gameplay durations and triggers are `ushort` simulation ticks;
- source `id` and `class` fields are removed from the Character Authoring Document;
- aliases are validated for missing targets and cycles, then expanded;
- unknown fields, operations, capabilities, units, or enum values are errors;
- operation parameter schemas reject missing, unknown, out-of-range, or non-finite values;
- authored operation list order is preserved byte-for-byte in normalized semantic order;
- warnings never mutate semantics; and
- authoring and cooked schema versions advance independently.

Replace the current parity tests that register JSON before reading “expected” data. Tests compare parsed source to explicit contracts and compiled output to deterministic expected bytes.

**Gate:** pure compile tests cover valid FightGuy, every validation category, alias expansion, deterministic byte output, and explicit authoring-schema migration refusal.

## Phase 3 — Build the FightGuy operation catalog

Inventory every hotfixed FightGuy slot and express it with the minimum fixed-timeline operation set. Expected categories include:

- set or preserve velocity;
- spawn typed hitbox/projectile data;
- set bounded action/aim state owned by an engine primitive;
- start a stateful capability;
- emit a semantic presentation event; and
- complete the fixed ability duration.

Do not add operations for other Characters speculatively.

Replace loose FightGuy `Params` with typed fields. Generic normals execute through the timeline runtime. Existing FightGuy special classes may remain temporarily only as explicitly registered internal capabilities; remove `(CharacterClass.FightGuy, slot)` dispatch from `AbilityFactory`.

The runtime executes same-tick operations in authored order. Active stateful primitives implement start/cancel/complete. The generic runtime invokes cancellation on hitstun, death, Burst, or other authoritative interruption without an authored cleanup list.

**Gate:** focused timeline-runtime tests pin operation order, typed validation, cancellation, natural completion, and internal-capability admission. No FightGuy runtime behavior depends on a string parameter lookup.

## Phase 4 — Add authoritative presentation events

Add a stable Shared event record keyed by:

- match tick;
- entity ID; and
- cooked operation index.

The simulation emits semantic presentation IDs. Local and predicted paths can produce them immediately; GameServer transport confirms remote/complex paths; clients deduplicate replayed/confirmed events by stable identity. Presentation resolution remains client-only and cannot affect simulation state.

Update packet/version codecs only once the event shape is fixed. Add round-trip, duplicate suppression, rollback replay, packet-loss, and late-confirmation tests before wiring visual adapters.

**Gate:** the same FightGuy timeline produces one observable presentation event per authored operation in local, predicted, and server-confirmed paths without duplicates.

## Phase 5 — Implement Unity asset cooking

Add one `CharacterAssetCatalog` authoring type and focused editor for imported asset assignment. Each FightGuy Animation Definition binds:

- one package-local semantic ID;
- one imported AnimationClip;
- the FightGuy rig/import dependency set; and
- the metadata required to bake a deterministic pose track.

The Unity cook stage bakes pose data from the exact clip and rig reference. It rejects missing clip/pose pairs, duplicate IDs, incompatible rigs, invalid sample metadata, unsupported assets, and unresolved animation IDs used by the Character Authoring Document.

Generate the runtime Unity catalog from the canonical private client payload. It replaces manual `FightGuy_AnimConfig.asset` ownership; generated output may use a ScriptableObject internally, but creators never hand-edit it.

Track every transitive dependency, including import settings and cooker/toolchain version. Unity asset changes mark the package stale and trigger a debounced recook.

**Gate:** changing FightGuy R’s bound clip automatically changes the source hash, rebakes its paired pose track, regenerates the runtime binding, and leaves no manual rebake/recompile step.

## Phase 6 — Assemble and stage the cooked package

Write deterministic payloads to a temporary directory, validate every cross-reference, compute payload/package hashes, then atomically replace `content-cooked/fightguy/` only on success.

The manifest records:

- package ID and development or published version;
- exact cooked-content hash;
- source hash;
- authoring/cooked schema versions;
- dependencies and capability requirements;
- payload hashes;
- cooker/toolchain identity; and
- warnings.

Commit the canonical cooked package. Add one verification command that recooks and byte-compares the committed artifact. Replace build/release/server-project staging of raw FightGuy JSON and scattered pose files with copies from the canonical package.

Local source checkouts may load a Stale Cook, but status must include source hash, cooked source hash, and current cook diagnostic. CI, release staging, and online admission reject stale or mismatched content.

**Gate:** a clean checkout can verify the committed package byte-for-byte; client and GameServer stage the same package hash.

## Phase 7 — Add immutable match content

Introduce the Built-In Roster Manifest:

- source form maps legacy `CharacterClass` selectors to stable package IDs;
- cooked form pins exact version/hash requirements.

Add `MatchContentCatalogBuilder` and immutable `MatchContentCatalog`. The GameServer resolves exact packages, validates them, assigns compact handles, and sends the handle map before simulation. Local Training builds the same catalog in-process. A running match retains its catalog after recooks; the next match captures the new package hash.

The temporary legacy adapter snapshots Manki/Kistu/Nilus into the same immutable runtime shape. It is the only module allowed to call the old registry during this slice.

Update consumers identified by symbol/reference search:

- `src/Server/MatchInstance.cs` and `src/Server/Program.cs`;
- `client/Unity/Assets/Scripts/Runtime/World/GameManager.cs`;
- `TrainingMatch.cs` and `PvPMatch.cs`;
- `ResultsUI.cs` and Character-selection display paths;
- Ability Lab character/package selection;
- Shared test helpers;
- MoveDataReport, MovementReport, SelfPlayReport, AbDiffReport, and SpectateView; and
- every FightGuy-specific test currently using `CharacterRegistry.Get`.

**Gate:** two simultaneous match catalogs can resolve different FightGuy development hashes without shared mutation; all consumers display/simulate the definition pinned by their own catalog.

## Phase 8 — Make Ability Lab the package editor

Add New/Open Character Package flows. New creates:

- source package manifest;
- minimal Character Authoring Document with universal slots and empty timelines; and
- empty Character Asset Catalog.

Missing required assignments appear as structured cook errors, not hidden defaults. FightGuy mechanics or internal capabilities are never copied into new packages.

Migrate existing Ability Lab editing to source DTOs. It edits movement, slots, fixed timelines, typed operations, animation IDs, timings, hitboxes, and parameters. Hurtboxes remain displayed from migrated source data but read-only for this slice.

Save behavior:

1. compare disk hash to loaded hash and block on conflict;
2. persist the draft deterministically;
3. attempt a full cook;
4. on success, replace the canonical package and refresh authoritative preview through the cooked runtime path;
5. on failure, retain the prior cooked artifact, show structured diagnostics, and mark visual draft preview non-authoritative.

Expose explicit authoring-schema migration and atomic semantic-ID rename operations. Do not implement automatic rewriting or field-aware merge.

**Gate:** create a minimal package, assign imported FightGuy assets, edit R’s Animation Definition, save, cook, and start a new local match that resolves the new hash without restart/recompile/rebake rituals.

## Phase 9 — Atomic FightGuy cutover

After all preparatory gates pass, switch every FightGuy production consumer in one cutover:

1. stage the source package under `Assets/CharacterPackages/FightGuy/`;
2. commit its deterministic cooked package;
3. load FightGuy only through the Built-In Roster Manifest and Match Content Catalog;
4. remove direct raw `character.json` loading from Ability Lab runtime, `GameManager`, and GameServer startup;
5. remove FightGuy `RegisterOverride` calls and override tests;
6. delete the authored `BuildFightGuy` definition and its registry entry path;
7. remove manual FightGuy AnimationConfig ownership and scattered baked-data staging;
8. replace current serializer/parity tests with compiler/package tests;
9. delete the temporary `spell_r` current-schema assertion; and
10. update Ability Lab, animation-system, build/release, and character-import documentation to name the new source/cooked/runtime ownership.

No feature flag, dual runtime loader, generated C# snapshot, or fallback survives the cutover.

## Differential acceptance

Run the Phase 1 harness against the hotfixed current JSON definition and the final Cooked Character Definition for every FightGuy ground/air slot. Compare the full observable trace. Any delta requires an explicit named approval in the plan/PR; architecture migration alone is not permission to retune gameplay.

Then verify:

- focused source/compiler/cooker/catalog/timeline/event tests;
- complete FightGuy behavioral regressions;
- `dotnet build src/Shared/ --nologo`;
- `dotnet test tests/Shared.Tests/` with known unrelated debt separated from new failures;
- `dotnet build src/Server/`;
- deterministic recook/byte comparison;
- Unity CLI Pipeline recompile/status gate with zero current console errors; and
- Unity playtest: Ability Lab, Training, and PvP/local GameServer all show FightGuy R’s paired Cyclone Kick clip and pose track from the same package hash.

## Completion checklist

- [ ] Character Package source has one owner per fact.
- [ ] FightGuy raw authoring JSON is never loaded by runtime.
- [ ] FightGuy C# authored definition and fallback are deleted.
- [ ] FightGuy animation binding and deterministic pose track share one Animation Definition.
- [ ] Manual rebake/recompile/restart is not required after a valid asset/source save.
- [ ] Cooked package is deterministic, committed, hash-verified, and staged identically for client/GameServer.
- [ ] Ability Lab previews valid drafts through an in-memory cook and real runtime interpreter.
- [ ] Invalid drafts preserve the last valid cook and show structured diagnostics.
- [ ] Stale local execution has a persistent visible warning; release/online fail closed.
- [ ] FightGuy uses typed fixed timelines; no loose runtime parameter maps or hidden class/slot dispatch remain.
- [ ] Match Content Catalog is immutable and match-scoped.
- [ ] Temporary legacy registry access is isolated to the catalog builder.
- [ ] Differential traces contain no unexplained gameplay changes.
- [ ] Temporary internal capabilities have documented owners and migration paths.
