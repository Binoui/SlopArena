# Range-Based Combat System

Complete implementation of Range-based combat (Range-Based) inspired combat mechanics for SlopArena.

## 🎯 Overview

This system implements three core Range-Based mechanics:
1. **Soft Lock** - Automatic target tracking (no button press)
2. **Attack Range** - Distance where attacks hit immediately
3. **Warp Range** - Distance where auto-dash triggers before attack

## 📁 Architecture

```
Scripts/Combat/
├── TargetLockSystem.cs      — Soft lock (auto-track nearest enemy)
├── AttackWarping.cs          — Auto-dash toward target
├── CombatComponent.cs        — Range checks + warp execution
└── Hurtbox.cs

Shared/
└── AttackData.cs             — Attack definitions with Range-Based ranges
```

---

## 🔴 Soft Lock System

**File**: `Scripts/Combat/TargetLockSystem.cs`

Continuously tracks the nearest enemy in camera view without button press.

### Features
- **Automatic tracking**: Updates every 100ms
- **Cone-based**: 45° half-angle (90° total FOV)
- **Range-limited**: 20m max distance
- **Sticky targeting**: Hysteresis prevents target flickering
- **Visual feedback**: Red ring indicator under target

### Configuration
```csharp
[Export] public float LockRange = 20f;           // Max lock distance
[Export] public float LockAngle = 45f;           // Half-angle cone
[Export] public float UpdateInterval = 0.1f;     // Update frequency
[Export] public float StickyMultiplier = 1.2f;   // Hysteresis threshold
```

### Usage
```csharp
// In PlayerController._Ready()
_targetLock = new TargetLockSystem
{
    Camera = _camera.GetCamera(),
    LockRange = 20f,
    LockAngle = 45f,
};
AddChild(_targetLock);

// Query target
float dist = _targetLock.GetDistanceToTarget();
Vector3 dir = _targetLock.GetDirectionToTarget();
```

---

## ⚡ Attack Warping

**File**: `Scripts/Combat/AttackWarping.cs`

Auto-dash toward target when in warp range but outside attack range.

### Behavior
```
Player presses attack button:
├─ Target distance ≤ AttackRange (4m)
│  └─> Execute attack immediately
│
├─ AttackRange < distance ≤ WarpRange (4-12m)
│  ├─> Dash toward target at WarpSpeed
│  ├─> Stop at AttackRange distance
│  └─> Execute attack
│
└─ Distance > WarpRange (12m+)
   └─> Attack in place (likely miss)
```

### Features
- **Dynamic tracking**: Updates warp direction mid-dash (tracks moving targets)
- **Collision-aware**: Uses `CharacterBody3D.MoveAndSlide()` for physics
- **Safety timeout**: 0.5s max warp duration (prevents infinite dash)
- **Cancellable**: Can be interrupted by hitstun

### Usage
```csharp
// Start warp (called automatically by CombatComponent)
_warpSystem.StartWarp(attackRange: 4f, warpSpeed: 25f, onComplete: () =>
{
    ExecuteAttack();
});

// Cancel warp (e.g., got hit during warp)
_warpSystem.CancelWarp();
```

---

## 🎮 Attack Stage Configuration

**File**: `Shared/AttackData.cs`

### New Fields
```csharp
public struct AttackStage
{
    // Existing fields (damage, stun, etc.)
    // ...

    // Range-Based-style range system
    public float AttackRange;       // Distance where attack hits immediately (e.g., 4m)
    public float WarpRange;         // Distance where auto-dash triggers (e.g., 12m)
    public float WarpSpeed;         // Dash speed during warp (e.g., 25 m/s)
    public bool UseTargetLock;      // true = use soft lock for this attack
    public bool RotateTowardTarget; // true = auto-rotate during attack (future)
    public float TrackingStrength;  // 0-1: rotation lerp strength (future)
}
```

### Example Definitions

#### Light Attack (fast, long warp)
```csharp
new AttackStage
{
    Damage = 12f,
    KnockbackForce = 25f,
    StunTicks = 30,
    SelfLockTicks = 40,
    StartupTicks = 8,
    
    // Range-Based ranges
    AttackRange = 4f,      // Hit within 4m
    WarpRange = 10f,       // Warp if 4-10m away
    WarpSpeed = 25f,       // Fast dash (25 m/s)
    UseTargetLock = true,
    RotateTowardTarget = true,
    TrackingStrength = 0.9f,  // Strong tracking
}
```

#### Heavy Attack (slow, shorter warp)
```csharp
new AttackStage
{
    Damage = 25f,
    KnockbackForce = 50f,
    StunTicks = 60,
    SelfLockTicks = 80,
    StartupTicks = 20,
    
    // Range-Based ranges
    AttackRange = 6f,      // Longer reach
    WarpRange = 15f,       // Longer warp range
    WarpSpeed = 18f,       // Slower warp (more telegraphed)
    UseTargetLock = true,
    RotateTowardTarget = true,
    TrackingStrength = 0.5f,  // Less tracking (dodgeable)
}
```

#### No-Warp Attack (projectile/AoE)
```csharp
new AttackStage
{
    Damage = 8f,
    // ...
    
    // No warp
    AttackRange = 8f,      // Range check only
    WarpRange = 0f,        // No warp
    WarpSpeed = 0f,
    UseTargetLock = false, // No target lock
}
```

---

## 🔧 Combat Component Integration

**File**: `Scripts/Combat/CombatComponent.cs`

### New Methods

#### ExecuteAttackWithWarp
```csharp
public void ExecuteAttackWithWarp(AttackStage stage, Action onAttackStart)
```
Main entry point for Range-Based-style attacks. Checks range and initiates warp if needed.

**Flow:**
1. Check if `UseTargetLock = true` and target exists
2. Measure distance to target
3. If `dist ≤ AttackRange`: Execute immediately
4. If `AttackRange < dist ≤ WarpRange`: Start warp → execute after
5. If `dist > WarpRange`: Execute in place (miss)

#### CancelAttackWarp
```csharp
public void CancelAttackWarp()
```
Cancel active warp (e.g., player gets hit during warp).

#### IsWarping
```csharp
public bool IsWarping()
```
Check if currently warping toward target.

---

## 🎯 Target Groups

Entities must be in the `"enemies"` group to be targetable.

### Setup
```csharp
// In DummyManager.cs (NPCs)
body.AddToGroup("enemies");

// In PlayerController.cs (for PvP)
AddToGroup("enemies");  // If PvP enabled
```

---

## 📊 Performance Notes

- **Soft lock update**: 10 Hz (every 0.1s) - negligible cost
- **Warp tracking**: Updates every physics frame (~60 Hz) only during active warp
- **Target search**: O(N) where N = enemies in scene (typically < 10)
- **No allocations**: Reuses existing node queries

---

## 🚀 Usage Example

### In CharacterDefinition.cs
```csharp
new AbilityData
{
    Name = "Monkey Combo",
    CooldownTicks = 0,
    Stages = new AttackStage[]
    {
        // Stage 1: Light punch
        new() { 
            Damage = 4f, 
            KnockbackForce = 3f, 
            StunTicks = 10, 
            SelfLockTicks = 46, 
            StartupTicks = 6,
            AttackRange = 4f,     // Hit within 4m
            WarpRange = 10f,      // Warp if 4-10m away
            WarpSpeed = 25f,      // Fast dash
            UseTargetLock = true,
            RotateTowardTarget = true,
            TrackingStrength = 0.9f,
        },
        // Stages 2-3: Similar with adjusted values
    },
    AnimationNames = new[] { "melee", "leg_sweep", "backflip" },
}
```

### In PlayerController.cs (future - not yet wired)
```csharp
private void OnAttackInput(int abilitySlot)
{
    var ability = _charDef.Abilities[abilitySlot];
    var stage = ability.Stages[0];
    
    // Range-Based-style: check range + warp if needed
    _combatComponent.ExecuteAttackWithWarp(stage, () =>
    {
        // This callback fires after warp completes (or immediately if no warp)
        StartAttackAnimation(abilitySlot);
    });
}
```

---

## 🎨 Visual Feedback

### Current
- ✅ Red ring under soft-locked target
- ✅ Ring rotates slowly (visual polish)
- ✅ Ring hidden when no target

### Future (TODO)
- ⏳ Dash trail VFX during warp
- ⏳ Target reticle on HUD
- ⏳ Distance indicator (color-coded by range)
- ⏳ Warp startup animation

---

## 🐛 Debugging

### Console Output
Warp system prints debug messages:
```
[Attack] Target in range (3.2m <= 4.0m), attacking
[Attack] Target in warp range (7.5m), warping to 4.0m
[Warp] Starting warp: 3.5m at 25.0m/s
[Warp] Warp complete, executing attack
[Warp] Safety timeout, ending warp
```

### Common Issues

**Target not locking:**
- Check entity is in `"enemies"` group
- Verify camera is set in `TargetLockSystem.Camera`
- Check target within `LockRange` (20m default)
- Check target within `LockAngle` (45° default)

**Warp not triggering:**
- Verify `UseTargetLock = true` in `AttackStage`
- Check `WarpRange > AttackRange`
- Check `WarpSpeed > 0`
- Verify `CombatComponent.Setup()` received `TargetLockSystem`

**Warp overshooting:**
- Reduce `WarpSpeed` (try 15-20 m/s)
- Increase `AttackRange` (stop sooner)

---

## ✅ Implementation Checklist

- [x] TargetLockSystem.cs (soft lock)
- [x] AttackWarping.cs (auto-dash)
- [x] AttackData.cs (Range-Based range fields)
- [x] CombatComponent.cs (range check + warp integration)
- [x] PlayerController.cs (target lock setup)
- [x] DummyManager.cs (add NPCs to "enemies" group)
- [x] CharacterDefinition.cs (add ranges to Manki attacks)
- [ ] Wire up attack input to call `ExecuteAttackWithWarp()` (current: direct spawn)
- [ ] Add rotation toward target during attack (TrackingStrength)
- [ ] Add warp VFX (dash trail)
- [ ] Add target reticle HUD element

---

## 🎮 Design Rationale

### Why Range-Based-style over traditional targeting?

**Problem**: 3D brawlers suffer from depth perception issues. Players miss attacks due to:
- Camera angle obscuring distance
- Fast-paced movement making manual aim difficult
- Frustration when attacks "pass through" enemies

**Solution**: Range-Based's three-tier system
1. **Soft lock**: Removes need for precise camera aiming
2. **Attack range**: Immediate hit within melee range (forgiving)
3. **Warp range**: Magnetic dash feels responsive (not cheap)

### Range Tuning Guidelines

**Light attacks** (jabs, quick hits):
- `AttackRange`: 3-5m (short reach)
- `WarpRange`: 8-12m (long dash for mobility)
- `WarpSpeed`: 25-30 m/s (fast, responsive)
- `TrackingStrength`: 0.8-1.0 (strong tracking)

**Heavy attacks** (smashes, slams):
- `AttackRange`: 5-7m (longer reach)
- `WarpRange`: 10-15m (moderate dash)
- `WarpSpeed`: 15-20 m/s (slower, more telegraphed)
- `TrackingStrength`: 0.4-0.6 (less tracking, dodgeable)

**Projectiles/AoE**:
- `AttackRange`: weapon-dependent
- `WarpRange`: 0 (no warp)
- `UseTargetLock`: false

---

## 🔮 Future Enhancements

### Phase 1 (Core - DONE)
- [x] Soft lock system
- [x] Attack warping
- [x] Range-based execution

### Phase 2 (Polish - TODO)
- [ ] Auto-rotation toward target during attack (`RotateTowardTarget`)
- [ ] Warp VFX (dash trail, speed lines)
- [ ] Target reticle HUD
- [ ] Distance-based color coding (green = in range, yellow = warp range, red = out of range)

### Phase 3 (Advanced - TODO)
- [ ] Multi-target soft lock (cycle with Tab)
- [ ] Soft lock priority (prefer low HP targets)
- [ ] Warp canceling via dodge/roll
- [ ] Aerial warp (dash in 3D space, not just horizontal)

---

## 📚 References

- **Range-based combat (Range-Based)**: Original inspiration for range-based combat
- **Smash Bros**: Hitbox/hurtbox system (see `HITBOX_SYSTEM.md`)
- **Naraka Bladepoint**: Similar soft lock + dash system
- **Dragon Ball FighterZ**: Auto-tracking during combos

---

**Questions?** Read this doc, test in-game with NPCs, watch console output during attacks.
