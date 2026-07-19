# HitSpark VFX — Phase 1 Design

**Date:** 2026-07-19
**Status:** Design approved, pending implementation

## Goal

Replace the placeholder `HitSpark_Placeholder` prefab with a proper burst particle effect that gives satisfying visual feedback when attacks connect.

## Current State

- `HitSpark_Placeholder.prefab` — bare Unity ParticleSystem (Shuriken), 10 yellow particles, 0.3s life, cone shape
- `CombatFeedback.cs` — reads `_sim.LastTickHits` each FixedUpdate, spawns the prefab at target position, destroys after `_sparkLifetime`
- `TrainingMatch.cs` — has `_combatFeedback` field wired to `CombatFeedback` in scene
- URP pipeline (not HDRP), VFX Graph package available but unused

## Scope

This phase covers ONE effect: **enhanced hit spark burst** using the built-in Unity Particle System (Shuriken).

**Out of scope:**
- VFX Graph (Phase 2+)
- Per-character colored sparks
- Directional alignment to attack angle
- Object pooling (not needed at training-match scale)
- Movement VFX / ability VFX (separate phases)

## Design

### Prefab: HitSpark.prefab

Replaces `HitSpark_Placeholder.prefab` in `Assets/Prefabs/VFX/`.

One `ParticleSystem` component, non-looping, Play On Awake.

### Particle Module Configuration

| Module | Setting | Value |
|--------|---------|-------|
| **Main** | Duration | 0.5s |
| | Start Lifetime | 0.4s (constant) |
| | Start Speed | 3 → 0 (curve: linear ramp down) |
| | Start Size | 0.3 (constant) |
| | Max Particles | 50 |
| | Looping | false |
| | Play On Awake | true |
| **Emission** | Rate over Time | 0 (no continuous emission) |
| | Burst | 1 burst, 35 particles, 0s delay |
| **Shape** | Shape | Cone |
| | Angle | 90° (wide hemisphere-like spray) |
| | Radius | 0.1 |
| **Size over Lifetime** | Size | 1 → 0.05 (curve: hold 0.1s then shrink) |
| **Color over Lifetime** | Color | `#FFD700` (gold) → `#FF4500` (orange-red) → transparent alpha=0 |
| **Renderer** | Render Mode | Billboard |
| | Material | Default-Particle (URP) or existing particle material |
| | Sort Mode | By Distance |

### Curve Details

- **Speed curve**: starts at 3, holds for 0.05s, then linear to 0 at 0.4s — particles shoot out then decelerate
- **Size curve**: holds at 1x (0.3 units) for first 0.1s, then smooth curve down to 0.05x at end of lifetime — gives a "pop" feel
- **Color gradient**: gold (#FFD700) at birth → orange (#FF4500) at 40% life → transparent (alpha 0) at 100% — warm flash that fades

### Integration

`CombatFeedback.cs` needs no changes — just re-assign the `_hitSparkPrefab` field to the new prefab:

- `TrainingMatch` — update serialized reference in scene
- `PvPMatch` — ensure `CombatFeedback` exists and is wired (check if it already is)

### Learning Objectives

The beginner will learn:
1. Unity Particle System inspector layout (modules panel)
2. Curve editing (default vs custom curves)
3. Gradient editor (color keys + alpha keys)
4. Shape configuration (cone angle, radius)
5. Burst emission (rate vs burst)
6. Testing in play mode (TrainingMatch)

## Implementation Plan

1. Open the existing `HitSpark_Placeholder.prefab` in Unity's Particle System inspector
2. Configure each module per the table above
3. Test in TrainingMatch (hit NPC, observe the burst)
4. Tweak curves/gradients until it looks good
5. Rename prefab to `HitSpark.prefab`
6. Update serialized references in TrainingMatch scene
7. Confirm PvPMatch also wires CombatFeedback

## Future Phases

| Phase | Topic | Tech |
|-------|-------|------|
| 2 | Movement VFX (dash trail, landing dust) | Shuriken + Trails |
| 3 | Manki ability VFX (flame, bomb explosion) | VFX Graph |
| 4 | Per-character spark colors | ScriptableObject config |
| 5 | Object pooling | Unity Pooling API |
