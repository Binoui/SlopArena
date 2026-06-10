# SlopArena Hitbox/Hurtbox System — Architecture & Usage

## 🎯 Overview

SlopArena uses a **server-authoritative** system with **sphere-sphere pure math collision detection** (no Godot physics queries on the server).

### Key concepts

- **Hitbox** = attack zone (red in debug view)
- **Hurtbox** = entity vulnerable zone (blue in debug view)
- **Detection** = sphere-sphere collision check in `SpellResolver.Tick()` (Shared/)
- **Tick-based** = everything in ticks (60Hz), not floating point seconds

---

## 📁 File architecture

```
SlopArena/
├── Shared/
│   ├── Hitbox.cs              ← Pure C# struct (netcode-ready)
│   ├── SpellResolver.cs       ← Collision engine (pure math)
│   └── AttackData.cs          ← Attack definitions (stages)
│
├── Scripts/
│   ├── Combat/
│   │   ├── Hurtbox.cs         ← Area3D client-side (visual marker)
│   │   └── CombatComponent.cs ← Spawn hitboxes, apply damage
│   │
│   └── Debug/
│       └── DebugHitboxDraw.cs ← Wireframe spheres + labels (F3 to toggle)
```

---

## 🔴 Hitbox (attack)

### Definition — `Shared/Hitbox.cs`

```csharp
public struct Hitbox
{
    public float X, Y, Z;           // Absolute position (world space)
    public float VX, VY, VZ;        // Velocity (0,0,0 = static melee, ≠0 = projectile)
    public float Radius;            // Sphere radius
    public ushort DurationTicks;    // Lifetime in ticks (60Hz)
    public ushort AgeTicks;         // Current age (incremented each tick)

    public float Damage;            // Damage amount
    public float KnockbackForce;    // Horizontal knockback force
    public float KnockbackUpward;   // Vertical knockback force
    public ushort StunTicks;        // Stun duration in ticks
    public ulong OwnerId;           // Attacker's ID (to skip self-hit)

    public bool Active;             // false = hitbox destroyed (after impact or expiration)
}
```

### Spawning — Two methods

#### 1. Direct method (melee/cone attacks)

```csharp
// In CombatComponent.cs
public List<ulong> CheckMeleeCone(Vector3 origin, Vector3 forward, float range, ...)
{
    var hb = new Hitbox
    {
        X = origin.X + forward.X * range * 0.5f,
        Y = origin.Y + 1f,
        Z = origin.Z + forward.Z * range * 0.5f,
        Radius = range * 0.5f,
        DurationTicks = 5,          // ~83ms lifetime
        Damage = damage,
        KnockbackForce = knockbackForce,
        KnockbackUpward = knockbackUpward,
        OwnerId = _entityId,
    };
    SpellResolver.Spawn(hb);
    var results = SpellResolver.Tick(entities);
    // Process hits...
}
```

#### 2. Via `AttackStage` (combos/abilities)

```csharp
// In CharacterDefinition.cs — attack declaration
new AbilityData
{
    Name = "Basic Attack",
    Stages = new[]
    {
        new AttackStage
        {
            Damage = 20f,
            KnockbackForce = 15f,
            KnockbackUpward = 5f,
            StunTicks = 30,         // 0.5s
            SelfLockTicks = 40,     // 0.67s animation lock
            StartupTicks = 10,      // 0.17s startup frames
        }
    }
}

// In PlayerController.cs — spawn hitbox based on stage data
var stage = ability.Stages[_currentStage];
var hb = new Hitbox
{
    X = attackerPos.X + forward.X * 2f,
    Y = attackerPos.Y + 1f,
    Z = attackerPos.Z + forward.Z * 2f,
    Radius = 2f,                    // Hardcoded for now
    DurationTicks = 5,
    Damage = stage.Damage,
    KnockbackForce = stage.KnockbackForce,
    KnockbackUpward = stage.KnockbackUpward,
    StunTicks = stage.StunTicks,
    OwnerId = pid,
};
SpellResolver.Spawn(hb);
```

---

## 🔵 Hurtbox (target)

### Client-side — `Scripts/Combat/Hurtbox.cs` (Godot Area3D)

```csharp
public partial class Hurtbox : Area3D
{
    [Export] public Node3D OwnerEntity;
    public event Action<Vector3, float, Vector3> OnHit;

    public void TakeHit(Vector3 attackerPos, float damage, Vector3 knockbackForce)
    {
        OnHit?.Invoke(attackerPos, damage, knockbackForce);
    }
}
```

- **Purpose**: Visual marker, client-side event trigger (VFX, SFX)
- **Collision layer**: 2 (entities)
- **NOT used for detection**: server does detection with pure math

### Server-side — `SpellResolver.EntityData` (pure C# struct)

```csharp
public struct EntityData
{
    public ulong Id;
    public float PosX, PosY, PosZ;
    public float Radius;        // Hurtbox radius
    public bool Active;
}
```

- Built via `BuildEntityList()` in `CombatComponent.cs`
- Passed to `SpellResolver.Tick()` for collision check

---

## ⚙️ Collision engine — `SpellResolver.cs`

### Tick-by-tick flow

```csharp
public static List<HitResult> Tick(List<EntityData> entities)
{
    var results = new List<HitResult>();
    var hitThisTick = new HashSet<ulong>();  // Anti double-hit

    foreach (var hb in _hitboxes)
    {
        // 1. Move projectiles
        hb.X += hb.VX * Simulation.TickDt;
        hb.Y += hb.VY * Simulation.TickDt;
        hb.Z += hb.VZ * Simulation.TickDt;

        // 2. Check collision vs all entities
        foreach (var entity in entities)
        {
            if (entity.Id == hb.OwnerId) continue;          // Skip self-hit
            if (hitThisTick.Contains(entity.Id)) continue;  // Already hit this tick

            float dx = entity.PosX - hb.X;
            float dy = entity.PosY - hb.Y;
            float dz = entity.PosZ - hb.Z;
            float distSq = dx*dx + dy*dy + dz*dz;
            float combinedRadius = hb.Radius + entity.Radius;

            if (distSq <= combinedRadius * combinedRadius)
            {
                // HIT! Calculate knockback direction
                float dist = MathF.Sqrt(distSq);
                float kbX = dist > 0.001f ? (dx / dist) * hb.KnockbackForce : 0f;
                float kbZ = dist > 0.001f ? (dz / dist) * hb.KnockbackForce : 0f;

                results.Add(new HitResult { ... });
                hitThisTick.Add(entity.Id);
                hb.Active = false;  // One-hit-per-hitbox
                break;
            }
        }

        // 3. Age / expire
        hb.AgeTicks++;
        if (hb.AgeTicks >= hb.DurationTicks || !hb.Active)
            _hitboxes.RemoveAt(i);
    }

    return results;
}
```

### Key points

- **Sphere-sphere collision**: `distSq <= (r1 + r2)²`
- **One-hit-per-hitbox**: `hb.Active = false` after impact
- **No double-hit in the same tick**: `hitThisTick` HashSet
- **Knockback direction**: normalized toward target (dx/dist, dz/dist)
- **Edge case fixed**: if `dist < 0.001f` (perfect overlap), knockback = 0 (before: bug where kbZ = raw KnockbackForce)

---

## 🐛 Debug visualization — `F3` to toggle

### `DebugHitboxDraw.cs`

- **Hitboxes** (red): wireframe sphere + label `HIT\nDMG:20\nKB:15\n5/60t`
- **Hurtboxes** (blue): wireframe sphere + label `HURT\nR:2.0`
- **Labels 3D**: billboard, no depth test, above spheres
- **Toggle**: `F3` key (in `Main.cs`)

### Label example

```
HIT             (red)
DMG:20.0
KB:15.0
5/60t           (age / duration in ticks)

HURT            (blue)
R:2.0           (radius)
```

---

## 🚧 Current limitations (to extend)

### 1. **Single shape: spheres only**

- No capsules (long swords)
- No boxes (rectangular AoE)
- No cones (flamethrower)
- No rays (laser beam)

**Future solution**: add a `HitboxShape` enum in `Hitbox.cs`

```csharp
public enum HitboxShape { Sphere, Capsule, Box, Cone, Ray }

public struct Hitbox
{
    public HitboxShape Shape;
    public float Param1, Param2, Param3;  // Capsule: radius, height, 0
                                           // Box: width, height, depth
                                           // Cone: angle, range, 0
    // ...
}
```

### 2. **Hitbox spawn hardcoded**

- Position: `attackerPos + forward * 2f` (hardcoded 2m in front)
- Radius: `2f` (hardcoded)
- Duration: `5` ticks (hardcoded)

**Move to `AttackStage`:**

```csharp
public struct AttackStage
{
    // Existing fields...
    public float HitboxOffsetForward;  // 2f = 2m in front
    public float HitboxOffsetUp;       // 1f = 1m above
    public float HitboxRadius;         // 2f = sphere 2m
    public ushort HitboxDurationTicks; // 5 = ~83ms
}
```

### 3. **No multi-hitboxes per attack**

- One attack = one hitbox
- No support for: long sword (3 aligned hitboxes), shotgun (5 projectiles), etc.

**Future solution**: `Hitbox[] Hitboxes` in `AttackStage`

---

## 📋 How to add a new attack

### Step 1: Define the `AttackStage` (data-driven)

```csharp
// In CharacterDefinition.cs
new AbilityData
{
    Name = "Heavy Slam",
    CooldownTicks = 180,  // 3s cooldown
    Stages = new[]
    {
        new AttackStage
        {
            Damage = 50f,
            KnockbackForce = 30f,
            KnockbackUpward = 10f,
            LungeForce = 5f,          // Forward dash during attack
            StunTicks = 60,           // 1s stun
            SelfLockTicks = 80,       // 1.33s animation lock
            StartupTicks = 20,        // 0.33s startup (telegraphing)
        }
    }
}
```

### Step 2: Spawn the hitbox (currently hardcoded)

```csharp
// In PlayerController.cs or CombatComponent.cs
var stage = ability.Stages[_currentStage];
var attackerPos = GlobalPosition;
var forward = -Transform.Basis.Z.Normalized();

var hb = new Hitbox
{
    X = attackerPos.X + forward.X * 2f,   // ⚠️ Hardcoded: 2m in front
    Y = attackerPos.Y + 1f,               // ⚠️ Hardcoded: 1m in height
    Z = attackerPos.Z + forward.Z * 2f,
    Radius = 2f,                          // ⚠️ Hardcoded: radius 2m
    DurationTicks = 5,                    // ⚠️ Hardcoded: 5 ticks
    VX = 0, VY = 0, VZ = 0,              // Static melee (0 = no projectile)
    Damage = stage.Damage,
    KnockbackForce = stage.KnockbackForce,
    KnockbackUpward = stage.KnockbackUpward,
    StunTicks = stage.StunTicks,
    OwnerId = _entityId,
    Active = true,
};
SpellResolver.Spawn(hb);
```

### Step 3: Tick + resolve hits

```csharp
var entities = BuildEntityList();
var results = SpellResolver.Tick(entities);

foreach (var hit in results)
{
    // Apply damage, knockback, stun
    _simulation.OnEntityHit?.Invoke(hit.TargetEntityId, hit.Damage, 
                                    hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
}
```

---

## 🎮 Debug controls

- **F3**: Toggle hitbox/hurtbox visualization
- **Tab**: Target NPC (shows target ring)
- **LMB/RMB**: Spawn melee hitboxes
- **Q/E/R/F**: Abilities

---

## 🔧 Next steps to extend the system

### Option A: Add params in `AttackStage` (data-driven)

```csharp
public struct AttackStage
{
    // Existing...
    public float HitboxOffsetForward;
    public float HitboxOffsetUp;
    public float HitboxRadius;
    public ushort HitboxDurationTicks;
    public HitboxShape HitboxShape;  // Sphere, Capsule, Box, Cone
}
```

**Advantages**: Simple, keep logic in code  
**Disadvantages**: Not flexible for multi-hitboxes (long sword = 3 spheres)

### Option B: Define `HitboxTemplate[]` per attack

```csharp
public struct HitboxTemplate
{
    public HitboxShape Shape;
    public Vector3 OffsetLocal;   // Relative to attacker
    public float Radius;
    public ushort SpawnDelayTicks; // Delayed spawn (e.g., projectile after 10t)
    public ushort DurationTicks;
}

public struct AttackStage
{
    // Existing...
    public HitboxTemplate[] Hitboxes;  // Multi-hitbox support
}
```

**Advantages**: Flexible (shotgun = 5 projectiles, sword = 3 spheres aligned)  
**Disadvantages**: More complex to setup

### Option C: Hitbox spawning via `SpecialEffectKeys` (ability code)

```csharp
// In CharacterDefinition.cs
new AbilityData
{
    Name = "Fireball",
    SpecialEffectKeys = new[] { "spawn_fireball_projectile" },
    // ...
}

// In AbilityRegistry.cs
_effects["spawn_fireball_projectile"] = (combat, _) =>
{
    var pos = combat.GetOwnerPosition();
    var forward = combat.GetCameraForward();
    var hb = new Hitbox
    {
        X = pos.X, Y = pos.Y + 1.5f, Z = pos.Z,
        VX = forward.X * 30f,  // Projectile: 30 m/s
        VY = forward.Y * 30f,
        VZ = forward.Z * 30f,
        Radius = 1f,
        DurationTicks = 120,  // 2s lifetime
        Damage = 40f,
        // ...
    };
    SpellResolver.Spawn(hb);
};
```

**Advantages**: Maximum flexibility (custom code per spell)  
**Disadvantages**: Less data-driven, harder to debug

**Recommendation**: Start with **Option A** (simple) for basic attacks, keep **Option C** for complex spells (projectiles, delayed AoE).

---

## 📊 Performance notes

- **SpellResolver.Tick()**: O(H × E) where H = active hitboxes, E = entities
- **Typically**: H < 10, E < 50 → ~500 checks/tick = negligible
- **Pure math**: no Godot physics queries (netcode-ready)
- **Struct-based**: no GC allocations (except List resize)

---

## 🔗 Key files to modify for extension

1. **`Shared/Hitbox.cs`**: Add fields (Shape, Params)
2. **`Shared/AttackData.cs`**: Add HitboxTemplate[] or offset/radius fields
3. **`Shared/SpellResolver.cs`**: Add collision shapes (capsule, box, cone)
4. **`Scripts/Combat/CombatComponent.cs`**: Spawn logic from AttackStage data
5. **`Scripts/Debug/DebugHitboxDraw.cs`**: Visualize new shapes (capsules, boxes)

---

## ✅ Summary

| Aspect | Current state |
|--------|-------------|
| **Detection** | ✅ Sphere-sphere pure math (netcode-ready) |
| **Tick-based** | ✅ 60Hz, ushort ticks |
| **One-hit** | ✅ HashSet anti double-hit |
| **Projectiles** | ✅ Velocity support (VX, VY, VZ) |
| **Knockback** | ✅ Direction calculated (fixed: line 106 bug) |
| **Debug visual** | ✅ F3 toggle, wireframe + labels |
| **Multi-shapes** | ❌ Spheres only (capsules/boxes coming) |
| **Data-driven hitbox spawn** | ❌ Hardcoded (offset/radius/duration) |
| **Multi-hitboxes** | ❌ One attack = one hitbox |

---

**Questions?** Read this doc, press F3 in-game, and watch `DebugHitboxDraw` in action. Red hitboxes and blue hurtboxes should make everything visually clear.
