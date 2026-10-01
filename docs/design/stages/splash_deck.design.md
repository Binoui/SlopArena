---
key: splash_deck
status: draft
approved_by: null
blockout: []
---

# Pool Stage Design Brief — Draft

## Personality

A timber-deck fight floats over one tropical sea, with a waterfront village, palm beach, and working harbor sharing its coastline.

## Composition decisions

1. Use `Assets/TropicalEnvironment/Scenes/TropicalEnvironment_Demo.unity` as the source for the surrounding world; leave the vendor scene unmodified. The current Splash Deck visual layout and collision shell are not constraints on the new design. Retain the existing `splash_deck` stage key unless a separate stage is explicitly requested.
2. Proposed fight premise: a broad static timber boardwalk over water is the clear main combat floor, with four usable spawns and an unmistakable outer edge. The existing two raised surfaces remain visually aligned to the unchanged baked collision during preview; decide whether each earns a genuine high-ground route or should be removed in the later gameplay-shell design. No obstacle or hazard is justified by the backdrop alone.
3. From the default gameplay camera, the middle distance behind the fight is the assembled waterfront village: the existing `HouseFloor_01/02` scene instances with their nested roofs, walls, stairs, and doors, backed by palms and distant rock silhouettes. Rooflines form the dominant landmark; do not reconstruct the village from isolated prop prefabs.
4. A grouped palm beach occupies a side third: existing `PalmTree_01–06`, rocks and shoreline vegetation as a whole visual region. It supplies an asymmetrical green silhouette without extending into the fight or implying a reachable path.
5. A grouped harbor occupies the opposite distant side: the existing `Pier_01–04`, `Ship_01`, and `Ship_02` scene arrangements. Show a pier line and one dominant sail silhouette; keep ships farther and visually quieter than the village and fighters.
6. The village, palm beach, and harbor form one loose, uneven U-shaped coastline around the wooden fight deck. A single ocean surface at y=−8 passes beneath the fight and meets all three source regions; their terrain-derived banks and the shared low coast connect rather than reading as separate cut-out islands. Rounded, irregular shorelines soften the joins, while the remaining direction stays open to ocean and sky through camera yaw.
7. Preserve the vendor region's internal placement and nested assemblies when forming each stage-owned group. The demo's `Prefabs` root has 875 direct children rather than three ready-made region roots, so selecting/grouping existing neighborhoods is a production step, not individual decorative placement or an asset-catalog search.
8. The shared water is intentionally inside the current player-death corridor at y=−8: fighters may visibly pass through it, with **no collider, physics response, or gameplay effect**. This visual-only exception is accepted for the design preview. Shoreline land, trees, ships, and any apparent solid support remain outside the gameplay and blast corridors; none may suggest a safe recovery route. Above the floor, roofs and crowns break the skyline; far away, ships and landforms suggest a populated coast.

## Negative decisions

- No dependency on the current Splash Deck pool-deck geometry or primitive art.
- No disconnected island cutouts, hard rectangular shoreline borders, deck-height decorative pool strips, resort umbrellas, or poolside furniture in the waterfront composition.
- No copying the 1000×1000 `Terrain_01` with its 30,542 tree instances wholesale simply to obtain a forest backdrop; any localized shoreline treatment must be chosen and verified deliberately.
- No entire 875-object demo-scene dump, catalog search, hand placement of small decorative props, ancient ruin, treasure-field or jungle-outpost subscene.
- No second hero landmark, crowded fight center, solid shore or prop in recovery/death space, decorative colliders, Unity-only hazards, or decorative surfaces that look safely landable. The y=−8 water alone is the accepted visual-only pass-through exception.
- No authoritative gameplay collision derived from vendor Terrain, colliders, ships, buildings, or water.

## Palette & lighting

Use the demo scene's warm afternoon timber and sand, deep palm greens, muted blue ocean, and open sky. The fight deck and both raised surfaces should read as harbor wood rather than a separate resort palette. Production may adapt lighting for fighter readability without inventing a new visual language.

## Camera vantages

- Four quarter-turn gameplay-camera views from the main floor, with the village, beach, harbor, and ocean directions read in turn.
- Floor edges and four spawn approaches; any elevated route only if separately accepted.
- Recovery and death-space views toward each group, checking for false landing surfaces and visible geometry crossings.

## Source references and open review

The demo's real art vocabulary is visible in `.stage-authoring-cache/splash_deck/demo-cameras/Camera4.png` (village), `Camera2.png` (palm beach), and `Camera1.png` (harbor). A temporary, collider-free 25×16 floor was rendered against the **unmodified complete demo scene** from four directions at `.stage-authoring-cache/splash_deck/source-composition/{north,east,south,west}.png`. The preview shows that the coastline, sailing ships, village and open ocean can frame a fight, but also that the untrimmed terrain and trees enter the proposed floor's death-space corridor and a nearby ship can dominate the frame. This is a source-position study, not the accepted stage composition, a finalized floor, or an in-game camera sign-off.

The maintainer approved a provisional **real-art-first visual review** instead of another placeholder backdrop. The local `Resources/Stages/splash_deck.prefab` contains a removable `Demo Backdrop Preview`: three grouped neighborhoods copied from the demo, 63 terrain-tree palms/bananas from the village region, source-height shoreline meshes with the vendor terrain textures, and a muted copy of the vendor water material. Their internal scene arrangements are preserved. The existing Splash Deck floor and two raised surfaces remain only as a temporary camera-review foreground; its baked gameplay arena and authoritative collision have **not** changed. A pre-preview prefab backup is at `.stage-authoring-cache/splash_deck/pre-real-art-preview.prefab`.

Current edit-mode evidence: `.stage-authoring-cache/splash_deck/higher-sea/{north,east,south,west,under-deck,top}.png`. The water was raised to y=−8 at the maintainer's request; all three bank feet meet it at y=−7.97. The harbor and beach were lifted with their source hierarchies; a continuous low coast joins the three source-height terrain regions in an open U, and their formerly square borders were rounded and varied. These views are a composition check, **not** an in-game camera approval.

A targeted Unity resource-load and geometry probe found zero colliders, missing meshes/materials, unsupported shaders, or land vertices in the current horizontal blast corridor; the ocean does cover the gameplay footprint at y=−8 by design. The demo source scene stayed clean and the tracked `.arena` stayed unchanged. The prefab contains timber-colored deck and raised visual shells, but their existing collision geometry remains the authoritative shell until a reviewed design decision and subsequent bake.

Before `status: locked`, review the connected water, harbor deck, and three surrounding source regions from the real game camera—including free yaw, both raised surfaces, and recovery. Confirm whether those raised surfaces have a gameplay purpose; otherwise redesign the authoritative shell as one clear floor after visual sign-off. Any later floor, platform, boundary, or spawn change requires a new source scene, bake, inspect, tests, and human PVP review; no vendor collider becomes gameplay geometry.
