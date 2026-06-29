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
            AnimIndex = 0; // set on ability instance (synced to CharacterState by ActivateAbility/TickAbilities)
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

## 3. Model & Animations — FBX Pipeline (Unity)

### 3a. Character mesh

Place the static mesh FBX at `client/Unity/Assets/Art/Characters/<name>/<name>.fbx`.
Set `importAnimation: OFF` in the FBX import settings — the mesh file contains
no animations.

### 3b. Animation FBX files

Each ability/locomotion slot gets a **separate FBX file** with one animation clip.
Mixamo is the easiest source (free, auto-rigged), but any FBX animation with a
compatible Mixamo skeleton works.

Place all animation FBX files in `Assets/Art/Characters/<name>/Animations/`.

| Slot | FBX file | Expected clip name |
|------|----------|-------------------|
| Idle | `Idle.fbx` | Idle |
| Run | `run.fbx` | run |
| Jump | `jump.fbx` | jump |
| Fall | `fall.fbx` | fall |
| LMB stage 1 | `spell_lmb_1.fbx` | spell_lmb_1 |
| LMB stage 2 | `spell_lmb_2.fbx` | spell_lmb_2 |
| LMB stage 3 | `spell_lmb_3.fbx` | spell_lmb_3 |
| RMB | `spell_rmb.fbx` | spell_rmb |
| Air RMB | `spell_air_rmb.fbx` | spell_air_rmb |
| Q | `spell_q.fbx` | spell_q |
| E | `spell_e.fbx` | spell_e |
| F (Ultimate) | `spell_f.fbx` | spell_f |

### 3c. Clip renaming (Mixamo workaround)

Mixamo FBX animations all have the internal clip name `mixamo.com`.
After importing, rename clips via Unity script:

```csharp
var importer = AssetImporter.GetAtPath(path) as ModelImporter;
var clips = new ModelImporterClipAnimation[1];
clips[0] = new ModelImporterClipAnimation();
clips[0].name = Path.GetFileNameWithoutExtension(path);
clips[0].takeName = "mixamo.com";
clips[0].firstFrame = 0;
clips[0].lastFrame = importer.defaultClipAnimations[0].lastFrame;
importer.clipAnimations = clips;
importer.SaveAndReimport();
```

### 3d. Animation naming in code

In `AbilitySpec.AnimationNames`, use the FBX clip name (the renamed clip,
not the original `mixamo.com`):

```csharp
AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" }
```

The `AssignClip()` method in `SlopArenaAnimatorGenerator.cs` maps clip names
to config slots. Ensure your clip name matches one of the case labels.

### 3e. Baked skeleton data (hurtboxes)

The `.bin` files at `data/<name>_skeleton.bin` are pre-baked per-character
(generated via the Godot-era bake tool). No Unity bake step is needed.

**If hurtboxes are misaligned:**
- Verify `HurtboxBoneScale` matches the baked data's export scale
- Verify `HurtboxBoneDefs` bone names match the skeleton
- Ensure the `.bin` file is non-empty and was generated from the correct rig
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
