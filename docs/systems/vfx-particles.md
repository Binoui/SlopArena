# VFX — Particle Systems

> Client-side visual feedback for combat events. All VFX are Unity built-in Particle Systems
> (Shuriken) spawned from prefabs. No VFX Graph (Phase 2+).

---

## Architecture

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

---

## HitSpark Prefab

**Path:** `Assets/Prefabs/VFX/HitSpark.prefab`
**Material:** `Assets/Art/Materials/HitSpark.mat` (URP Particles/Unlit, Additive blending)

### Particle Module Configuration

| Module | Property | Value |
|--------|----------|-------|
| **Main** | Duration | 0.5s |
| | Start Lifetime | 0.4s (constant) |
| | Start Speed | 3 (constant) |
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

## Adding a New Particle VFX

1. **Create the prefab:** Build a ParticleSystem in the scene → configure modules → save as prefab in `Assets/Prefabs/VFX/`
2. **Wire combat feedback:** Assign prefab reference to CombatFeedback's serialized field (or extend CombatFeedback if new trigger type)
3. **Create material:** Save in `Assets/Art/Materials/` using URP Particles/Unlit with Additive blending for glow effects
4. **Commit:** Prefab + material + scene changes

---

## Current State

| VFX | Status | File |
|-----|--------|------|
| Hit sparks | ✅ Done | `HitSpark.prefab` + `CombatFeedback.cs` |
| Dash trail | ❌ TODO | — |
| Landing dust | ❌ TODO | — |
| Manki flamethrower | ❌ TODO | Future: `SpellVFXManager.cs` |
| Manki bomb explosion | ❌ TODO | Future: `SpellVFXManager.cs` |
| Manki aerosol flame | ❌ TODO | Future: `SpellVFXManager.cs` |
| FightGuy spell VFX | ❌ TODO | Future |

---

## References

- `CombatFeedback.cs` — `client/Unity/Assets/Scripts/Runtime/Combat/CombatFeedback.cs`
- HitSpark material — `Assets/Art/Materials/HitSpark.mat`
- HitSpark prefab — `Assets/Prefabs/VFX/HitSpark.prefab`
- Design spec — `docs/superpowers/specs/2026-07-19-hit-spark-vfx-design.md`
