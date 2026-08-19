# VFX — Combat Presentation

> Client-side presentation driven by accepted server-simulation events. Shared impact
> meshes communicate gameplay strength; particle trails and future character effects add flavor.

---

## Architecture

### Shared impact grammar

```
ServerSimulation.LastTickHits
         │ accepted hits: contact, direction, damage, force, hitstop
         ▼
ISimulationBridge.LastTickHits
         │
         ▼
CombatFeedback.OnTick()
         │ classify Light / Medium / Heavy / Launch
         ▼
GraphicHitEffect.Spawn()
         └─ pooled GameObject + one dynamic mesh + one shared material
```

**Key files:**

- `client/Unity/Assets/Scripts/Runtime/Combat/CombatFeedback.cs`
- `client/Unity/Assets/Scripts/Runtime/Combat/GraphicHitEffect.cs`
- `src/Shared/SpellResolver.cs`

Training and PvP call the same `CombatFeedback` path after advancing their simulation bridge.
The rollback bridge snapshots current-tick hits before reconciliation can replay local history.
Ignored invincibility contacts and countered contacts are not emitted as accepted hit events.

### Bone Trail VFX

```
AbilitySpec.BoneTrails[]    ← per-ability data (BoneTrailDef struct)
         │
         ▼
  PlayerRenderer.UpdateAnimationState()
         │
         ├─ detects attack start → reads BoneTrails from current ability spec
         ├─ GetOrCreateBoneTrail(boneName) → instantiates/caches trail as child of bone transform
         ├─ sets startColor + startSize from BoneTrailDef
         ├─ enables emission (ParticleSystem.emission.enabled = true)
         │
         ├─ on attack end → DisableAllTrails()
         └─ on respawn → destroys all trail GameObjects
```

**Key file:** `client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs`

- One reusable `BoneTrail.prefab` instanced per bone per character
- Cached in `Dictionary<string, ParticleSystem>` — zero per-frame allocation
- World simulation space: particles freeze in place, tracing the bone's motion arc
- Additive blending, short lifetime (0.1-0.3s), emission disabled by default
- Trails are toggled on/off via emission module, not created/destroyed per swing

---

## Shared Impact Tiers

| Tier | Classification | Shape |
|------|----------------|-------|
| Light | Damage below 6 and launch force below 12 | Compact ring, four short rays, small incoming stroke |
| Medium | Damage 6–10 and launch force below 12 | Larger ring, six rays, directional wedge |
| Heavy | Damage 11+ and launch force below 12 | Double core, nine rays, thick directional stroke |
| Launch | Final launch force 12+ | Double ring, eleven rays, long launch-direction streaks |

`SpellResolver.HitResult` carries the collision point. `ServerSimulation` adds the final
attacker-to-target direction, post-hook damage, launch magnitude, and applied hitstop only
after the hit passes invincibility and counter interception.

`GraphicHitEffect` prewarms 16 instances. Every active impact uses one `MeshRenderer`, one
reused `Mesh`, and the same material. Vertex geometry and colors are rebuilt in cached lists;
no child strokes or per-hit materials are allocated. The effect holds through hitstop, then
expands and fades using unscaled time.

The shared tier is deliberately character-neutral. Manki smoke/fire, Kistu blade fragments,
FightGuy ki, and Nilus void effects must layer over it without changing its gameplay meaning.

## BoneTrail Prefab

**Path:** `Assets/Resources/Prefabs/VFX/BoneTrail.prefab`
**Material:** Reuses `Assets/Art/Materials/HitSpark.mat` (URP Particles/Unlit, Additive blending)

### Particle Module Configuration

| Module | Property | Value |
|--------|----------|-------|
| **Main** | Duration | 1s (continuous) |
| | Looping | true |
| | Start Lifetime | Random 0.1-0.3s |
| | Start Speed | 0 |
| | Start Size | 0.15 (overridden by BoneTrailDef.Width at runtime) |
| | Max Particles | 200 |
| | Simulation Space | World |
| | Play On Awake | false |
| **Emission** | Rate over Time | 120 |
| **Size over Lifetime** | Enabled | true |
| | Curve | 1.0 @ birth → 0.0 @ death |
| **Color over Lifetime** | Enabled | true |
| | Gradient | White (full alpha) → White (transparent), color from BoneTrailDef at runtime |
| **Renderer** | Render Mode | Billboard |
| | Material | HitSpark (URP Particles/Unlit, Additive) |
| **Shape** | Enabled | false (point emission from bone) |

### Behavior

- Prefab is instantiated at runtime as a child of the weapon bone (e.g. `mixamorig:RightHand`)
- Particles emit from the bone position and freeze in world space — the moving bone leaves a stationary trail behind it
- Short lifetime (0.1-0.3s) creates a tight arc, not a long smear
- Rate 120/sec = 2 particles per frame at 60fps
- Color and size set dynamically per ability from `BoneTrailDef`
- Same Additive blending + soft circle texture as HitSpark

### Data Declaration

`BoneTrailDef` struct in `src/Shared/AbilitySpec.cs`:
| Field | Type | Description |
|-------|------|-------------|
| `BoneName` | string | Skeleton bone name (e.g. `mixamorig:RightHand`) |
| `Width` | float | Particle size in meters |
| `R, G, B, A` | float | Trail color (RGBA) |

Per-ability data on `AbilitySpec`:
```csharp
public BoneTrailDef[]? BoneTrails;
```

### Adding a Trail to an Ability

In the character's data file (e.g. `FightGuyData.cs`), add to any ability spec:
```csharp
BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } },
```
No code changes needed — PlayerRenderer picks it up automatically.

---

## Adding Character-Specific VFX

1. Keep `CombatFeedback` tier classification unchanged.
2. Add a character/move presentation layer driven by the accepted hit owner and active slot.
3. Pool reusable effects and share materials.
4. Align directional effects to `HitResult.DirX`, `DirZ`, and `KnockbackAngle`.
5. Keep ordinary effects inside the shared tier's screen coverage; major launch and KO effects
   may briefly exceed it.

---

## Current VFX State

| VFX | Status | File |
|-----|--------|------|
| Shared impact tiers | Implemented | `GraphicHitEffect.cs` + `CombatFeedback.cs` |
| Bone trails | Implemented | `BoneTrail.prefab` + `PlayerRenderer.cs` |
| Character-specific hit layers | Not implemented | — |

### Authored bone trails

| Character | Ability | Bone | Color | Status |
|-----------|---------|------|-------|--------|
| FightGuy | Authored attack stages | Stage-defined | Blue | Implemented |
| Manki | Authored attack stages | Stage-defined | Orange | Implemented |

### Future

| VFX | Status |
|-----|--------|
| Dash start and trail | Not implemented |
| Jump and landing dust | Not implemented |
| Manki flame and explosions | Not implemented |
| Kistu blade fragments and glints | Not implemented |
| FightGuy ki layer | Not implemented |
| Nilus void layer | Not implemented |
| KO and respawn presentation | Not implemented |

---

## References

- `CombatFeedback.cs` — `client/Unity/Assets/Scripts/Runtime/Combat/CombatFeedback.cs`
- `GraphicHitEffect.cs` — `client/Unity/Assets/Scripts/Runtime/Combat/GraphicHitEffect.cs`
- `PlayerRenderer.cs` — `client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs`
- BoneTrail prefab — `Assets/Resources/VFX/BoneTrail.prefab`
- `BoneTrailDef` struct — `src/Shared/AttackData.cs`
- Historical prototype spec — `docs/superpowers/specs/2026-07-19-hit-spark-vfx-design.md`
