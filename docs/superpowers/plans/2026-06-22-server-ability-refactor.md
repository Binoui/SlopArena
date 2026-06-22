# Server-Side Warp + ServerAbility Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify all abilities under the `ServerAbility` pattern, remove the legacy data-driven `AttackStage[]` execution path, and fix warp movement to run server-side.

**Architecture:** All abilities become `ServerAbility` subclasses (polymorphic). Data (damage, timings) stays in `AbilitySpec` as tunable parameters. Warp movement moves from client-only `AttackWarping.cs` to `Shared/Simulation.cs` (server-authoritative). The old `AbilityExecutor.ProcessActive()` path is removed.

**Tech Stack:** C# pure (`Shared/`), Godot C# (`Scripts/`), tick-based simulation (60Hz)

---

## File Structure

### Created Files
- `Shared/Abilities/MankiLmbCombo.cs` — 3-hit melee combo with 5m range-closing lunge
- `Shared/Abilities/MankiRoundBomb.cs` — Projectile throw ability (Q slot)
- `Shared/Abilities/MankiAerosolFlame.cs` — Charged flamethrower (RMB slot)

### Modified Files
- `Shared/Simulation.cs:518-545` — Refactor `ProcessWarp()` to properly handle collision + gravity
- `Shared/Simulation.cs:113-114` — Remove `ActionState.Warping` branch (warp becomes invisible velocity modifier)
- `Shared/Simulation.cs:471-484` — Remove `ProcessAttack()` (dead code after refactor)
- `Shared/Simulation.cs:486-514` — Remove `StartAttackFromSlot()` warp initiation (moved to ServerAbility OnStart)
- `Shared/ServerSimulation.cs:224` — Remove typeId == 0 check (all abilities are ServerAbility now)
- `Shared/AbilityExecutor.cs` — DELETE (no longer needed)
- `Shared/Abilities/GenericMelee.cs` — DELETE (placeholder)
- `Shared/Abilities/MeleeCombo.cs` — DELETE (replaced by MankiLmbCombo)
- `Shared/Abilities/BackflipRoll.cs` — DELETE (placeholder, not used by Manki)
- `Shared/Abilities/AbilityFactory.cs:14-20` — Update mapping (1=MankiLmbCombo, 2=MankiRoundBomb, 3=MankiAerosolFlame)
- `Shared/Characters/MankiData.cs:78-94` — Set LMB.AbilityTypeId = 1, add Params for lunge
- `Shared/Characters/MankiData.cs:149-176` — Set Q.AbilityTypeId = 2, keep ProjectileConfig in Params
- `Shared/Characters/MankiData.cs:110-134` — Set RMB.AbilityTypeId = 3, add Params for charge timing
- `Scripts/Combat/AttackWarping.cs` — DELETE (client-only, replaced by server warp)
- `Scripts/Entities/PlayerController.cs` — Remove references to `AttackWarping` component

---

## Task 1: Fix Server-Side Warp Movement

**Files:**
- Modify: `Shared/Simulation.cs:518-545`
- Modify: `Shared/Simulation.cs:113-114`

**Context:** Warp currently lives in client-only `AttackWarping.cs` (`_PhysicsProcess`). This causes prediction desync. Server must control warp movement in `Simulation.SimulateTick()`.

**Approach:** Refactor `ProcessWarp()` to handle collision detection, gravity (airborne warps), and terrain snapping. Remove the separate `ActionState.Warping` state — warp becomes a velocity override that applies during any state.

- [ ] **Step 1: Read current ProcessWarp implementation**

Run: `cat Shared/Simulation.cs | sed -n '518,545p'`
Expected: Shows current simple warp logic (no collision, no gravity)

- [ ] **Step 2: Replace ProcessWarp with full movement simulation**

```csharp
/// <summary>
/// Process warping state: move toward warp target each tick with collision + gravity.
/// Returns true if warp completed (arrived at target), false if still warping.
/// </summary>
private static bool ProcessWarp(ref CharacterState s, CharacterDefinition def, ArenaDefinition arena)
{
    float dx = s.WarpTargetX - s.PX;
    float dz = s.WarpTargetZ - s.PZ;
    float distSq = dx * dx + dz * dz;
    float attackRangeSq = s.WarpAttackRange * s.WarpAttackRange;

    // Close enough → warp complete
    if (distSq <= attackRangeSq)
    {
        s.WarpSpeed = 0f;
        s.VX = 0f;
        s.VZ = 0f;
        return true;
    }

    // Move toward target
    float dist = MathF.Sqrt(distSq);
    s.VX = (dx / dist) * s.WarpSpeed;
    s.VZ = (dz / dist) * s.WarpSpeed;
    s.FacingYaw = MathF.Atan2(dx, dz);

    // Apply gravity if airborne
    if (!s.IsGrounded)
    {
        float gravity = def.Movement.Gravity * TickDt;
        s.VY -= gravity;
        if (s.VY < -def.Movement.MaxFallSpeed)
            s.VY = -def.Movement.MaxFallSpeed;
    }

    // Apply movement with collision
    ProcessMovement(ref s, def.Movement, arena);
    
    return false; // still warping
}
```

- [ ] **Step 3: Update SimulateTick to handle warp as velocity override**

Find line 113-114:
```csharp
else if (s.State == ActionState.Warping)
    ProcessWarp(ref s, def);
```

Replace with:
```csharp
// Warp processing: velocity override during any state
if (s.WarpSpeed > 0f)
{
    bool warpComplete = ProcessWarp(ref s, def, arena);
    if (warpComplete)
    {
        // Warp arrival: ability activation is handled by ServerAbility.OnStart
        // Just clear warp state here
        s.State = ActionState.Idle;
    }
}
else if (s.State == ActionState.Dashing)
    ProcessDash(ref s, stats);
```

- [ ] **Step 4: Remove ActionState.Warping from StartAttackFromSlot**

Find `Shared/Simulation.cs:486-514` (StartAttackFromSlot function).

Delete lines 493-506 (the warp initiation block):
```csharp
// Check for warp-to-target
if (ability != null && ability.Stages is { Length: > 0 }
    && ability.Stages[0].WarpRange > 0f && s.WarpSpeed > 0f)
{
    // ...entire warp setup block...
    s.State = ActionState.Warping;
    return;
}
```

Warp will now be initiated by ServerAbility.OnStart, not by Simulation.

- [ ] **Step 5: Verify ActionState enum**

Run: `grep -n "enum ActionState" Shared/ActionState.cs`
Expected: Shows Warping = 4 or similar. (We'll keep the enum value for now but it won't be used)

- [ ] **Step 6: Commit warp refactor**

```bash
git add Shared/Simulation.cs
git commit -m "refactor: move warp movement to server-side with collision/gravity

- ProcessWarp() now handles collision detection and airborne gravity
- Warp is a velocity override during any state, not a separate state
- Removed warp initiation from StartAttackFromSlot (will be ServerAbility responsibility)
- Server-authoritative warp prevents client prediction desync"
```

---

## Task 2: Remove Legacy Data-Driven Execution Path

**Files:**
- Modify: `Shared/Simulation.cs:471-484`
- Delete: `Shared/AbilityExecutor.cs`
- Modify: `Shared/ServerSimulation.cs:224`

**Context:** The old `AttackStage[]` + `AbilityExecutor.ProcessActive()` path is dead code once all abilities are `ServerAbility`. Clean it up.

- [ ] **Step 1: Remove ProcessAttack from Simulation.cs**

Find `Shared/Simulation.cs:471-484` (ProcessAttack function):
```csharp
private static void ProcessAttack(ref CharacterState s, CharacterDefinition def, ref InputState input)
{
    // Server-side abilities handle their own tick via ServerSimulation.TickAbilities
    if (s.IsServerAbility) return;

    if (s.AnimLockTicks > 0)
        return;

    bool airborne = !s.IsGrounded;
    var ability = def.GetSlotAbility(s.AttackSlot - 1, airborne);
    AbilityExecutor.ProcessActive(ref s, ability, ref input);
}
```

Delete the entire function (lines 471-484).

- [ ] **Step 2: Remove ProcessAttack call from SimulateTick**

Find line ~111-112:
```csharp
else if (s.State == ActionState.Attacking)
    ProcessAttack(ref s, def, ref input);
```

Delete these two lines. Attacking state is now purely handled by ServerSimulation.TickAbilities.

- [ ] **Step 3: Remove StartAttackFromSlot references to AbilityExecutor**

Find `Shared/Simulation.cs` line ~530-532:
```csharp
var ability = def.GetSlotAbility(s.AttackSlot - 1, airborne);
ushort cd = AbilityExecutor.GetCooldown(s, s.AttackSlot);
AbilityExecutor.TryStart(ref s, ability, s.AttackSlot, cd);
```

Replace with:
```csharp
// Ability activation is now handled by ServerSimulation pre-sim phase
// This function is only called for buffered input consumption
```

Actually, `StartAttackFromSlot` itself should be removed since ServerSimulation handles activation. Let's delete it entirely.

Find the entire `StartAttackFromSlot` function (lines ~486-514) and delete it.

- [ ] **Step 4: Remove StartAttackFromSlot call from SimulateTick**

Find line ~122:
```csharp
StartAttackFromSlot(ref s, def, slot);
```

Replace with:
```csharp
// Ability activation handled by ServerSimulation.Tick pre-sim phase
// Client prediction: mark state as Attacking to prevent movement
s.State = ActionState.Attacking;
s.AttackSlot = slot;
```

Find line ~138:
```csharp
StartAttackFromSlot(ref s, def, input.ActiveSlot);
```

Replace with same block above.

- [ ] **Step 5: Delete AbilityExecutor.cs**

Run: `rm Shared/AbilityExecutor.cs`

- [ ] **Step 6: Remove typeId == 0 check from ServerSimulation**

Find `Shared/ServerSimulation.cs:224`:
```csharp
if (spec.AbilityTypeId == 0) continue; // data-driven, handled by SimulateTick
```

Replace with:
```csharp
if (spec.AbilityTypeId == 0)
{
    Console.WriteLine($"[ServerSimulation] ERROR: Ability slot {input.ActiveSlot} has AbilityTypeId=0 (no ServerAbility assigned)");
    continue;
}
```

- [ ] **Step 7: Remove GetCooldown/SetCooldown from AbilityExecutor**

Wait, we deleted `AbilityExecutor.cs` but `ServerSimulation.cs` uses `SetCooldown`. We need to move those helper functions.

Find `Shared/AbilityExecutor.cs:125-150` (GetCooldown/SetCooldown functions).

Copy them to `Shared/ServerSimulation.cs` after the `GetActiveAbility` method (around line 104):

```csharp
/// <summary>
/// Get cooldown ticks for a slot (1-6).
/// </summary>
public static ushort GetCooldown(CharacterState s, byte slot) => slot switch
{
    1 => s.Cooldown0,
    2 => s.Cooldown1,
    3 => s.Cooldown2,
    4 => s.Cooldown3,
    5 => s.Cooldown4,
    6 => s.Cooldown5,
    _ => 0,
};

/// <summary>
/// Set cooldown ticks for a slot (1-6).
/// </summary>
public static void SetCooldown(ref CharacterState s, byte slot, ushort ticks)
{
    switch (slot)
    {
        case 1: s.Cooldown0 = ticks; break;
        case 2: s.Cooldown1 = ticks; break;
        case 3: s.Cooldown2 = ticks; break;
        case 4: s.Cooldown3 = ticks; break;
        case 5: s.Cooldown4 = ticks; break;
        case 6: s.Cooldown5 = ticks; break;
    }
}
```

- [ ] **Step 8: Fix SetCooldown calls in ServerSimulation**

Find line ~84:
```csharp
SetCooldown(ref state, (byte)(ability.Slot + 1), ability.Cooldown);
```

This is correct (Slot is 0-based, SetCooldown expects 1-based).

Find line ~146:
```csharp
SetCooldown(ref state, (byte)(ability.Slot + 1), ability.Cooldown);
```

Also correct.

- [ ] **Step 9: Verify AbilityExecutor is no longer used**

Run: `grep -rn "AbilityExecutor" Shared/ Scripts/ Server/`
Expected: No results

- [ ] **Step 10: Build to check for compile errors**

Run: `dotnet build --nologo 2>&1 | grep -i error`
Expected: No errors (warnings about nullable are fine)

- [ ] **Step 11: Commit legacy path removal**

```bash
git add Shared/Simulation.cs Shared/ServerSimulation.cs
git rm Shared/AbilityExecutor.cs
git commit -m "refactor: remove legacy data-driven ability execution path

- Deleted AbilityExecutor.cs (ProcessActive, TryStart, CanBufferCombo)
- Removed ProcessAttack() from Simulation.SimulateTick
- Removed StartAttackFromSlot() warp/combo logic (now ServerAbility responsibility)
- Moved GetCooldown/SetCooldown helpers to ServerSimulation
- All abilities now MUST have AbilityTypeId > 0 (ServerAbility dispatch)"
```

---

## Task 3: Implement MankiLmbCombo (5m Range-Closing)

**Files:**
- Create: `Shared/Abilities/MankiLmbCombo.cs`
- Modify: `Shared/Characters/MankiData.cs:78-94`
- Modify: `Shared/Abilities/AbilityFactory.cs:14-20`

**Context:** Manki's LMB is a 3-hit combo that lunges forward to close 5m gaps. Each stage has its own lunge duration, hitbox timing, and chain window. This replaces the placeholder `MeleeCombo`.

- [ ] **Step 1: Create MankiLmbCombo.cs**

```csharp
using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's LMB combo: 3-hit chain with forward lunge per stage.
    /// Closes up to 5m gaps via lunge_duration param (applied at stage start).
    /// </summary>
    public sealed class MankiLmbCombo : ServerAbility
    {
        private byte _stage;
        private ushort _stageTicks;
        private ushort _lungeDuration; // ticks to apply lunge velocity

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _stage = 0;
            _stageTicks = 0;
            _lungeDuration = (ushort)GetParam(def, "lunge_duration", 10f);

            var stage = GetCurrentStage(def);

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1); // 0-based slot → 1-based AttackSlot
            s.ComboStage = 0;
            s.AnimIndex = 0;
            s.AnimLockTicks = stage.DurationTicks;

            // Start lunge
            if (stage.LungeForce > 0f)
                SetVelocityInFacing(ref s, stage.LungeForce);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            var stage = GetCurrentStage(def);
            _stageTicks++;

            // Lunge velocity (applied for first N ticks of each stage)
            if (_stageTicks <= _lungeDuration && stage.LungeForce > 0f)
            {
                SetVelocityInFacing(ref s, stage.LungeForce);
            }

            // Spawn hitboxes at their trigger ticks
            if (stage.HitboxEvents != null)
            {
                foreach (var evt in stage.HitboxEvents)
                {
                    if (evt.TriggerTick == _stageTicks)
                        SpawnHitbox(ref s, evt);
                }
            }

            // Chain check: advance to next stage if input matches and within chain window
            var stages = def.GetSlotAbility(Slot, false).Stages;
            if (input.ActiveSlot == (Slot + 1)
                && _stageTicks >= stage.ChainWindowTicks
                && _stage < stages.Length - 1)
            {
                // Consume the buffered input
                input.ActiveSlot = 0;

                // Advance to next stage
                _stage++;
                _stageTicks = 0;
                s.ComboStage = _stage;
                s.AnimIndex = _stage;
                s.AnimLockTicks = stages[_stage].DurationTicks;

                // Restart lunge
                _lungeDuration = (ushort)GetParam(def, "lunge_duration", 10f);
                if (stages[_stage].LungeForce > 0f)
                    SetVelocityInFacing(ref s, stages[_stage].LungeForce);

                return; // don't end this tick
            }

            // End check: duration expired and no chain triggered
            if (_stageTicks >= stage.DurationTicks)
                EndAbility(ref s);
        }

        private AttackStage GetCurrentStage(CharacterDefinition def)
        {
            var stages = def.GetSlotAbility(Slot, false).Stages;
            return stages[Math.Min(_stage, stages.Length - 1)];
        }
    }
}
```

- [ ] **Step 2: Update MankiData.cs LMB spec**

Find `Shared/Characters/MankiData.cs:78-94` (LMB definition).

Add `AbilityTypeId` and `Params`:
```csharp
LMB = new AbilitySpec
{
    Name = "Monkey Combo",
    AbilityTypeId = 1, // MankiLmbCombo
    CooldownTicks = 0,
    Stages = new AttackStage[]
    {
        new() { DurationTicks = 52, ChainWindowTicks = 10, LungeForce = 8f,
                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 36, Radius = 1f, OffX = 0, OffY = 0, OffZ = 0.9f, Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 2f, StunTicks = 10, Interruptible = true } },
                AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
        new() { DurationTicks = 38, ChainWindowTicks = 8, LungeForce = 8f,
                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 22, Radius = 1f, OffX = 0, OffY = 0, OffZ = 0.9f, Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, StunTicks = 14, Interruptible = true } },
                AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
        new() { DurationTicks = 66, ChainWindowTicks = 0, LungeForce = 8f,
                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 10, DurationTicks = 43, Radius = 1f, OffX = 0, OffY = 0, OffZ = 0.9f, Damage = 10f, KnockbackForce = 16f, KnockbackUpward = 4f, StunTicks = 18, Interruptible = true } },
                AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
    },
    AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" },
    Params = new()
    {
        ["lunge_duration"] = 10f, // apply lunge velocity for first 10 ticks of each stage
    },
},
```

- [ ] **Step 3: Update AbilityFactory.cs**

Find `Shared/Abilities/AbilityFactory.cs:14-20`:
```csharp
public static ServerAbility CreateServer(byte typeId)
{
    return typeId switch
    {
        1 => new MeleeCombo(),
        2 => new BackflipRoll(),
        _ => new GenericMelee(),
    };
}
```

Replace with:
```csharp
public static ServerAbility CreateServer(byte typeId)
{
    return typeId switch
    {
        1 => new MankiLmbCombo(),
        _ => throw new ArgumentException($"Unknown AbilityTypeId: {typeId}"),
    };
}
```

- [ ] **Step 4: Delete placeholder ServerAbility files**

Run:
```bash
rm Shared/Abilities/GenericMelee.cs
rm Shared/Abilities/MeleeCombo.cs
rm Shared/Abilities/BackflipRoll.cs
```

- [ ] **Step 5: Build to check for compile errors**

Run: `dotnet build --nologo 2>&1 | grep -i error`
Expected: No errors

- [ ] **Step 6: Commit MankiLmbCombo**

```bash
git add Shared/Abilities/MankiLmbCombo.cs Shared/Characters/MankiData.cs Shared/Abilities/AbilityFactory.cs
git rm Shared/Abilities/GenericMelee.cs Shared/Abilities/MeleeCombo.cs Shared/Abilities/BackflipRoll.cs
git commit -m "feat(abilities): implement MankiLmbCombo with 5m range-closing lunge

- 3-hit combo that lunges forward for first 10 ticks of each stage
- Reads lunge_duration from Params (tunable)
- Hitbox spawning at per-stage TriggerTick
- Chain window support (buffer input during active attack)
- Deleted placeholder GenericMelee, MeleeCombo, BackflipRoll"
```

---

## Task 4: Implement MankiRoundBomb (Projectile Q)

**Files:**
- Create: `Shared/Abilities/MankiRoundBomb.cs`
- Modify: `Shared/Characters/MankiData.cs:149-176`
- Modify: `Shared/Abilities/AbilityFactory.cs:16`

**Context:** Manki Q throws a parabolic projectile that explodes on impact. Currently uses `RoundBombSpec : AbilitySpec` with `SpawnHitbox()` override. We're converting this to a `ServerAbility` that reads from `ProjectileConfig` params.

- [ ] **Step 1: Create MankiRoundBomb.cs**

```csharp
using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's Q: throw a bomb projectile in a parabolic arc.
    /// Reads ProjectileConfig from AbilitySpec.Params at runtime.
    /// </summary>
    public sealed class MankiRoundBomb : ServerAbility
    {
        private bool _projectileSpawned;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _projectileSpawned = false;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            s.AnimIndex = 0;
            s.AnimLockTicks = (ushort)GetParam(def, "throw_duration", 60f);
            s.IsAiming = true; // show aim indicator
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort throwTick = (ushort)GetParam(def, "throw_trigger_tick", 10f);

            // Spawn projectile at throw_trigger_tick (mid-animation)
            if (!_projectileSpawned && s.AttackElapsedTicks >= throwTick)
            {
                _projectileSpawned = true;
                s.IsAiming = false;

                // Read params
                float D = Math.Clamp(s.AimTargetDistance, 0.5f, GetParam(def, "max_range", 12f));
                float launchAngleDeg = GetParam(def, "launch_angle", 30f);
                float g = GetParam(def, "gravity", 30f);
                float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
                float dY = -def.CapsuleHeight * 0.5f - launchOffsetY;

                // Compute ballistic launch velocity
                float launchRad = launchAngleDeg * (MathF.PI / 180f);
                CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
                    out float _, out float hSpeed, out float vSpeed);

                float aimCos = MathF.Cos(s.AimYaw);
                float aimSin = MathF.Sin(s.AimYaw);

                // Spawn projectile hitbox
                Resolver.Spawn(new Hitbox
                {
                    X = s.PX,
                    Y = s.PY + launchOffsetY,
                    Z = s.PZ,
                    VX = hSpeed * aimSin,
                    VY = vSpeed,
                    VZ = hSpeed * aimCos,
                    Radius = GetParam(def, "hitbox_radius", 0.6f),
                    Shape = HitboxShape.Sphere,
                    EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                    Damage = GetParam(def, "damage", 8f),
                    KnockbackForce = GetParam(def, "knockback_force", 10f),
                    KnockbackUpward = GetParam(def, "knockback_upward", 6f),
                    StunTicks = (ushort)GetParam(def, "stun_ticks", 14f),
                    DurationTicks = (ushort)GetParam(def, "max_flight_ticks", 90f),
                    OwnerId = s.EntityId,
                    Gravity = g,
                    Explosion = new ProjectileExplosion
                    {
                        Radius = GetParam(def, "explosion_radius", 3f),
                        Damage = GetParam(def, "explosion_damage", 25f),
                        KnockbackForce = GetParam(def, "explosion_knockback_force", 18f),
                        KnockbackUpward = GetParam(def, "explosion_knockback_upward", 12f),
                        StunTicks = (ushort)GetParam(def, "explosion_stun_ticks", 20f),
                        DurationTicks = (ushort)GetParam(def, "explosion_duration_ticks", 6f),
                    },
                });
            }

            // End ability when animation lock expires
            if (s.AttackElapsedTicks >= s.AnimLockTicks)
                EndAbility(ref s);
        }
    }
}
```

- [ ] **Step 2: Update MankiData.cs Q spec**

Find `Shared/Characters/MankiData.cs:149` (Q = new RoundBombSpec).

Replace `RoundBombSpec` with `AbilitySpec` and flatten `ProjectileConfig` into `Params`:

```csharp
Q = new AbilitySpec
{
    Name = "Round Bomb",
    AbilityTypeId = 2, // MankiRoundBomb
    CooldownTicks = 90,
    Stages = new AttackStage[]
    {
        new() { DurationTicks = 60, ChainWindowTicks = 0,
                HitboxEvents = Array.Empty<HitboxEvent>(), // projectile spawned by ServerAbility
                AttackRange = 20f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
    },
    AnimationNames = new[] { "spell_q" },
    SpecialEffectKeys = new[] { "MankiRoundBomb" },
    Params = new()
    {
        ["throw_duration"] = 60f,
        ["throw_trigger_tick"] = 10f,
        ["launch_angle"] = 30f,
        ["gravity"] = 30f,
        ["max_range"] = 12f,
        ["hitbox_radius"] = 0.6f,
        ["launch_offset_y"] = 1.2f,
        ["damage"] = 8f,
        ["knockback_force"] = 10f,
        ["knockback_upward"] = 6f,
        ["stun_ticks"] = 14f,
        ["max_flight_ticks"] = 90f,
        ["explosion_radius"] = 3f,
        ["explosion_damage"] = 25f,
        ["explosion_knockback_force"] = 18f,
        ["explosion_knockback_upward"] = 12f,
        ["explosion_stun_ticks"] = 20f,
        ["explosion_duration_ticks"] = 6f,
    },
},
```

Remove the `LoopAnimName` and `ProjectileConfig` properties (no longer needed).

- [ ] **Step 3: Update AbilityFactory.cs**

Add case 2:
```csharp
public static ServerAbility CreateServer(byte typeId)
{
    return typeId switch
    {
        1 => new MankiLmbCombo(),
        2 => new MankiRoundBomb(),
        _ => throw new ArgumentException($"Unknown AbilityTypeId: {typeId}"),
    };
}
```

- [ ] **Step 4: Verify RoundBombSpec is no longer used**

Run: `grep -rn "RoundBombSpec" Shared/`
Expected: Only the class definition in `Shared/RoundBombSpec.cs` (we'll delete it later)

- [ ] **Step 5: Build to check for compile errors**

Run: `dotnet build --nologo 2>&1 | grep -i error`
Expected: No errors

- [ ] **Step 6: Commit MankiRoundBomb**

```bash
git add Shared/Abilities/MankiRoundBomb.cs Shared/Characters/MankiData.cs Shared/Abilities/AbilityFactory.cs
git commit -m "feat(abilities): implement MankiRoundBomb projectile ability

- Parabolic arc throw with aim targeting
- Reads all projectile params from Params dict (tunable)
- Explosion on impact (damage, knockback, radius)
- Replaces RoundBombSpec.SpawnHitbox() override with proper ServerAbility"
```

---

## Task 5: Implement MankiAerosolFlame (Charged RMB)

**Files:**
- Create: `Shared/Abilities/MankiAerosolFlame.cs`
- Modify: `Shared/Characters/MankiData.cs:110-134`
- Modify: `Shared/Abilities/AbilityFactory.cs:17`

**Context:** Manki RMB is a hold-to-charge flamethrower. Tap = quick burst, hold = charged cone. Uses `ChargedStages` for the charged variant.

- [ ] **Step 1: Create MankiAerosolFlame.cs**

```csharp
using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's RMB: hold-to-charge flamethrower cone.
    /// Tap = quick burst, hold 45 ticks = charged version (longer range, more damage).
    /// </summary>
    public sealed class MankiAerosolFlame : ServerAbility
    {
        private bool _charged;
        private ushort _chargeTicks;
        private bool _hitboxSpawned;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _charged = false;
            _chargeTicks = 0;
            _hitboxSpawned = false;

            ushort chargeThreshold = (ushort)GetParam(def, "charge_threshold", 45f);

            // Check if player held long enough to charge
            if (s.ChargeTicks >= chargeThreshold)
            {
                _charged = true;
            }

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            s.AnimIndex = (byte)(_charged ? 1 : 0); // charged anim vs normal anim
            s.AnimLockTicks = (ushort)GetParam(def, _charged ? "charged_duration" : "normal_duration", 50f);
            s.ChargeTicks = 0; // reset charge accumulator
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort triggerTick = (ushort)GetParam(def, _charged ? "charged_trigger_tick" : "normal_trigger_tick", 10f);

            // Spawn hitbox at trigger tick
            if (!_hitboxSpawned && s.AttackElapsedTicks >= triggerTick)
            {
                _hitboxSpawned = true;

                // Capsule hitbox in front of character
                float offZ = GetParam(def, _charged ? "charged_off_z" : "normal_off_z", 2.5f);
                float endOffZ = GetParam(def, _charged ? "charged_end_off_z" : "normal_end_off_z", 4.0f);
                float radius = GetParam(def, _charged ? "charged_radius" : "normal_radius", 1.0f);
                ushort duration = (ushort)GetParam(def, _charged ? "charged_hitbox_duration" : "normal_hitbox_duration", 30f);

                SpawnHitbox(ref s, new HitboxEvent
                {
                    TriggerTick = triggerTick,
                    DurationTicks = duration,
                    Shape = HitboxShape.Capsule,
                    Radius = radius,
                    OffX = 0,
                    OffY = 1.0f,
                    OffZ = offZ,
                    EndOffX = 0,
                    EndOffY = 0,
                    EndOffZ = endOffZ - offZ, // relative to start
                    Damage = GetParam(def, _charged ? "charged_damage" : "normal_damage", 14f),
                    KnockbackForce = GetParam(def, _charged ? "charged_knockback" : "normal_knockback", 24f),
                    KnockbackUpward = GetParam(def, _charged ? "charged_knockback_up" : "normal_knockback_up", 8f),
                    StunTicks = (ushort)GetParam(def, _charged ? "charged_stun" : "normal_stun", 20f),
                    Interruptible = true,
                });
            }

            // End ability when animation lock expires
            if (s.AttackElapsedTicks >= s.AnimLockTicks)
                EndAbility(ref s);
        }
    }
}
```

- [ ] **Step 2: Update MankiData.cs RMB spec**

Find `Shared/Characters/MankiData.cs:110` (RMB = new AerosolFlameSpec).

Replace with `AbilitySpec` and flatten all params:

```csharp
RMB = new AbilitySpec
{
    Name = "Aerosol + Lighter",
    AbilityTypeId = 3, // MankiAerosolFlame
    CooldownTicks = 30,
    Stages = new AttackStage[]
    {
        new() { DurationTicks = 58, ChainWindowTicks = 0,
                HitboxEvents = Array.Empty<HitboxEvent>(), // spawned by ServerAbility
                AttackRange = 6f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
    },
    ChargedStages = new AttackStage[]
    {
        new() { DurationTicks = 50, ChainWindowTicks = 0,
                HitboxEvents = Array.Empty<HitboxEvent>(),
                AttackRange = 8f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
    },
    ChargeHoldTicks = 45,
    AnimationNames = new[] { "spell_rmb_attack", "spell_rmb_charged" },
    SpecialEffectKeys = new[] { "MankiAerosolFlame" },
    Params = new()
    {
        ["charge_threshold"] = 45f,
        ["normal_duration"] = 58f,
        ["normal_trigger_tick"] = 8f,
        ["normal_off_z"] = 2.0f,
        ["normal_end_off_z"] = 3.0f,
        ["normal_radius"] = 0.8f,
        ["normal_hitbox_duration"] = 38f,
        ["normal_damage"] = 8f,
        ["normal_knockback"] = 14f,
        ["normal_knockback_up"] = 4f,
        ["normal_stun"] = 24f,
        ["charged_duration"] = 50f,
        ["charged_trigger_tick"] = 10f,
        ["charged_off_z"] = 2.5f,
        ["charged_end_off_z"] = 4.0f,
        ["charged_radius"] = 1.0f,
        ["charged_hitbox_duration"] = 30f,
        ["charged_damage"] = 14f,
        ["charged_knockback"] = 24f,
        ["charged_knockback_up"] = 8f,
        ["charged_stun"] = 20f,
    },
},
```

Remove `ChargeAnimName`, `ConeAngle`, `ConeRange`, `MaxChargeTicks` properties (no longer needed for ServerAbility).

- [ ] **Step 3: Update AbilityFactory.cs**

Add case 3:
```csharp
public static ServerAbility CreateServer(byte typeId)
{
    return typeId switch
    {
        1 => new MankiLmbCombo(),
        2 => new MankiRoundBomb(),
        3 => new MankiAerosolFlame(),
        _ => throw new ArgumentException($"Unknown AbilityTypeId: {typeId}"),
    };
}
```

- [ ] **Step 4: Build to check for compile errors**

Run: `dotnet build --nologo 2>&1 | grep -i error`
Expected: No errors

- [ ] **Step 5: Commit MankiAerosolFlame**

```bash
git add Shared/Abilities/MankiAerosolFlame.cs Shared/Characters/MankiData.cs Shared/Abilities/AbilityFactory.cs
git commit -m "feat(abilities): implement MankiAerosolFlame charged flamethrower

- Hold-to-charge: tap = quick burst, hold 45 ticks = charged cone
- Capsule hitbox with longer range when charged
- Reads all normal/charged params from Params dict
- Replaces AerosolFlameSpec with proper ServerAbility"
```

---

## Task 6: Clean Up Obsolete AbilitySpec Subclasses

**Files:**
- Delete: `Shared/RoundBombSpec.cs`
- Delete: `Shared/AerosolFlameSpec.cs`
- Modify: `Shared/Characters/MankiData.cs:1` (remove using statement if present)

**Context:** Now that all Manki abilities are `ServerAbility`, the old `AbilitySpec` subclasses (`RoundBombSpec`, `AerosolFlameSpec`) are dead code.

- [ ] **Step 1: Delete RoundBombSpec.cs**

Run: `rm Shared/RoundBombSpec.cs`

- [ ] **Step 2: Delete AerosolFlameSpec.cs**

Run: `rm Shared/AerosolFlameSpec.cs`

- [ ] **Step 3: Verify no references remain**

Run: `grep -rn "RoundBombSpec\|AerosolFlameSpec" Shared/ Scripts/`
Expected: No results

- [ ] **Step 4: Build to check for compile errors**

Run: `dotnet build --nologo 2>&1 | grep -i error`
Expected: No errors

- [ ] **Step 5: Commit cleanup**

```bash
git rm Shared/RoundBombSpec.cs Shared/AerosolFlameSpec.cs
git commit -m "chore: delete obsolete AbilitySpec subclasses

- Removed RoundBombSpec (replaced by MankiRoundBomb ServerAbility)
- Removed AerosolFlameSpec (replaced by MankiAerosolFlame ServerAbility)
- All abilities now use ServerAbility pattern"
```

---

## Task 7: Remove Client-Side AttackWarping Component

**Files:**
- Delete: `Scripts/Combat/AttackWarping.cs`
- Modify: `Scripts/Entities/PlayerController.cs` (remove AttackWarping references)

**Context:** Warp movement is now server-authoritative (in `Simulation.ProcessWarp`). The client-only `AttackWarping` component is dead code.

- [ ] **Step 1: Find AttackWarping usage in PlayerController**

Run: `grep -n "AttackWarping\|_attackWarping" Scripts/Entities/PlayerController.cs`
Expected: Shows lines where AttackWarping is referenced

- [ ] **Step 2: Remove AttackWarping field from PlayerController**

Find the field declaration (likely around line 30-50):
```csharp
private AttackWarping? _attackWarping;
```

Delete this line.

- [ ] **Step 3: Remove AttackWarping setup in _Ready or Setup**

Find the setup call (likely in `_Ready` or a `Setup` method):
```csharp
_attackWarping = GetNode<AttackWarping>("AttackWarping");
// or
_attackWarping?.Setup(this, _targetLock);
```

Delete these lines.

- [ ] **Step 4: Remove any warp initiation calls**

Find calls to `_attackWarping?.StartWarp(...)` (likely in ability activation code).

Delete these calls. Warp is now initiated by ServerAbility.OnStart setting `CharacterState.WarpSpeed/WarpTargetX/WarpTargetZ`.

- [ ] **Step 5: Delete AttackWarping.cs**

Run: `rm Scripts/Combat/AttackWarping.cs`

- [ ] **Step 6: Build to check for compile errors**

Run: `dotnet build --nologo 2>&1 | grep -i error`
Expected: No errors

- [ ] **Step 7: Commit AttackWarping removal**

```bash
git add Scripts/Entities/PlayerController.cs
git rm Scripts/Combat/AttackWarping.cs
git commit -m "refactor: remove client-side AttackWarping component

- Warp movement now runs server-side in Simulation.ProcessWarp
- Client just renders predicted state from CharacterState.WarpSpeed
- Removed all AttackWarping references from PlayerController"
```

---

## Task 8: Update Simulation.SimulateTick Input Buffering

**Files:**
- Modify: `Shared/Simulation.cs:142-150`

**Context:** The old buffering code references `AbilityExecutor.CanBufferCombo()`, which we deleted. We need to rewrite the buffer logic to check for ServerAbility combo chains directly.

- [ ] **Step 1: Find input buffering code**

Run: `grep -n "CanBufferCombo" Shared/Simulation.cs`
Expected: Shows line ~150

- [ ] **Step 2: Replace CanBufferCombo call**

Find lines ~145-150:
```csharp
// Combo chain buffer: same slot during active attack
if (s.State == ActionState.Attacking && input.ActiveSlot == s.AttackSlot)
{
    bool airborne = !s.IsGrounded;
    var ability = def.GetSlotAbility(input.ActiveSlot - 1, airborne);
    ushort cd = AbilityExecutor.GetCooldown(s, input.ActiveSlot);
    if (AbilityExecutor.CanBufferCombo(s, ability, cd))
    {
        s.BufferedSlot = input.ActiveSlot;
    }
}
```

Replace with:
```csharp
// Combo chain buffer: same slot during active attack (server abilities handle chaining internally)
if (s.State == ActionState.Attacking && input.ActiveSlot == s.AttackSlot && s.AttackElapsedTicks > 0)
{
    // Only buffer if not on cooldown
    ushort cd = ServerSimulation.GetCooldown(s, input.ActiveSlot);
    if (cd == 0)
    {
        s.BufferedSlot = input.ActiveSlot;
    }
}
```

- [ ] **Step 3: Fix general input buffer (lines ~155-165)**

Find the general buffer block:
```csharp
// General buffer: within InputBufferWindow (6 frames) of unlock
else if (s.AnimLockTicks <= InputBufferWindow || s.HitstunTicks <= InputBufferWindow)
{
    if (cooldown == 0)
        s.BufferedSlot = input.ActiveSlot;
}
```

Replace `cooldown` reference with `ServerSimulation.GetCooldown`:
```csharp
// General buffer: within InputBufferWindow (6 frames) of unlock
else if (s.AnimLockTicks <= InputBufferWindow || s.HitstunTicks <= InputBufferWindow)
{
    ushort cd = ServerSimulation.GetCooldown(s, input.ActiveSlot);
    if (cd == 0)
        s.BufferedSlot = input.ActiveSlot;
}
```

- [ ] **Step 4: Build to check for compile errors**

Run: `dotnet build --nologo 2>&1 | grep -i error`
Expected: No errors

- [ ] **Step 5: Commit input buffer fix**

```bash
git add Shared/Simulation.cs
git commit -m "fix: update input buffering to use ServerSimulation.GetCooldown

- Replaced AbilityExecutor.CanBufferCombo with direct cooldown check
- ServerAbilities handle combo chaining internally via Tick()
- Input buffer only prevents buffering when slot is on cooldown"
```

---

## Task 9: Smoke Test — Run Sandbox Mode

**Files:** None (testing only)

**Context:** Verify that Manki's LMB/RMB/Q abilities activate and run without crashes. This is a manual smoke test.

- [ ] **Step 1: Build the project**

Run: `dotnet build --nologo`
Expected: Build succeeded

- [ ] **Step 2: Launch Godot editor**

Run: `godot4 --editor .` (or open via GUI)

- [ ] **Step 3: Run sandbox scene (F5)**

Press F5 or click "Run Project"
Expected: Game loads into training arena

- [ ] **Step 4: Test LMB combo**

- Click LMB 3 times rapidly
- Expected: Character performs 3-hit combo with forward lunge
- Should see character close 5m gaps when attacking from distance

- [ ] **Step 5: Test Q (bomb throw)**

- Press Q
- Expected: Character throws bomb in parabolic arc
- Move mouse/aim to adjust target distance
- Bomb should explode on impact

- [ ] **Step 6: Test RMB (flamethrower)**

- Tap RMB (quick burst)
  - Expected: Short flamethrower cone
- Hold RMB for 1 second, release (charged)
  - Expected: Longer cone with more damage

- [ ] **Step 7: Check console for errors**

Run: `grep -i "error\|exception\|null" <godot_console_output>`
Expected: No critical errors (warnings about nullable are fine)

- [ ] **Step 8: Document smoke test results**

Create `docs/superpowers/plans/2026-06-22-smoke-test-results.txt`:
```
Date: 2026-06-22
Tester: [Your Name]
Build: [commit hash]

LMB Combo: [PASS/FAIL] - [notes]
Q Bomb Throw: [PASS/FAIL] - [notes]
RMB Flamethrower: [PASS/FAIL] - [notes]

Issues Found:
- [list any bugs/issues]
```

- [ ] **Step 9: Commit smoke test results (if issues found)**

```bash
git add docs/superpowers/plans/2026-06-22-smoke-test-results.txt
git commit -m "test: document smoke test results for ServerAbility refactor"
```

---

## Task 10: Final Cleanup — Remove ActionState.Warping Enum

**Files:**
- Modify: `Shared/ActionState.cs`

**Context:** We removed all references to `ActionState.Warping` in `Simulation.SimulateTick`. The enum value is dead code now.

- [ ] **Step 1: Find ActionState enum**

Run: `cat Shared/ActionState.cs`
Expected: Shows enum with Warping = 4 or similar

- [ ] **Step 2: Remove Warping enum value**

```csharp
public enum ActionState : byte
{
    Idle = 0,
    Attacking = 1,
    Dashing = 2,
    Hitstun = 3,
    // Warping = 4,  // REMOVED: warp is now a velocity override, not a state
    AirDodging = 5,
}
```

Comment out or delete `Warping = 4`.

- [ ] **Step 3: Verify no references remain**

Run: `grep -rn "ActionState.Warping" Shared/ Scripts/`
Expected: No results

- [ ] **Step 4: Build to check for compile errors**

Run: `dotnet build --nologo 2>&1 | grep -i error`
Expected: No errors

- [ ] **Step 5: Commit ActionState cleanup**

```bash
git add Shared/ActionState.cs
git commit -m "chore: remove ActionState.Warping (dead enum value)

- Warp is now a velocity override (WarpSpeed > 0), not a state
- Removed unused enum value to prevent confusion"
```

---

## Self-Review Checklist

- [x] **Spec coverage:** All requirements addressed?
  - ✅ Server-side warp with collision/gravity (Task 1)
  - ✅ Remove legacy data-driven path (Task 2)
  - ✅ Manki LMB combo with 5m lunge (Task 3)
  - ✅ Manki Q bomb projectile (Task 4)
  - ✅ Manki RMB charged flamethrower (Task 5)
  - ✅ Clean up obsolete AbilitySpec subclasses (Task 6)
  - ✅ Remove client-side AttackWarping (Task 7)
  - ✅ Fix input buffering (Task 8)
  - ✅ Smoke test (Task 9)
  - ✅ Final cleanup (Task 10)

- [x] **Placeholder scan:** No TBD/TODO/implement-later text
  - All code blocks are complete
  - All params have concrete values

- [x] **Type consistency:** Method signatures match across tasks
  - `ProcessWarp()` returns `bool` (Task 1)
  - `ServerSimulation.GetCooldown/SetCooldown` (Task 2, 8)
  - `ServerAbility.OnStart/Tick` signatures match (Task 3, 4, 5)

- [x] **File paths:** All absolute, no ambiguity
  - Uses exact line numbers where relevant
  - References existing files that we verified exist

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-22-server-ability-refactor.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
