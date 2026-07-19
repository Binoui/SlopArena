# VFX — Particle Systems

> Client-side visual feedback for combat events. All VFX are Unity built-in Particle Systems
> (Shuriken) spawned from prefabs. No VFX Graph (Phase 2+).

---

## Architecture

### Hit Sparks

```
ServerSimulation.LastTickHits
         │
         ▼
  CombatFeedback.OnTick()    ← called each FixedUpdate after _localSim.Tick()
         │
         ├─ reads hit results (TargetEntityId, position)
         ├─ deduplicates per tick (HashSet<ulong> _alreadyTriggered)
         └─ Instantiate(_hitSparkPrefab, targetPos, identity) → Destroy(spark, lifetime)
```

**Key file:** `client/Unity/Assets/Scripts/Runtime/Combat/CombatFeedback.cs`

- Single MonoBehaviour attached to the `TrainingMatch` GameObject
- `SetSimulation(sim)` called on match start
- Spawns the prefab at the target's world position on each hit
- Prefab auto-destroys after `_sparkLifetime` (default 1s)
- No object pooling (not needed at training-match scale)

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

## HitSpark Prefab

**Path:** `Assets/Prefabs/VFX/HitSpark.prefab`
**Material:** `Assets/Art/Materials/HitSpark.mat` (URP Particles/Unlit, Additive blending)

### Particle Module Configuration

| Module | Property | Value |
|--------|----------|-------|
| **Main** | Duration | 0.5s |
| | Start Lifetime | 0.4s (constant) |
| | Start Speed | Random 2-7 |
| | Start Size | 0.3 (constant) |
| | Max Particles | 50 |
| | Looping | false |
| | Play On Awake | true |
| **Emission** | Rate over Time | 0 |
| | Burst | 1 burst, 35 particles, 0s delay |
| **Shape** | Type | Cone |
| | Angle | 90° |
| | Radius | 0.1 |
| | Emit from | Volume |
| **Size over Lifetime** | Enabled | true |
| | Curve | 1.0 @ birth → 0.9 @ 10% life → 0.3 @ 40% → 0.05 @ death |
| **Color over Lifetime** | Enabled | true |
| | Gradient | #FFD700 (gold) → #FF4500 (orange) → #8B0000 (dark red), alpha 1→0 |
| **Renderer** | Render Mode | Billboard |
| | Material | HitSpark (URP Particles/Unlit) |
| | Sort Mode | None |

### Behavior

- Spawned at the **hit entity's world position** (from `CharacterState.PX/PY/PZ`)
- Particles burst outward in a hemisphere-like cone (90° angle)
- Gold flash that fades to orange then transparent with size shrinking
- Additive blending makes sparks glow against dark/dim backgrounds
- One VFX instance per hit entity per tick (dedup by `TargetEntityId`)

---

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

## Adding a New Particle VFX

1. **Create the prefab:** Build a ParticleSystem in the scene → configure modules → save as prefab
2. **Wire combat feedback:** Assign prefab reference to CombatFeedback's serialized field (hit sparks)
3. **Or wire bone trail:** Add `BoneTrails` data to the ability spec in the character's data file
4. **Create material:** Save in `Assets/Art/Materials/` using URP Particles/Unlit with Additive blending
5. **Commit:** Prefab + material + code changes

---

## Current VFX State

### Hit Sparks

| VFX | Status | File |
|-----|--------|------|
| Hit sparks | ✅ Done | `HitSpark.prefab` + `CombatFeedback.cs` |

### Bone Trails (Active Abilities)

| Character | Ability | Bone | Color | Status |
|-----------|---------|------|-------|--------|
| FightGuy | LMB / AirLMB | `mixamorig:RightHand` | Blue (0.3,0.6,1.0) | ✅ Done |
| Manki | LMB / AirLMB | `mixamorig:RightHand` | Orange (1.0,0.6,0.0) | ✅ Done |

### Future

| VFX | Status | Notes |
|-----|--------|-------|
| Dash trail | ❌ TODO | — |
| Landing dust | ❌ TODO | — |
| Manki flamethrower | ❌ TODO | SpellVFXManager |
| Manki bomb explosion | ❌ TODO | SpellVFXManager |
| Manki aerosol flame | ❌ TODO | SpellVFXManager |
| FightGuy spell VFX | ❌ TODO | SpellVFXManager |

---

## References

- `CombatFeedback.cs` — `client/Unity/Assets/Scripts/Runtime/Combat/CombatFeedback.cs`
- `PlayerRenderer.cs` — `client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs`
- HitSpark material — `Assets/Art/Materials/HitSpark.mat`
- HitSpark prefab — `Assets/Prefabs/VFX/HitSpark.prefab`
- BoneTrail prefab — `Assets/Resources/Prefabs/VFX/BoneTrail.prefab`
- `BoneTrailDef` struct — `src/Shared/AbilitySpec.cs`
- Design spec — `docs/superpowers/specs/2026-07-19-hit-spark-vfx-design.md`
