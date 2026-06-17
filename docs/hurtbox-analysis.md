# Hurtbox / Visual Model Alignment Analysis

## The Core Problem

Character hurtboxes (server-side, from baked data) don't align with the visual model (client-side, from Godot skeleton). The user sees hurtboxes floating above or below the rendered character.

## Architecture

```
Server (pure C#)                              Client (Godot)
─────────────────                              ──────────────
CharacterDefinition                            CharacterDefinition
  - CapsuleHeight, CapsuleRadius                 - same
  - HurtboxBoneScale (baked→world)              - VisualScale (GLB→world)
  - ModelSoleOffset                             - ModelSoleOffset
  - ModelYOffset (computed or manual)           - AutoModelYOffset + ComputeModelYOffset()

BakedAnimationData (.bin)                       GLB file imported in Godot
  - Bone positions per frame                     - Skeleton3D with bones
  - Recorded at 60fps via headless_bake.gd       - Bone transforms via Godot scene tree
  - Uses get_bone_global_pose()                  - Bones rendered through Skeleton3D → Armature → GLB instance hierarchy

ServerSimulation.Tick():                        PlayerModel.Load():
  for each bone:                                  modelNode.Scale = VisualScale
    bx, by, bz = baked.GetBonePosition(...)       modelNode.Position.Y = ComputeModelYOffset()
    bx *= HurtboxBoneScale                       
    wy = state.PY + by                           // Visual foot ends up at:
                                                 // bodyY + modelYOffset + (lowestBone × VisualScale)
```

## The Transform Chain Problem

The baked data records `skel.get_bone_global_pose(boneIdx).origin` — this is the bone position in **skeleton-relative space** (inside the Skeleton3D node).

But the visual skeleton's world position goes through:

```
body (CharacterBody3D)
  └── PlayerModel (Node3D, Position.Y = modelYOffset, Scale = VisualScale)
       └── GLB instance root
            └── Armature (may have position/rotation)
                 └── Skeleton3D (may have position)
                      └── bones (skeleton-relative)
```

The hidden nodes (GLB instance root, Armature, Skeleton3D) can have their own transforms. These are NOT captured in the baked data. So `bakedHipsY × VisualScale` does NOT equal the visual Hips world position.

**Proof from Manki (working):**
```
Body Y = 6.48, modelYOffset = -0.5862, VisualScale = 1.0
bakedHipsY = -3.99

If baked → visual was 1:1: Hips = 6.48 + (-0.5862) + (-3.99) = 1.90
Actual visual Hips (from capsule debug): 6.40
Difference: 4.50m — from the GLB internal node chain
```

## Current State (after all fixes)

### What works:
- Manki: visual and hurtboxes aligned ✅
- Bunny visual: model now at correct height (tpose fix + VisualScale=0.022) ✅
- Bunny hurtboxes (client): follow skeleton ✅
- Server TimeScale: tick → baked frame mapping using DurationTicks ✅
- NPC char def: NPCs now use their own CharacterDefinition in simulation ✅

### What's still wrong:
- Bunny server hurtboxes: still floating (because server doesn't replicate the modelYOffset)

### The offset formula (already working on client):

```csharp
// ComputeModelYOffset():
float lowestY = scan baked "tpose" frame 0 for minimum bone Y
float footWorldY = lowestY * HurtboxBoneScale
modelYOffset = -(footWorldY + capsuleHalf + soleOffset)
// = -(lowestBoneY * HurtboxBoneScale + CapsuleHeight/2 + ModelSoleOffset)
```

This gives the visual model's Position.Y relative to the body. The server hurtbox needs the same shift to align.

## Approaches Tried

### 1. Server soleOffset (current)
Add `-ModelSoleOffset` to server hurtbox Y.
- ❌ Only works if soleOffset matches exactly between characters
- ❌ Manki's soleOffset=0.47 is a large hack value, not real sole thickness
- ❌ Broke Manki (hurtboxes went underground)

### 2. HipsWorldY approach (proposed, not implemented)
Declare a per-character HipsWorldY and compute everything relative to Hips.
- ⚠️ Blocked: baked Hips Y ≠ visual Hips Y due to GLB internal node chain
- Would require knowing the GLB hierarchy offsets (different per export tool)

### 3. Direct modelYOffset on server (recommended)
Store the pre-computed modelYOffset in CharacterDefinition and add it to server hurtbox Y.
- ✅ Simple — already have ModelYOffset field
- ✅ Same offset on both sides
- ✅ No baked data scanning on server
- ✅ Works regardless of GLB internal transforms

## Recommended Fix

```csharp
// CharacterDefinition already has: public float ModelYOffset; // default 0

// Client already computes it (ComputeModelYOffset) and now stores it

// For characters that use AutoModelYOffset = true:
// The computed value is printed at startup, then copied into the definition.

// On server (ServerSimulation.cs):
float wy = py + by + def.ModelYOffset;  // instead of py + by + soleY

// On client (PlayerModel.cs):
// Already: pm.Position = new Vector3(0, ComputeModelYOffset(), 0)
```

### To make it fully data-driven:
1. Pre-compute ModelYOffset per character (it's already printed in the log)
2. Set the value in BunnyData.cs and MankiData.cs
3. Set AutoModelYOffset = false
4. Remove ModelSoleOffset from CharacterDefinition (no longer needed)
5. Server uses def.ModelYOffset directly — no computation needed

### Values:
| Character | CapsuleHalf | Hips (baked) | Foot (baked) | VisualScale/HurtboxBoneScale | SoleOffset | ModelYOffset |
|-----------|-------------|--------------|--------------|------------------------------|------------|--------------|
| Manki     | 0.65        | -3.99        | -53.38       | 1.0 / 0.01                  | 0.47       | -0.5862      |
| Bunny     | 0.75        | 0.00         | -39.21       | 0.022 / 0.022               | 0.35       | +0.1342*     |

*Note: Bunny's ModelYOffset with tpose baked data and soleOffset=0 was previously computed. The user adjusted soleOffset to make the model touch the ground, which changes this value. The current log shows `offset=-0.7079` with soleOffset=0.35 and HurtboxBoneScale=0.01.

### Log note about scale mismatch in last run:
```
[Model] Loading Bunny: scale=(1, 1, 1)  ← user removed VisualScale, was giant
[ModelY] Auto: lowest=-39.2102 scale=0.01 footY=-0.3921
```
HurtboxBoneScale was 0.01 (not yet updated to 0.022). The fix: set both VisualScale=0.022 and HurtboxBoneScale=0.022.
