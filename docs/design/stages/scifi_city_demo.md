---
key: scifi_city_demo
display_name: SciFi City Demo
design_brief: docs/design/stages/scifi_city_demo.design.md
variant: static
pvp_target: 2-4
competitive_intent: mixed
source_scene: client/Unity/Assets/Stages/scifi_city_demo/scifi_city_demo.unity
arena: data/arenas/scifi_city_demo.arena
presentation_prefab: client/Unity/Assets/Resources/Stages/scifi_city_demo.prefab
---

# SciFi City Demo

## Gameplay shell

The authoritative shell preserves the central rooftop cluster using `WestRoof`, `EastRoof`, `UpperDeck`, a continuous `BridgeDeck`, and `WestStairRamp`/`EastStairRamp`. The roof solids use primitive meshes; the bridge and ramps use stage-owned subassets in `CollisionMeshes.asset`. Shallow bridge joins and stair ramps provide continuous walking routes instead of exposing decorative triangle seams or stair risers to collision.

Four ordered spawns alternate between the west and east roofs. The first two occupy opposite-roof diagonal positions for a duel; the remaining two complete matching rows with equal within-roof spacing. All four face toward the bridge-centered fight space. The spawn repair preserves collision surfaces, spawn elevation, and overall arena bounds. Small art-panel height offsets and facade trim are intentionally excluded from the simple collision surfaces. The surrounding city remains presentation-only.

## Presentation

The cosmetic prefab is a stage-owned copy of the LowPolySciFiCity demo scene with vendor colliders, cameras, rigidbodies, and scripts removed. Its URP materials, local lights, neon emissives, and atmosphere remain presentation-only.

## Verification

Run bake and inspect after every gameplay-shell change. Human acceptance requires normal 2-player and 4-player PVP review; this demo is not accepted until that review passes.

## Current progress

**2026-09-16 — approved playability repair**

- Replaced art-derived collision meshes with simple stage-owned surfaces and restored the previously cosmetic-only final bridge segment.
- Fixed Shared capsule sweeps catching separated coplanar floor edges, including replay against the original dense rooftop mesh.
- Verified both bridge directions for every admitted character, stair ascent/descent, wall blocking, and ordinary flat-ledge walk-off behavior. Live Training routes crossed the bridge and both stair routes without losing groundedness.
- Bake and inspection pass with matching source/baked hashes, valid spawns, and no cosmetic colliders or missing asset references.
- The city presentation prefab is unchanged by this repair. Human two-player and four-player PVP acceptance remains outstanding.

**2026-09-03 — initial scene-integration milestone**

- The user considers the imported SciFi City world and its full background satisfactory for this stage's current goal.
- The complete background remains in the presentation prefab; the next iteration changes the gameplay shell, not the city composition.
- The user reports that the stage runs smoothly. No performance optimization work is planned without a measured regression on representative hardware.
- Automated preflight passed: bake produced 12,478 collision triangles and four spawns; inspection found matching source/baked hashes, zero cosmetic colliders, and no missing mesh/material references; Shared build, ArenaShipping tests, Server build, and Unity recompile passed.

## Next iteration

1. Human reviewers exercise roof movement, both bridge directions, stairs, jumping, recovery, and free camera rotation.
2. Reassess roof fighting space and route widths only after movement and crossings feel reliable; arena expansion is not part of this repair.
3. Author any accepted layout changes in the simplified stage-owned collision scene, then bake, inspect, and rerun the authoritative route regressions.
4. Keep the presentation prefab unchanged unless a visual change is intentional. Complete normal two-player and four-player PVP acceptance before treating the stage as accepted.
