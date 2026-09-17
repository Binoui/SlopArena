---
key: slop_pit
status: locked
locked: 2026-09-17
approved_by: project maintainer
blockout:
  - .stage-authoring-cache/slop_pit/design/three-quarter.png
  - .stage-authoring-cache/slop_pit/art-pass-05/gameplay.png
---

# Slop Pit Design Brief

## Acceptance

The maintainer accepted the playable layout and iterative visual passes, then approved the stage as good enough on 2026-09-17. This locks the current static design, not packaged-release readiness or external 2–4-player PVP acceptance. The initial playable blockout and art iterations proceeded under an explicit provisional exception to the design-lock gate.

## Personality

A scrapyard brawl happens over a cool factory void while the spectators' service galleries stay above the mess.

## Composition decisions

1. The broad 32×26 deck stays open through every camera yaw. Open edges expose recovery and knockout space; the center is not a corridor between props.
2. Equal 6×5 platforms occupy opposite diagonal corners, centered at (-10,-8) and (10,8), with tops 2 units above the deck. Preserve the accepted full-jump approaches and clear central fight.
3. Four ordered deck spawns remain at X/Z (-5,-4), (5,4), (-5,4), (5,-4), with marker-center Y=0.85. Blast boundaries derive from the static collision shell.
4. Muted, differently sized steel plates use staggered seams, two rust-toned edge replacements and five small repair patches. Sparse patch bolts and hazard-striped fascia provide industrial detail without visual noise across the center.
5. Four sparse two-level service galleries and connecting walkways wrap the horizon. Two static diagonal conveyors sit behind opposite galleries; simple Blender-authored panel, pipe and casing scraps dress the belts and the space below the kill plane.
6. The world is deliberately simple: cool distant enclosure, dark steel structure, warm galleries and broad warm deck-edge light pools. The cooler center and readable platform tops remain dominant. No giant sign or competing billboard.
7. This variant is static. Cosmetic geometry stays outside the blast corridor except the thin surface treatment aligned to the accepted shell. Moving lifts require a separate authoritative feature; never approximate them with Unity-only animation.

## Negative decisions

- No giant sign, dense crowds, dense scrap heap or unnecessary background detail.
- No props obstructing the deck, platform approaches or recovery routes.
- No decorative colliders or background geometry intersecting knockout space.
- No hazards or moving platforms in this variant.

## Palette & lighting

Subdued blue-gray distance, muted cool steel, limited rust/ochre repairs and warm gallery/deck-edge accents. Preserve fighter readability; do not return the background to featureless black or blow out platform tops.

## Camera vantages

- Four quarter-turn views from the main deck.
- Both platform tops looking across the center.
- Deck edges and low recovery views toward the sparse scrap field.
- Elevated views toward both conveyors and the surrounding galleries.
