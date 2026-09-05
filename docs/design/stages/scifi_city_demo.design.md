---
key: scifi_city_demo
status: locked
locked: 2026-09-03
approved_by: user
blockout:
  - .stage-authoring-cache/scifi_city_demo/design/lock-default.png
---

# SciFi City Demo Design Brief

## Personality
A compact neon rooftop fight is carved from a complete living sci-fi city rather than assembled in empty space.

## Composition decisions

1. `Assets/LowPolySciFiCity/Scenes/LP_SciFiCity_DEMO.unity` is the visual world source and remains vendor-owned and unmodified.
2. The initial gameplay shell is exactly the 19 GameObjects currently tagged `Floor` in the source scene's central multilevel rooftop cluster; that selection includes roof pieces, stairs, and the existing bridge run.
3. The selected floor cluster is the visual and gameplay focus; surrounding source-scene geometry supplies the close city, distant skyline, and vertical context without a new city-composition pass.
4. Preserve the source scene's native URP materials, neon emissives, city lighting, and atmosphere for this first integration demo.
5. This is an integration proof: the user will deliberately revise the tagged floor selection after the first playable result rather than require the initial shell to match Industrial Rooftop.

## Negative decisions

- No mutation of vendor source scenes or assets.
- No new asset-selection or custom city-construction pass.
- No authoritative collision from vendor colliders, scripts, cameras, or lights.
- No attempt to reproduce the previous Twin Roofs layout.

## Palette & lighting

Use the source scene's dark neon sci-fi palette: deep shadowed architecture with cyan, magenta, and red emissive accents.

## Camera vantages

- Main floor across the central rooftop cluster.
- The existing bridge in both directions.
- Stairs and upper/lower floor relationships.
- Recovery and death-space views toward the source city's skyline.
