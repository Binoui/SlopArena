# Adding a New Character — Full Pipeline

> SlopArena — Godot 4.6 C# (main branch)
> Last updated: June 2026

---

## Overview

Adding a new character involves **4 domains**:

1. **Data** — Define the character in `CharacterDefinition` (stats, abilities, animations)
2. **Code** — Implement special effects (if abilities need custom logic beyond the data)
3. **Model** — FBX from Kenney + Mixamo animations
4. **Icon** — UI icon for class select (placeholder for now)

Below is the step-by-step process for each.

---

## 1. Data — `Shared/CharacterDefinition.cs`

### 1a. Add to CharacterClass enum

```csharp
public enum CharacterClass : byte
{
    Vanguard,
    Wraith,
    Channeler,
    Knight,
    YourNewClass   // ← add here (keep Vanguard=0, sequential)
}
```

> The enum values are used as array indices in `CharacterRegistry.All[]`.
> Always append at the end to avoid breaking existing indices.

### 1b. Build the character definition

Add a private static method in `CharacterRegistry` (same file), then add it to the `BuildRegistry()` array:

```csharp
return new CharacterDefinition[]
{
    BuildVanguard(),   // index 0
    BuildWraith(),     // index 1
    BuildChanneler(),  // index 2
    BuildKnight(),     // index 3
    BuildYourClass(),  // index 4  ← add here
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
            WalkSpeed = 10f,           // Vanguard=9, Knight=10, Wraith=11
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

        LMB = new AbilityData
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
                    SelfLockTicks = 8,     // Self animation lock
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
        AirLMB = new AbilityData
        {
            Name = "Rising Slash",
            CooldownTicks = 0,
            Stages = new AttackStage[]
            {
                new() { Shape = AttackShape.MeleeCone, Damage = 6f, Range = 3f, HitAngleDeg = 50f, KnockbackForce = 8f, KnockbackUpward = 8f, LungeForce = 10f, StunTicks = 14, SelfLockTicks = 8, ChainWindowTicks = 0 },
            }
        },

        // Air RMB — downward spike
        AirRMB = new AbilityData
        {
            Name = "Aerial Slam",
            CooldownTicks = 0,
            Stages = new AttackStage[]
            {
                new() { Shape = AttackShape.MeleeCone, Damage = 8f, Range = 3.5f, HitAngleDeg = 40f, KnockbackForce = 15f, KnockbackUpward = -8f, LungeForce = 15f, StunTicks = 16, SelfLockTicks = 10, ChainWindowTicks = 0 },
            }
        },

        Q = new AbilityData { ... },
        E = new AbilityData { ... },
        R = new AbilityData { ... },
        F = new AbilityData { ... },   // Ultimate
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
| `SelfLockTicks` | ushort | Self animation lock duration | Prevents other actions |
| `ChainWindowTicks` | ushort | Combo chain window | 0 = final/no chain |

---

## 2. Code — Special Effects

If an ability needs logic beyond what AttackStage can express (e.g., teleport, delayed AoE, status application), create a special effect file.

### 2a. Create the abilities file

```
Scripts/Characters/{YourClass}/{YourClass}Abilities.cs
```

```csharp
#nullable enable
using Godot;

public static class YourClassAbilities
{
    public static void EffectName(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        Vector3 forward = combat.GetCameraForward();

        // Visual feedback
        StatusSpells.CreateCircleVisual(combat, pos, 5f, new Color(1f, 0.5f, 0f, 0.3f), 1f);
        StatusSpells.CreateImpactVisual(combat, pos, 3f, new Color(1f, 0.8f, 0f));

        // Hit detection
        var hits = combat.CheckMeleeCone(pos, forward, 3f, 45f, 8f, 15f, 5f);

        // Status effects
        combat.ApplyStatus(StatusType.Slowed, 3f, combat.GetEntityId());
        combat.ApplyStatusToLastHit(StatusType.Burn, 4f);
    }
}
```

Available CombatComponent API:

| Method | Purpose |
|--------|---------|
| `GetOwnerPosition()` | Get 3D position of the character |
| `GetCameraForward()` | Get camera-relative forward direction |
| `CheckMeleeCone(pos, dir, range, angle, dmg, kb, kbUp)` | Cone hit check → returns hit entity IDs |
| `CheckCircleHit(pos, radius, dmg, kb, kbUp)` | Circle AoE hit check |
| `ApplyStatus(type, duration, sourceId)` | Apply status to self |
| `ApplyStatusToLastHit(type, duration)` | Apply status to most recent hit targets |

### 2b. Register in AbilityRegistry

In `Scripts/Characters/AbilityRegistry.cs`:

```csharp
{ "YourClassEffectName", YourClassAbilities.EffectName },
```

The key string must match `SpecialEffectKeys` in your `CharacterDefinition`.

---

## 3. Model & Animations — FBX Pipeline

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
3. Download as **FBX Binary** with skin
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

In `AbilityData.AnimationNames`, use the FBX animation name (without `.fbx`):

```csharp
AnimationNames = new[] { "sword_slash", "sword_spin", "sword_uppercut" }
```

The `AnimationController` loads animations by name from the character's animation library.
If `AnimationNames` is null/empty, it falls back to generic names (`attack_{slot}_{stage}`).

### 3d. Animation path remapping

Mixamo uses path conventions like `RootNodeL/Skeleton:bone`.
KayKit models use `PlayerModel/Rig_Medium/Skeleton3D:bone`.
The system auto-remaps between them — see `AnimationController.DetectPrefixToStrip()`.

---

## 4. Icon — UI

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
- [ ] FBX files — in `assets/characters/{Name}/`
- [ ] Animations — all 8 slots + locomotion (idle/run/jump/fall)
- [ ] `AnimationNames` — set in each AbilityData
- [ ] `ClassSelectUI` — add to class switch if needed
- [ ] Build & test — `dotnet build`, 0 errors

---

## Reference — Existing Characters

| Class | Weight | Speed | HP feel | Range | Special mechanic |
|-------|--------|-------|---------|-------|-----------------|
| Vanguard | Heavy (1.3) | Slowest (9) | Tanky | Melee | Shield, buffs, delayed AoE |
| Wraith | Light (0.7) | Fastest (11) | Squishy | Mixed | Invisibility, poison, projectiles |
| Channeler | Medium (0.9) | Medium (10) | Medium | Ranged | Frost/Fire status, beam |
| Knight | Medium (1.0) | Medium (10) | Medium | Melee | Stun, parry, gap closer |
