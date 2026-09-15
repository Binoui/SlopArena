---
name: blender-character-polish
description: Safely polish existing textured, rigged characters through Blender MCP. Use for mesh, normals, texture, material, accessory, skin-weight and deformation cleanup; candidate comparisons and game-asset handoff. Not animation authoring or Unity package integration.
---

# Blender character polish

## Ownership
Preserve the character's approved identity, rig compatibility and gameplay contracts. Read the character handoff and current ticket first. Animation creation/import belongs to `sloparena-animation-authoring`; package integration belongs to `sloparena-character-workflow`. This skill owns Blender asset editing and visual evidence, not simulation or publishing.

## Inspect before editing
Read the available MCP tool schema before first use. Inventory the active file, candidate objects, parents, transforms, modifiers, rig/actions, UVs, weights, material slots and packed/external images. Record source paths and identify the approved baseline. Never infer that an object named Original contains the original source: comparisons may use the preceding iteration.

Distinguish geometry, split normals, painted texture and lighting defects. Inspect matching textured and clay views; clay alone cannot establish that a textured defect is fixed. Reuse existing materials and rig infrastructure before adding replacements.

## Bounded iteration
1. State the intended change, preserved features and non-goals; obtain approval.
2. Save a separate candidate. Never overwrite approved studies or Unity source during exploration.
3. Change the smallest surface that fixes the demonstrated problem. Do not globally weld UV seams, smooth intentional angular shapes, redesign the skeleton or add detail to hide defects.
4. Compare identical cameras, lighting, materials and poses. Label both baselines accurately.
5. Validate changed regions in motion. Use actual bound clips where available; otherwise document controlled poses and explicitly leave full animation verification open.
6. Deliver candidate path, textured comparisons, relevant motion evidence, checks performed and remaining defects. User approval promotes the next baseline.

## Evidence gates
- Geometry: inspect topology, winding, degenerate faces, normals and joins in the changed region.
- Skinning: inspect weights and extreme deformations; unchanged weights do not by themselves prove good motion.
- Textures: inspect seams, stretching and transitions under matching lighting.
- Appearance: include front, side/three-quarter and gameplay-size views as appropriate. A Blender gameplay-size approximation is not Unity verification.
- Export: inventory material/texture cost, preserve bind pose/scale, and bake required procedural shading for the target shader. Keep the comparison scene separate from the shipping asset.
- Shipping: real Unity animation, attachment, lighting and gameplay-camera acceptance remains required through the existing project workflow.

## Handoff and durability
Record exact input/output paths, object roles, asset dependencies, approval and verification status. A local path in an issue is not an upload. Check tracking/ignore state and availability; report untracked assets explicitly. Never commit, push or upload without permission.

See [technical recipes](references/technical-recipes.md) for observed Blender/MCP pitfalls and tested algorithm patterns. These are recipes to adapt and verify, not universal repair scripts.
