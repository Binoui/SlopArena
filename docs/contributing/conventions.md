# SlopArena Art and Asset Conventions

This document covers visual direction, asset production, naming, licensing, and repository hygiene. Gameplay architecture belongs in [Architecture Overview](../architecture-overview.md), [Combat Systems](../systems/combat-systems.md), and [Ability Architecture](../systems/ability-architecture.md).

## Visual direction

The broader graphic identity for UI, menus, results, web, trailers, Workshop surfaces, and presentation copy is defined by the [SlopArena Visual Language](../design/visual-language.md). This document defines the complementary 3D character and asset conventions.

SlopArena uses Pixel8r2-style 3D pixel art:

- three-tone cell shading with hard highlight, midtone, and shadow bands;
- dark outlines and readable silhouettes;
- a limited, high-contrast palette with one signature color per fighter;
- flat matte materials rather than photorealistic PBR;
- enough visual exaggeration to read a move from a camera view.

The target is an arcade-fighter silhouette, not Roblox blockiness, generic low-poly cubes, realistic rendering, or chibi proportions.

## Character source assets

- Model in a clean T-pose for reliable rigging.
- Keep the mesh complete: no floating parts, embedded weapons, props, or particle effects.
- Use Unity VFX and bone-attached props for presentation elements.
- Prefer a Mixamo-compatible humanoid rig for ordinary fighters. Preserve the standard `mixamorig:` names when using that rig.
- Keep meshes economical; personality comes from silhouette, palette, pose, and motion more than polygon count.
- Fix orientation, root motion, and bone naming in the source asset. Do not hide a source transform mistake with runtime remapping.

The supported authoring formats and import settings are project/toolchain concerns. Check the current Unity package workflow before choosing FBX, GLB, or another source format.

## Animation naming

Use lowercase semantic IDs with dots for package identity, for example:

```text
anim.idle
anim.run
anim.jump
anim.fall
anim.dash
anim.hit-light
anim.attack-1
```

Package animation IDs are stable semantic references. The package asset catalog binds each ID to an imported clip and deterministic pose track. Do not make a filename, Unity path, vendor name, or generated catalog entry the gameplay identity.

Use clear movement, damage, and move names. Keep one semantic ID per meaning; renaming an ID is an explicit source-and-catalog refactor, not an informal string edit.

## Package asset ownership

New character assets are owned by the package under `client/Unity/Assets/CharacterPackages/<package>/` and are bound through `CharacterAssetCatalog.asset`. The Unity cook stage produces runtime bindings and pose data from the exact imported assets. Generated cooked output belongs under `content-cooked/` and is immutable for a match.

Runtime gameplay does not load raw authoring assets directly. Presentation resolves semantic IDs through generated package bindings and plays clips through Animancer.

## Third-party and licensed assets

Do not commit purchased or non-redistributable source assets. Keep local source under an ignored directory such as `/mnt/storage`, or import it from the valid original package. Commit only project-owned adaptations that the repository may legally redistribute.

Record required attribution and license metadata in the package manifest. A built-in or Workshop package must not expose a private vendor asset through a public path or capability ID. See the accepted content decisions in [`docs/README.md`](../README.md#accepted-adrs).

Optional Asset Store dependencies remain local and ignored. A fresh checkout must still compile when an optional visual dependency is absent.

## Readability and presentation

- Test silhouettes and move tells from the actual gameplay camera.
- Keep weapons, flames, particles, and cloth separate from the base mesh so they can be replaced or disabled cleanly.
- Prefer authored key poses and clear anticipation/recovery over extra detail.
- Keep visual effects client-only; do not encode gameplay in a material, particle system, or animation callback.

## Repository and commits

- Keep source filenames and semantic IDs consistent and descriptive.
- Do not commit generated Unity library state, local vendor assets, or transient reports unless a task explicitly makes an artifact canonical.
- Use one focused squash commit per branch and the repository's Conventional Commit format:

```text
<type>(<scope>): <imperative summary> (issue #N)
```

Use `feat`, `fix`, `refactor`, `docs`, `test`, or `chore` as appropriate. Explain the authoritative data path and verification in the pull request.

## References

- [Adding a Character](../characters/adding-a-new-character.md)
- [Character import checklist](../characters/character-import-checklist.md)
- [Character kit design principles](../characters/character-kit-design-principles.md)
- [Unity CLI](unity-cli.md)
