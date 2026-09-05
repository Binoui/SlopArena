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

The authoritative shell is the stage-owned copy of the 19 source-scene objects tagged `Floor` when the design was locked. It includes the central multilevel roof, stairs, and bridge. Four grounded spawns support the current 2-4-player contract. The user will deliberately revise this selection after the first playable integration.

## Presentation

The cosmetic prefab is a stage-owned copy of the LowPolySciFiCity demo scene with vendor colliders, cameras, rigidbodies, and scripts removed. Its URP materials, local lights, neon emissives, and atmosphere remain presentation-only.

## Verification

Run bake and inspect after every gameplay-shell change. Human acceptance requires normal 2-player and 4-player PVP review; this demo is not accepted until that review passes.

## Current progress

**2026-09-03 — initial scene-integration milestone**

- The user considers the imported SciFi City world and its full background satisfactory for this stage's current goal.
- The complete background remains in the presentation prefab; the next iteration changes the gameplay shell, not the city composition.
- The user reports that the stage runs smoothly. No performance optimization work is planned without a measured regression on representative hardware.
- Automated preflight passed: bake produced 12,478 collision triangles and four spawns; inspection found matching source/baked hashes, zero cosmetic colliders, and no missing mesh/material references; Shared build, ArenaShipping tests, Server build, and Unity recompile passed.

## Next iteration

1. The user adjusts the source scene's `Floor` tags to tune the playable layout.
2. Regenerate the stage-owned collision source from that selection, then bake and inspect.
3. Keep the existing presentation prefab unchanged for gameplay-only tag changes. Refresh it only after intentional world-visual edits.
4. Complete human 2-player and 4-player PVP acceptance before treating the stage as accepted.
