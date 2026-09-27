---
key: slop_pit
display_name: Slop Pit
target: pvp
players: 2-4
variant: static
authoritative_capability: null
availability: human-pvp-review-pending
competitive_intent: mixed
topology: open deck with diagonal equal platforms
source_scene: client/Unity/Assets/Stages/slop_pit/slop_pit.unity
baked_arena: data/arenas/slop_pit.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/slop_pit.prefab
design_brief: docs/design/stages/slop_pit.design.md
preflight:
  bake: "unity command --project-path client/Unity sloparena.stage.bake --stage slop_pit --format json"
  inspect: "unity command --project-path client/Unity sloparena.stage.inspect --stage slop_pit --output .stage-authoring-cache/slop_pit/inspection.json --format json"
  arena_tests: "dotnet test tests/Shared.Tests/ --nologo --filter FullyQualifiedName~ArenaShipping"
human_review:
  owner: project maintainer
  process: external normal PVP host/lobby/character-select/stage-select review at 2 and 4 players
  status: pending
---

# Slop Pit

## Fight premise

A broad open deck gives fighters room to brawl, while four perimeter walls run from deck level down to the blast plane. Two equal diagonal platforms create simple high-ground choices without taking over the central fight.

## Gameplay shell

The static shell is a 32×26 deck with bounds x −16..16 and z −13..13, top y=0, and bottom y=−1. Four continuous perimeter walls start at the deck surface and descend to the authored kill height y=−11; their wall collision prevents fighters from dropping beneath the deck. The kill height is fixed by the empty `AuthoringAids/KillPlane` marker.

- Two solid thin platforms are 6×5, with top y=2 and bottom y=1.5.
- Platform centers are (−10,−8) and (10,8), forming an equal diagonal pair.
- Both platforms are intended as full-jump-accessible positional choices; the open center remains the primary combat space.
- Four ordered deck spawns have X/Z positions (−5,−4), (5,4), (−5,4), and (5,−4). Their marker centers use y=0.85 under the existing spawn-marker contract; the deck surface remains y=0.
- Side blast boundaries remain derived from the baked collision shell; the authored kill-height marker preserves the current death plane while the walls extend down to it.
- No hazards, moving geometry, decorative colliders, or client-owned gameplay behavior are part of the static variant.

## Readability and balance intent

This is a mixed-intent stage with a broad central fighting floor. The four perimeter walls make the stage read as a contained industrial pit rather than a floating deck; the equal diagonal platforms provide symmetric positional choices while preserving the central fight. Recovery and blast behavior remain subject to the pending external 2-player and 4-player PVP review.

## Presentation

The cosmetic prefab is `client/Unity/Assets/Resources/Stages/slop_pit.prefab`. The accepted low-poly presentation uses staggered steel plates, restrained rust repairs, hazard-striped fascia, shallow platform frames, four sparse service galleries, two static conveyors and a shallow scrap field. Four steel perimeter walls use the deck surface treatment and continue from the deck edge down to the kill plane. Cool blue-gray distance and warm gallery/deck-edge accents preserve a quiet center. No giant sign, crowds or moving lifts.

The `LowPolyFPS6` scaffold and worklight prefabs were selected and inspected through `.asset-catalog-cache/slop-pit-reference/workset.json` and `inspection.json`. Vendor sources are unchanged. Stage-local primitive assemblies, palette materials and a hazard texture are embedded in the cosmetic prefab. Original Blender meshes under `Assets/Stages/slop_pit/Art/` provide folded panels, open pipes and crushed casings.

Per repository policy the licensed-art-dependent Resources prefab remains local/ignored and must accompany a complete art checkout. Its `.meta` and vendor dependencies must be preserved with it; the authoritative scene and arena alone cannot reproduce the visual stage. Our three original FBX meshes and their import metadata are tracked explicitly. Editable Blender source is retained locally at `.stage-authoring-cache/slop_pit/art-pass-03/scrap-source.blend`.

Local iteration backups and captures are retained under `.stage-authoring-cache/slop_pit/art-pass-01/` through `art-pass-05/`. The accepted design is locked in the design brief. Stage assets remain cosmetic; moving platforms require a separate authoritative capability.

## Production contract

The fixed authoritative source is `client/Unity/Assets/Stages/slop_pit/slop_pit.unity`; the typed bake writes `data/arenas/slop_pit.arena`; runtime presentation is discovered from the matching Resources prefab. The source scene must preserve the standard `Stage_slop_pit/GameplayGeometry`, `SpawnPoints`, and `AuthoringAids` hierarchy, with static gameplay meshes only under `GameplayGeometry`, four ordered tagged spawn markers, and an optional empty `AuthoringAids/KillPlane` transform. Without that marker, kill height remains derived as 10 units below the lowest geometry vertex; with it, its Y must exactly match the lowest geometry vertex and becomes the authoritative kill height.

The maintainer accepted the static gameplay layout and final visual treatment on 2026-09-17. Bake/inspect pass with matching source/baked hashes and four grounded spawns. Shared/Server builds and focused ArenaShipping coverage passed during authoring, including both FightGuy full-jump platform approaches. Shared smoke verified stable spawns, open-edge falling, blast death and grounded respawn. Live Training selection and gameplay-camera views were exercised with zero current Unity errors. Packaged client/server verification, measured performance and external human PVP review at both 2 and 4 players remain pending; visual acceptance does not imply release readiness.
The 2026-09-26 perimeter revision replaces open edges with four collision walls extending from the deck to the preserved kill height y=−11. Bake, inspect, and focused all-edge movement verification are required for this revision; external human PVP review at 2 and 4 players remains pending.
The accepted 2-unit platforms are near the full-jump limit: approach the inner edge before jumping. Existing rounded capsule support catches the lip and settles onto the top; a direct diagonal jump from the spawn can hit the underside. Preserve this accepted height unless the user requests further tuning.
