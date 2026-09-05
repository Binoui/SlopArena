---
name: sloparena-stage-authoring
description: "Author and maintain SlopArena PVP stages through separate authoritative collision baking and cosmetic Unity presentation, with fixed topology, asset provenance, structural inspection, and human PVP acceptance."
category: game-dev
---

# SlopArena Stage Authoring

Use this skill for a new PVP stage or a gameplay change to an existing PVP stage. It is an implementation workflow, not a concept-design guide.

Read [`docs/design/stage-concepts.md`](../../../docs/design/stage-concepts.md) before substantive work. That canonical page exists and is required input for topology, readability, and balance intent.

For new environment assets, read and complete [`sloparena-asset-selection`](../sloparena-asset-selection/SKILL.md) first. New assets require its verified workset and inspection evidence.

## Current authority and runtime path

```text
collision authoring scene
        │  sloparena.stage.bake
        ▼
data/arenas/<key>.arena
        │
        ├── Shared ArenaDefinition
        ├── GameServer MatchInstance
        └── client prediction/rollback

Resources/Stages/<key>.prefab
        │
        ▼
Unity MatchBase.SpawnStageVisual
        │
        ▼
cosmetic rendering, local lighting, presentation only
```

`src/Shared/ArenaDefinition.cs` is the authoritative PVP stage contract. It owns collision triangles, heightmap, bounds, blast lines, and spawn points. `Simulation` uses it for grounding, ledges, and collision; `ServerSimulation` resolves blast deaths and respawns; `MatchInstance` loads it on the dedicated server.

`client/Unity/Assets/Resources/Stages/<key>.prefab` is cosmetic. `MatchBase.SpawnStageVisual` loads it dynamically under the runtime Stage root. Unity scene colliders, physics, animation, and presentation must not decide PVP results.

The present stage registry is file-driven, not version/hash-pinned match content. `ArenaRegistry` discovers parseable `.arena` files; Stage Select offers non-training arenas only when a matching Resources prefab and nonempty collision data exist. Do not invent a registry entry, fallback, or version-admission mechanism in this workflow.

## Design brief prerequisite

Substantive stage work — a new stage, a gameplay change, or a visual pass that changes composition (masses, landmark, lighting intent) — REQUIRES a Stage Design Brief at `docs/design/stages/<key>.design.md` with `status: locked`, produced by [`sloparena-stage-design`](../sloparena-stage-design/SKILL.md). If it is absent, fail closed with exactly that message. Production does not invent visual intent.

Cosmetic-only maintenance that does not change composition (for example, replacing a broken material GUID in place) is exempt, matching the cosmetic-only carve-out under "Existing stages".

## Scope and availability

- Stages are PVP content only.
- Every accepted selectable stage currently supports **2–4 players**. The present selection/server path has no per-stage capacity admission, so a stage with fewer than four valid spawns is unsafe.
- `static` is the only currently available variant.
- `hazard`, `moving-geometry`, and special-mode variants must be declared explicitly in the stage brief, name their required authoritative capability, and fail closed until that Shared/server capability exists. Never approximate them with Unity-only scripts or decorative animation.
- The normal human PVP review is external to the repository. Agents must not drive it or claim that it passed.

## Required tool prerequisite

The repository provides the typed bake and inspect commands below. Use them instead of the legacy `Tools/SlopArena/Bake Arena...` editor menu:

```bash
unity command --project-path client/Unity \
  sloparena.stage.bake --stage <key> --format json

unity command --project-path client/Unity \
  sloparena.stage.inspect --stage <key> \
  --output .stage-authoring-cache/<key>/inspection.json \
  --format json
```

`sloparena.stage.bake` is a deliberate mutation: it opens the fixed authoring scene, validates its collision-source topology, and writes `data/arenas/<key>.arena` after source validation. It must not inspect or mutate cosmetic content.

`sloparena.stage.inspect` is read-only against stage source/output assets. It validates the complete source-to-baked-to-cosmetic relationship, writes a deterministic report and captures, and must not rebake or silently repair stale data.

Its generated output belongs in the local, gitignored directory:

```text
.stage-authoring-cache/<key>/
  inspection.json
  top.png
  front.png
  back.png
  left.png
  right.png
  isometric.png
```

Do not commit generated captures or reports. Human reviewers may attach them to the external tracker; the tracked brief intentionally contains no tracker URL or acceptance evidence link.

## Required stage files

Use one immutable lowercase `snake_case` key everywhere. For example, `slop_court`:

```text
docs/design/stages/slop_court.md
client/Unity/Assets/Stages/slop_court/slop_court.unity
client/Unity/Assets/Resources/Stages/slop_court.prefab
data/arenas/slop_court.arena
```

The key is immutable after acceptance. Change the display name in arena data and the brief, not the key. A new key is a deliberate migration, never a casual file rename.

The runtime Stage Select flow is file-driven. A matching valid `.arena` and Resources prefab are the delivery pair; do not create a separate manual stage-registration path.

## Stage brief

Every new stage has a tracked Markdown brief at:

```text
docs/design/stages/<key>.md
```

The file uses YAML front matter plus explanatory Markdown. It is a human production contract; `sloparena.stage.inspect` validates asset topology only and must not parse the brief as a runtime configuration source.

The front matter and body together bind:

- immutable key and mutable display name;
- PVP target, including 2–4-player support;
- declared variant and, when non-static, the required authoritative capability and blocked state;
- competitive intent: `competitive`, `mixed`, or `playful`; and the gameplay intent plus applicable Stage Concepts criteria;
- platform, traversability, spawn, and derived-boundary plan;
- visual concept and local-lighting intent;
- verified asset-selection workset/inspection references for newly introduced assets;
- fixed collision source scene, baked `.arena`, and cosmetic prefab paths;
- required automated preflight and the external human-review owner/process.

Do not duplicate baked collision values or render metrics into the brief. The `.arena` and inspection report are their respective sources of truth.

### Existing stages

Create or update this brief on the first **gameplay** change to an existing stage: gameplay geometry, spawns, bounds, blast-boundary behavior, or variant/capability behavior.

A cosmetic-only change to an existing stage has no dedicated stage-authoring gate by decision. It still must preserve the global authority boundary: cosmetics cannot introduce gameplay ownership, and new visual assets still require the asset-selection workflow.

## Collision authoring scene

The fixed scene path is:

```text
client/Unity/Assets/Stages/<key>/<key>.unity
```

It contains exactly one required root named `Stage_<key>`:

```text
Stage_<key>
├── GameplayGeometry
├── SpawnPoints
└── AuthoringAids
```

Rules:

- `GameplayGeometry` contains static `MeshFilter` collision geometry only. Unity Terrain is out of scope.
- No `MeshFilter` may exist outside `GameplayGeometry` anywhere under `Stage_<key>`.
- `SpawnPoints` contains empty GameObjects tagged `SpawnPoint`, named in strict order `Spawn_01` through `Spawn_04`.
- All four spawn markers must resolve onto authoritative gameplay ground after baking.
- `AuthoringAids` may contain non-mesh editor aids only. It must not contain decorative meshes, cosmetic prefabs, Terrain, or hidden visual references.
- Do not put art references under disabled objects: the current baker ingests descendant meshes and cannot safely distinguish dressing.
- The source root transform is position `(0,0,0)`, identity rotation, and scale `(1,1,1)`.

The collision scene exists only to produce authoritative arena data. Keep gameplay geometry sparse, intentional, and readable; use the Stage Concepts contract for what constitutes usable PVP space. The current baker derives the heightmap, X/Z bounds, kill height, side blast lines, and spatial grid from this geometry. Plan desired boundaries in the brief, bake, then inspect the derived result; no explicit blast-line override is available in this workflow.

## Cosmetic stage prefab

The runtime prefab path is fixed:

```text
client/Unity/Assets/Resources/Stages/<key>.prefab
```

Rules:

- Its root transform is position `(0,0,0)`, identity rotation, and scale `(1,1,1)`.
- It aligns with the authoritative source/baked coordinates with no offset, scale correction, or runtime alignment script.
- It contains **no `Collider` components** anywhere. Decorative collision is prohibited, including trigger-only convenience colliders.
- It owns stage-specific local lighting. The match scene supplies only global base lighting.
- It may reference only valid imported assets. Newly added environment assets need an approved asset-selection workset.
- It must not carry scripts that adjudicate gameplay, movement, spawn, blast, collision, hazard, or platform behavior.

This prefab is reviewed as art and presentation. It is never a substitute for collision baking.

## Authoring workflow

### 1. Establish the contract

0. Read the locked Stage Design Brief at `docs/design/stages/<key>.design.md` and quote its numbered decisions at the top of the production report. The design brief's decisions outrank this production brief's prose.
1. Confirm `docs/design/stage-concepts.md` exists and applies to the stage.
2. Create or update `docs/design/stages/<key>.md` with its YAML front matter and production contract.
3. Confirm the stage is static, PVP-only, and supports all 2–4-player matches. A non-static declaration blocks on the missing authoritative capability.
4. For new visual assets, produce and accept an asset-selection workset before introducing them to the prefab.

### 2. Author collision separately

1. Create or edit the fixed collision source scene.
2. Preserve the `Stage_<key>` hierarchy and source-root identity transform.
3. Put only static mesh collision under `GameplayGeometry`.
4. Place `Spawn_01` through `Spawn_04` as tagged empty markers under `SpawnPoints`.
5. Keep all decorative presentation out of the source scene.

Do not make collision geometry from the cosmetic prefab, copy arbitrary vendor mesh hierarchies into the bake source, or rely on disabled visual objects to avoid baking.

### 3. Author presentation separately

1. Compose `Resources/Stages/<key>.prefab` from approved assets.
2. Keep the visual root identity-aligned with the collision source.
3. Put stage-specific local lighting in the prefab.
4. Remove every Collider and gameplay-owning component from the prefab.
5. Preserve unrelated existing-stage content; use targeted edits, never broad scene regeneration.
6. Take visual intent only from the locked Stage Design Brief; never improvise composition.

### 4. Bake and inspect

After the typed tools exist, run the explicit sequence:

```bash
unity command --project-path client/Unity \
  sloparena.stage.bake --stage <key> --format json

unity command --project-path client/Unity \
  sloparena.stage.inspect --stage <key> \
  --output .stage-authoring-cache/<key>/inspection.json \
  --format json
```

Inspection must reject or fail the report for:

- missing/misnamed source scene or `Stage_<key>` root;
- MeshFilters outside `GameplayGeometry`, Terrain, or invalid source transforms;
- missing, unordered, untagged, non-grounded, or fewer than four spawn markers;
- unreadable/stale/mismatched `.arena`, missing collision triangles, or derived geometry inconsistent with the source;
- absent `Resources/Stages/<key>.prefab` or visual-root transform mismatch;
- any cosmetic Collider;
- missing mesh/material references, missing shaders, or unsupported shaders.

It must report, without a hard performance budget, renderer, triangle, material, shader, and local-light metrics for the cosmetic prefab. These are diagnostic evidence only; no performance gate exists in this workflow.

It captures six deterministic global views—top, front, back, left, right, and isometric—of the cosmetic prefab with the authoritative collision shell and spawn markers overlaid. The views expose density, sightlines, movement room, and visual-to-gameplay misalignment before human review.

### 5. Agent preflight

For every new stage and every gameplay change, run the established structural preflight before requesting human PVP review:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo --filter FullyQualifiedName~ArenaShipping
dotnet build src/Server/ --nologo

unity pipeline list --format json
unity command --project-path client/Unity recompile --format json
unity command --project-path client/Unity \
  get_console_logs --severity error --limit 20 --format json
```

Also run the typed bake and inspect commands above. Review the six captures and the inspection report. The preflight must prove the source/baked/prefab relationship, but it does not prove human playability.

Then run the brief-conformance self-check: for each numbered decision in the design brief, output one line `PASS` or `DEVIATION: <decision N> — <what differs and why>`. Deviations are reported to the user for accept/reject — never silently fixed, never silently accepted.

If the stage changes Shared/server source beyond baked data, run the project-required Shared/server contract tests for those code changes as well. Do not suppress unrelated current suite failures; report their existing cause separately.

### 6. External human PVP acceptance

After the agent preflight, a human performs the normal PVP host/lobby/character-select/stage-select flow. Agents do not perform this step and must not say it passed.

The human reviewer uses qualitative judgment for whether the stage is usable, sufficiently open, and not too crowded. They must exercise at least the 2-player and 4-player roster extremes, including spawn behavior, movement through the layout, edges, platforms, ledge recovery, blast boundaries, and combat space. Evidence and approval live only in the external tracker by decision; the stage brief contains no link or status record.

A stage is not ready for acceptance while the human PVP review is missing or failed, even if all automated checks pass.

## Explicit non-goals

- Client-only hazards, moving platforms, collision, or spawn logic.
- Unity Physics as PVP authority.
- Per-stage capacity subsets until Stage Select/server capacity admission exists.
- Explicit blast-boundary override controls.
- Terrain-backed authoritative stages.
- Versioned/hash-pinned stage admission.
- Automatic tracker upload or tracked PVP evidence.
- Performance-budget enforcement.
- Scene-wide regeneration, broad cleanup, or unrequested migration of untouched legacy stages.

## Completion checklist

- [ ] `docs/design/stage-concepts.md` exists and was used.
- [ ] Brief exists or was updated for the gameplay change.
- [ ] Key is immutable lowercase `snake_case`; display name is separate.
- [ ] Source scene, cosmetic prefab, and `.arena` use the fixed paths.
- [ ] Collision scene has the named root hierarchy; meshes only under `GameplayGeometry`.
- [ ] Four ordered `SpawnPoint` markers bake onto valid ground.
- [ ] Cosmetic prefab is identity-aligned, has local lighting as needed, and has zero Colliders.
- [ ] Every newly introduced asset has asset-selection workset evidence.
- [ ] Static variant is accepted; unsupported variants are blocked on explicit authoritative capability work.
- [ ] Typed bake and inspect tools exist and both succeed.
- [ ] Inspection report and six overlay captures are written to local cache and reviewed.
- [ ] Shared/server build and relevant arena tests pass; Unity recompiles with no current errors.
- [ ] External human PVP review passed at 2 and 4 players. Agents have not claimed that result without human evidence.
- [ ] Locked Stage Design Brief exists and was followed.
- [ ] Brief-conformance table reported.
