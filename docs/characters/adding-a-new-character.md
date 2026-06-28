# Adding a New Character — Full Pipeline

> SlopArena — Unity 6 C# (main branch)
> Last updated: June 2026
---

## Overview

Adding a new character involves **5 domains**:

1. **Data** — Define the character in `CharacterDefinition` (stats, abilities, animations)
2. **Code** — Implement special effects (if abilities need custom logic beyond the data)
3. **Model** — GLB from Mixamo / Blender / professional animator
4. **Bake** — Run `BakeSkeletonTool` to generate skeleton .bin for hurtbox positions
5. **Icon** — UI icon for class select (placeholder for now)

Below is the step-by-step process for each.

---

## 1. Data — `Shared/CharacterDefinition.cs`

### 1a. Add to CharacterClass enum

```csharp
public enum CharacterClass : byte
{
    Manki,
    YourNewClass   // ← add here
}
```

> The enum values are used as array indices in `CharacterRegistry.All[]`.
> Always append at the end to avoid breaking existing indices.

### 1b. Build the character definition

Add a private static method in `CharacterRegistry` (same file), then add it to the `BuildRegistry()` array:

```csharp
return new CharacterDefinition[]
{
    BuildManki(),      // index 0
    BuildYourClass(),  // index 1  ← add here
};
```

### 1c. Structure of a Build method

```csharp
private static CharacterDefinition BuildYourClass()
{
    return new CharacterDefinition
    {
        Class = CharacterClass.YourNewClass,
        DisplayName = "Your Name",

        // ═══════ MOVEMENT ═══════
        Movement = new MovementStats
        {
            WalkSpeed = 10f,           // Manki=11 (medium-fast)
            SprintSpeed = 14f,         // 12-15 range
            DashSpeed = 32f,           // 30-35
            AirAcceleration = 14f,
            JumpForce = 16f,
            Gravity = 38f,             // 35-42
            DashDurationTicks = 10,     // ~167ms
            DashCooldownTicks = 58,     // ~1s
            GroundFriction = 18f,
            AirFriction = 0.45f,
            MaxFallSpeed = 52f,
            MaxJumps = 2,
        },

        // ═══════ ABILITIES ═══════
        // Each slot: LMB, AirLMB, RMB, AirRMB, Q, E, R, F

        LMB = new AbilitySpec
        {
            Name = "My Combo",
            CooldownTicks = 0,         // 0 = no cooldown (basic attacks)
            Stages = new AttackStage[]
            {
                new()
                {
                    Shape = AttackShape.MeleeCone,
                    Damage = 6f,
                    Range = 2.8f,          // Cone range in meters
                    HitAngleDeg = 45f,     // Cone half-angle
                    Radius = 0f,           // Only used for CircleAOE/Projectile
                    KnockbackForce = 3f,
                    KnockbackUpward = 2f,
                    LungeForce = 12f,      // Forward burst
                    StunTicks = 12,        // Hitstun in 16.6ms ticks
                    DurationTicks = 8,      // Total animation lock
                    ChainWindowTicks = 42, // 0 = last hit, use for combo chain window
                },
                // ... more stages for multi-hit combos
            },
            // Charged variant (hold RMB):
            ChargedStages = new AttackStage[] { ... },
            ChargeHoldTicks = 18,          // ticks before charge fires

            // Animation names matching FBX files:
            AnimationNames = new[] { "great_sword_slash", "great_sword_spin" },

            // Special effects (keys registered in AbilityRegistry):
            SpecialEffectKeys = new[] { "YourClassEffectName" },
        },

        // Air LMB — separate ability, used when airborne
        AirLMB = new AbilitySpec
        {
            Name = "Rising Slash",
            CooldownTicks = 0,
            Stages = new AttackStage[]
            {
                new() { Shape = AttackShape.MeleeCone, Damage = 6f, Range = 3f, HitAngleDeg = 50f, KnockbackForce = 8f, KnockbackUpward = 8f, LungeForce = 10f, StunTicks = 14, DurationTicks = 8, ChainWindowTicks = 0 },
            }
        },

        // Air RMB — downward spike
        AirRMB = new AbilitySpec
        {
            Name = "Aerial Slam",
            CooldownTicks = 0,
            Stages = new AttackStage[]
            {
                new() { Shape = AttackShape.MeleeCone, Damage = 8f, Range = 3.5f, HitAngleDeg = 40f, KnockbackForce = 15f, KnockbackUpward = -8f, LungeForce = 15f, StunTicks = 16, DurationTicks = 10, ChainWindowTicks = 0 },
            }
        },

        Q = new AbilitySpec { ... },
        E = new AbilitySpec { ... },
        R = new AbilitySpec { ... },
        F = new AbilitySpec { ... },   // Ultimate
    };
}
```

### AttackStage fields reference

| Field | Type | Used for | Notes |
|-------|------|----------|-------|
| `Shape` | enum | Hitbox type | `MeleeCone`, `CircleAOE`, `Projectile`, `Beam`, `SelfBuff` |
| `Damage` | float | Raw damage | Added to target's DamagePercent |
| `Range` | float | Cone range / projectile distance | Meters |
| `HitAngleDeg` | float | Cone half-angle | 45° = 90° total cone |
| `Radius` | float | Circle AoE radius / projectile hitbox | Used when Shape != MeleeCone |
| `KnockbackForce` | float | Horizontal knockback | Scaled by target's damage% |
| `KnockbackUpward` | float | Vertical knockback | Negative = spike downward |
| `LungeForce` | float | Self-forward burst | Moves attacker forward |
| `StunTicks` | ushort | Hitstun duration on target | 1 tick = 16.6ms |
| `DurationTicks` | ushort | Total animation lock duration | Prevents other actions |
| `ChainWindowTicks` | ushort | Combo chain window | 0 = final/no chain |

---

## 2. Code — ServerAbility Implementation

If an ability needs per-tick logic beyond hitbox spawning (e.g., movement, projectiles, charging), create a ServerAbility subclass.

### 2a. Create the ServerAbility file

Create `Shared/Abilities/{CharacterName}{AbilityName}.cs`:

```csharp
using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Description of what this ability does.
    /// </summary>
    public sealed class YourClassAbilityName : ServerAbility
    {
        private ushort _chargeTicks;
        
        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            s.State = ActionState.Attacking;
            s.AnimLockTicks = (ushort)GetParam(def, "duration", 30f);
            s.AnimIndex = 0; // map to animation in CharacterDefinition
        }
        
        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            // Per-tick logic here
            if (s.AttackElapsedTicks >= s.AnimLockTicks)
                EndAbility(ref s);
        }
        
        public override void OnEnd(ref CharacterState s)
        {
            // Cleanup if needed (optional)
        }
    }
}
```

### 2b. Register in AbilityFactory

In `Shared/Abilities/AbilityFactory.cs`:

```csharp
public static ServerAbility CreateServer(byte typeId)
{
    return typeId switch
    {
        1 => new MankiLmbCombo(),
        2 => new MankiRoundBomb(),
        3 => new MankiAerosolFlame(),
        4 => new YourClassAbilityName(),  // ← Add your ability
        _ => throw new ArgumentException($"Unknown AbilityTypeId: {typeId}"),
    };
}
```

### 2c. Add to CharacterDefinition

In `Shared/Characters/YourClassData.cs`:

```csharp
LMB = new AbilitySpec
{
    Name = "Ability Name",
    AbilityTypeId = 4,  // matches AbilityFactory
    CooldownTicks = 0,
    Params = new() 
    { 
        ["duration"] = 30f,
        // ... other tunable params
    },
    Stages = new AttackStage[]
    {
        new() 
        { 
            DurationTicks = 30,
            HitboxEvents = new HitboxEvent[]
            {
                new HitboxEvent 
                { 
                    TriggerTick = 10, 
                    DurationTicks = 5, 
                    Radius = 1f, 
                    Damage = 8f,
                    // ... other hitbox properties
                }
            }
        }
    },
    AnimationNames = new[] { "animation_name" },
}
```

**For simple abilities without per-tick logic:**
If your ability is just hitbox spawning at fixed timings (no movement, no conditionals), you can use `AbilityTypeId = 0` and the old data-driven pattern still works. However, ServerAbility is recommended for all new abilities as it's more flexible.

See `docs/systems/ability-architecture.md` for complete ServerAbility pattern documentation.

---

## 3. Model & Animations — GLB Pipeline

### 3a. Character mesh

Any humanoid 3D model works — Mixamo handles rigging for most FBX files.
Sources for CC0/royalty-free models:

| Source | Licence | Notes |
|--------|---------|-------|
| Your own Blender model | Any | Best control over look |
| [Quaternius](https://quaternius.com) | CC0 | Consistent low-poly style |
| [Sketchfab](https://sketchfab.com) (CC0 filter) | Varies | Check per-model licence |
| [OpenGameArt](https://opengameart.org) | Varies | Filter by CC0 |
| Kenney | CC0 | Blocky style, works fine |

**Process:**
1. Upload your FBX to [Mixamo](https://www.mixamo.com)
2. Auto-rig with the Mixamo skeleton
3. Download as **GLB** with skin + animations
4. The skeleton mapping is handled automatically by `AnimationController`

### 3b. Animation files

For each ability slot, provide an animation. Mixamo is the easiest source (free, auto-rigged),
but any FBX animation with a compatible skeleton works.

**Required animations per character:**

| Slot | Type | Description |
|------|------|-------------|
| LMB stage 1 | Attack | First swing of basic combo |
| LMB stage 2 | Attack | Second swing |
| LMB stage 3 | Attack | Combo finisher (launcher) |
| Air LMB | Air attack | Upward swing (juggling) |
| RMB | Heavy attack | Big single hit |
| Air RMB | Air attack | Downward spike |
| Q / E / R | Special | Cast / strike / block |
| F (Ult) | Ultimate | Big impactful move |
| Idle | Locomotion | Standing breathing |
| Run | Locomotion | Forward movement |
| Jump / Fall | Locomotion | Air state

All animations use the **same Mixamo skeleton** (`mixamorig:Hips`, etc.).
Place FBX files in: `assets/characters/{YourClass}/`

### 3c. Animation naming convention

In `AbilitySpec.AnimationNames`, use the FBX animation name (without `.fbx`):

```csharp
AnimationNames = new[] { "sword_slash", "sword_spin", "sword_uppercut" }
```

The `AnimationController` loads animations by name from the character's animation library.
If `AnimationNames` is null/empty, it falls back to generic names (`attack_{slot}_{stage}`).

### 3d. GLB animation binding

With GLB, Godot's `AnimationPlayer` reads animation tracks by bone **path** (e.g., `mixamorig:Hips:rotation_quaternion`). The skeleton layout is baked into the GLB — no manual remapping needed.

**Key points:**
- Animations are **embedded in the GLB file** — no separate FBX files
- `AnimationNames` in `AbilitySpec` must match the animation names inside the GLB
- The `AnimationTree` root StateMachine state names must match `AnimationNames`
- Godot handles bone remapping between different skeletons automatically via `AnimationMixer` if needed

---

## 4. Bake — Skeleton .bin Generation

After importing your character's GLB and setting up animations, generate the pre-baked bone position file for runtime hurtbox positioning.

### 4a. Add BakedDataPath to CharacterDefinition

```csharp
BakedDataPath = "res://data/yourclass_skeleton.bin",
HurtboxBoneScale = 0.01f,  // 0.01 for Mixamo (cm→m), 1.0 for native meters
```

### 4b. Define which bones become hurtboxes

```csharp
HurtboxBoneDefs = new HurtboxBoneDef[]
{
    new("mixamorig:Head", 0, 0, 0, 0.25f),
    new("mixamorig:Spine2", 0, 0, 0, 0.3f),
    new("mixamorig:Hips", 0, 0, 0, 0.3f),
    new("mixamorig:RightHand", 0, 0, 0, 0.14f),
    new("mixamorig:LeftHand", 0, 0, 0, 0.14f),
    new("mixamorig:RightFoot", 0, 0, 0, 0.18f),
    new("mixamorig:LeftFoot", 0, 0, 0, 0.18f),
};
```

- **Bone names**: use Mixamo/Godot format (`mixamorig:Head` in definition, Godot converts to `mixamorig_Head`)
- **Offsets**: (OffX, OffY, OffZ) are additive adjustments to the baked bone position (usually 0)
- **Radius**: hurtbox sphere radius in meters
- Order must match the `BoneNames` array in `BakeSkeletonTool.cs`

### 4c. Run the bake tool

1. Open Godot editor
2. Open `tools/bake_skeleton.tscn`
3. Set `CharacterScenePath` to your character's .tscn
4. Set `OutputPath` to `res://data/yourclass_skeleton.bin`
5. Update the `BoneNames` array in `Scripts/Tools/BakeSkeletonTool.cs` to match your character's bones
6. Select the `BakeSkeletonTool` node → in the inspector, check **TriggerBake**
7. The console will print progress per animation

**What the tool does:**
- Loads your character scene (Skeleton3D + AnimationPlayer)
- For each animation, samples bone positions at 60fps
- Transforms positions into Hips local space via `AffineInverse()`
- Writes a compact binary file (≈270KB for 11 bones × 19 anims)

### 4d. Integration & visual alignment

After baking, the system auto-computes the visual model offset:

```csharp
AutoModelYOffset = true,       // compute from baked data
HurtboxBoneScale = 0.01f,      // Mixamo cm→m conversion
ModelSoleOffset = 0.47f,       // fine-tune so feet touch ground
```

The auto-offset scans all bones at idle frame 0, finds the lowest point (toe tip), and positions the model so it touches the capsule bottom. If the feet don't touch the ground, adjust `ModelSoleOffset`.

### 4e. Integration checklist

- [ ] `BakedDataPath` points to the generated .bin
- [ ] `HurtboxBoneScale` matches your GLB's unit system
- [ ] `HurtboxBoneDefs` bone names match the skeleton
- [ ] `.bin` file is committed to the repo (it's small and deterministic)
- [ ] The `.bin` is regenerated whenever animations change

---

## 5. Icon — UI

Icons are **not yet implemented** in the class select screen.
Currently `ClassSelectUI` shows class names and text descriptions only.

### Future implementation plan

When icon support is added:
- Add icon textures to `assets/ui/icons/{YourClass}.png`
- Reference in `ClassSelectUI` or a new `CharacterIconProvider`
- Icons should be 128×128 px, transparent background, showing character silhouette

### Until then

The class list in `ClassSelectUI` handles the `switch` mapping:

```csharp
case CharacterClass.Knight => "Knight", ...
```

Update this switch when adding a new class.

---

## Checklist — New Character

- [ ] `CharacterClass` enum — new entry appended
- [ ] `BuildRegistry()` — method added + registered in array
- [ ] `MovementStats` — tuned for the class role (slow/tank, fast/assassin, etc.)
- [ ] `LMB` — 3-hit combo with chain windows
- [ ] `AirLMB` — upward launch for juggling
- [ ] `RMB` — heavy attack (hold variant optional)
- [ ] `AirRMB` — downward spike
- [ ] `Q`, `E`, `R` — 3 utility/combat abilities
- [ ] `F` — ultimate ability
- [ ] Special effect code — if needed, created in `Scripts/Characters/{Name}/`
- [ ] `AbilityRegistry` — keys registered
- [ ] GLB file — in `assets/characters/{Name}/`
- [ ] Animations — all 8 slots + locomotion (idle/run/jump/fall)
- [ ] `AnimationNames` — set in each AbilitySpec
- [ ] Bake skeleton — run `BakeSkeletonTool` → check `.bin` in `data/`
- [ ] `BakedDataPath` + `HurtboxBoneScale` set in `CharacterDefinition`
- [ ] `ModelSoleOffset` adjusted so the model's feet touch the ground
- [ ] `ClassSelectUI` — add to class switch if needed
- [ ] Build & test — `dotnet build`, 0 errors

---

## Reference — Existing Characters

| Class | Weight | Speed | HP feel | Range | Special mechanic |
|-------|--------|-------|---------|-------|-----------------|
| Manki | Medium (1.0) | Fast (11) | Medium | Melee | Fire damage, aerial combos, inferno burst |
