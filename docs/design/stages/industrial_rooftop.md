---
key: industrial_rooftop
display_name: Industrial Rooftop
target: pvp
players: 2-4
variant: static
authoritative_capability: null
availability: preflight-required
competitive_intent: mixed
topology: connected-roofs arena
source_scene: client/Unity/Assets/Stages/industrial_rooftop/industrial_rooftop.unity
baked_arena: data/arenas/industrial_rooftop.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/industrial_rooftop.prefab
design_brief: docs/design/stages/industrial_rooftop.design.md
asset_selection:
  concept: industrial-rooftop
  profile: .asset-catalog-cache/industrial-rooftop/profile.json
  probe_workset: .asset-catalog-cache/industrial-rooftop/probe-workset.json
  workset: .asset-catalog-cache/industrial-rooftop/workset.json
  inspection: .asset-catalog-cache/industrial-rooftop/inspection.json
  report: .asset-catalog-cache/industrial-rooftop/report.html
  contact_sheet: .asset-catalog-cache/industrial-rooftop/contact-sheet.png
preflight:
  bake: "unity command --project-path client/Unity sloparena.stage.bake --stage industrial_rooftop --format json"
  inspect: "unity command --project-path client/Unity sloparena.stage.inspect --stage industrial_rooftop --output .stage-authoring-cache/industrial_rooftop/inspection.json --format json"
  arena_tests: "dotnet test tests/Shared.Tests/ --nologo --filter FullyQualifiedName~ArenaShipping"
human_review:
  owner: project maintainer
  process: external normal PVP host/lobby/character-select/stage-select review at 2 and 4 players
  status: pending
---

# Industrial Rooftop

## Fight premise

Two roofs, one dangerous crossing. Fighters contest the main roof's open floor and its HVAC high ground, then decide whether the second roof's flank position is worth the bridge — the only way across an alley that kills. The city reads from every direction; the bridge is the place everyone remembers.

## Gameplay shell

Twin Roofs: one main roof plus a lower second roof connected by a catwalk bridge, over an open-to-blast alley.

- The main roof deck (34×18, top y=0) remains the primary combat floor.
- The HVAC service decks are the main floor's high ground, in two tiers west of center (user-tuned heights): level 1 at top y=2.01, one well-timed jump from the floor; level 2 at top y=4.43, out of double-jump reach from the floor but reachable from level 1 with a double jump. No stair access; both are shell collision.
- The second building roof (14×10, top y=-1.2) is flank and reset space, 1.2 below the main roof: reachable by jump from the bridge end, traded for height disadvantage. A penthouse block breaks sightlines on it.
- The 4-unit alley between the roofs has no floor: falling into it is a blast death.
- Four ordered spawn markers: two on the main roof's clear south quadrants, two on the second roof, supporting 2-player and 4-player matches without a stage-specific capacity rule.
- Side blast boundaries re-derive from the twin-roof geometry; the brief does not override those values.
- No hazards, moving geometry, decorative colliders, or client-owned gameplay behavior.

## Readability and balance intent

This is a mixed-intent connected-roofs arena. The HVAC deck supplies the main floor's positional choice without crowding it; the bridge is a narrow, unmissable commitment; the second roof trades height for safety. The playable silhouette must read from all four quarter-turn camera vantages, with the city closing every horizon so no yaw looks into void. Sightlines stay clear across the main floor, along the bridge, and around the penthouse block.

## Presentation concept

Visual composition is owned by the locked Stage Design Brief referenced in front matter (`docs/design/stages/industrial_rooftop.design.md`). The current prefab state (three-pass visual result of 2026-09-02) remains the visual baseline. The stage still requires the pending external normal PVP review at 2 and 4 players before acceptance.

## Production contract

The collision scene uses the fixed `Stage_industrial_rooftop/GameplayGeometry`, `SpawnPoints`, and `AuthoringAids` hierarchy. The bake command validates the hierarchy, static mesh sources, four ordered tagged spawns, and derived arena data before writing the `.arena` file. The inspect command validates the source-to-baked-to-prefab relationship, cosmetic references, zero colliders, local-light ownership, and deterministic overlay captures.

Asset provenance is the accepted local industrial-rooftop workset and inspection evidence listed in the front matter. Those files are selection evidence, not runtime configuration. The stage is not accepted until automated preflight succeeds and a human completes the external 2-player and 4-player PVP review, including spawns, routes, edges, ledge recovery, blast boundaries, and combat space.
