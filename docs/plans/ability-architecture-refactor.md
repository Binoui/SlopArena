# Ability Architecture Refactor — Implementation Plan

> **Goal**: Make each ability self-contained so creating a new ability only touches
> `AbilityData` + `Ability` class + `AbilityFactory` — no changes to PlayerController or Simulation.
>
> **Principle**: Abilities declare their own execution. Simulation calls them.
> PlayerController delegates to them. Neither owns ability-specific logic.
>
> **For subagents**: Follow steps in order. After each step, run `dotnet build`
> from the repo root. If a step says "verify", the build MUST pass before moving on.

---

## Step 1 — Create `Shared/AbilityExecutor.cs`

Create a new file. This is a pure C# static class — zero Godot dependencies.
It holds the ability execution logic that currently lives scattered in `Simulation.cs`.

**File:** `Shared/AbilityExecutor.cs`

```csharp
using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// Pure C# ability execution logic. Called by Simulation.SimulateTick.
    /// Each method takes a specific AbilityData instance — no slot→ability switches.
    /// </summary>
    public static class AbilityExecutor
    {
        /// <summary>
        /// Try to start an attack from the given ability.
        /// Handles both fresh attacks (comboStage=0) and combo chains (same slot, next stage).
        /// Returns true if an attack was started.
        /// </summary>
        public static bool TryStart(ref CharacterState s, AbilityData ability, byte slot, ushort cooldown)
        {
            if (cooldown > 0) return false;

            // Combo chain: same slot, next stage exists
            if (slot == s.AttackSlot && s.ComboStage < ability.Stages.Length - 1)
            {
                s.ComboStage++;
                var stage = ability.Stages[s.ComboStage];
                s.State = ActionState.Attacking;
                s.AnimLockTicks = stage.DurationTicks;
                s.AttackElapsedTicks = 0;
                return true;
            }

            // Fresh attack
            if (ability.Stages.Length > 0)
            {
                var stage = ability.Stages[0];
                s.State = ActionState.Attacking;
                s.AnimLockTicks = stage.DurationTicks;
                s.ComboStage = 0;
                s.AttackElapsedTicks = 0;
                s.AttackSlot = slot;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Process an active attack: check buffered/immediate chain advance,
        /// or transition back to idle when the animation lock expires.
        /// Returns true if the attack continued (chain advanced), false if it ended.
        /// </summary>
        public static bool ProcessActive(ref CharacterState s, AbilityData ability, ref InputState input)
        {
            if (s.AnimLockTicks > 0) return true; // still locked, nothing to do

            // 1. Buffered chain (click buffered during lock, set by CanBuffer check in Simulation)
            if (s.BufferedSlot == s.AttackSlot && s.ComboStage < ability.Stages.Length - 1)
            {
                s.BufferedSlot = 0;
                s.ComboStage++;
                var stage = ability.Stages[s.ComboStage];
                s.AnimLockTicks = stage.DurationTicks;
                s.AttackElapsedTicks = 0;
                return true;
            }

            // 2. Immediate chain (click on same frame lock expired)
            if (input.ActiveSlot == s.AttackSlot && s.ComboStage < ability.Stages.Length - 1)
            {
                input.ActiveSlot = 0; // consumed
                s.ComboStage++;
                var stage = ability.Stages[s.ComboStage];
                s.AnimLockTicks = stage.DurationTicks;
                s.AttackElapsedTicks = 0;
                return true;
            }

            // 3. No chaining → back to idle
            s.State = ActionState.Idle;
            s.ComboStage = 0;
            s.AttackSlot = 0;
            s.AttackElapsedTicks = 0;
            return false;
        }

        /// <summary>
        /// Check if an input can be buffered for this ability during an active attack.
        /// Returns true if the input should be stored in BufferedSlot.
        /// </summary>
        public static bool CanBufferCombo(CharacterState s, AbilityData ability, ushort cooldown)
        {
            // Only same-slot during active attack, and there's a next stage
            if (s.State != ActionState.Attacking) return false;
            if (s.AttackElapsedTicks == 0) return false; // don't buffer the click that started the attack
            if (cooldown > 0) return false;
            return s.ComboStage < ability.Stages.Length - 1;
        }

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
    }
}
```

> **Verify**: `dotnet build Shared/SlopArena.Shared.csproj` compiles.

---

## Step 2 — Add `GetAnimationName()` to `AbilityData`

Add a helper method on `AbilityData` so callers don't need to check
`AnimationNames` + `AimedCharge` every time they want an animation name.

**File:** `Shared/AttackData.cs`
**Find:** the closing `}` of the `AbilityData` struct (line 97, before `AimedChargeData`)
**Insert BEFORE the closing `}`:**

```csharp
        /// <summary>
        /// Get the animation name for a given combo stage.
        /// Falls back to AimedCharge.AttackAnimName, then "melee".
        /// </summary>
        public readonly string GetAnimationName(int comboStage)
        {
            if (AnimationNames != null && AnimationNames.Length > 0)
                return AnimationNames[comboStage % AnimationNames.Length];
            if (AimedCharge.HasValue && !string.IsNullOrEmpty(AimedCharge.Value.AttackAnimName))
                return AimedCharge.Value.AttackAnimName;
            return "melee";
        }
```

> **Verify**: `dotnet build Shared/SlopArena.Shared.csproj` compiles.

---

## Step 3 — Refactor `Simulation.cs`

Replace the slot-specific ability processing with calls to `AbilityExecutor`.

### Step 3a — Replace `ProcessAttack` method (lines 497-540)

**Find** the entire `ProcessAttack` method (from `private static void ProcessAttack` through its closing `}`).
**Replace** with:

```csharp
        // ── ATTACK PROCESSING ──

        private static void ProcessAttack(ref CharacterState s, CharacterDefinition def, ref InputState input)
        {
            if (s.AnimLockTicks > 0)
                return;

            bool airborne = !s.IsGrounded;
            var ability = def.GetSlotAbility(s.AttackSlot - 1, airborne);
            AbilityExecutor.ProcessActive(ref s, ability, ref input);
        }
```

### Step 3b — Replace `StartAttackFromSlot` method (lines 542-590)

**Find** the entire `StartAttackFromSlot` method (from `private static void StartAttackFromSlot` through its closing `}`).
**Replace** with:

```csharp
        /// <summary>Start an attack from a given slot (1-6). Handles combo chain if same slot.</summary>
        private static void StartAttackFromSlot(ref CharacterState s, CharacterDefinition def, byte slot)
        {
            bool airborne = !s.IsGrounded;
            var ability = def.GetSlotAbility(slot - 1, airborne);
            ushort cd = AbilityExecutor.GetCooldown(s, slot);
            AbilityExecutor.TryStart(ref s, ability, slot, cd);
        }
```

### Step 3c — Replace the combo buffer logic (lines 139-161)

**Find** the block starting with:
```csharp
            // Buffer input if locked within window
            if (input.ActiveSlot > 0 && (s.AnimLockTicks > 0 || s.HitstunTicks > 0) && s.BufferedSlot == 0)
            {
                // Combo chain: same slot during attack → always buffer
                // Don't buffer the click that STARTED the attack (elapsed==0)
                if (s.State == ActionState.Attacking && input.ActiveSlot == s.AttackSlot &&
                    s.AttackElapsedTicks > 0)
                {
                    var ability = input.ActiveSlot switch
                    {
                        1 => def.LMB,
                        2 => def.RMB,
                        3 => def.Q,
                        4 => def.E,
                        5 => def.R,
                        6 => def.F,
                        _ => def.LMB,
                    };
                    if (s.ComboStage < ability.Stages.Length - 1)
                    {
                        // Can't GD.Print here (shared lib), but buffer IS set
                        s.BufferedSlot = input.ActiveSlot;
                    }
                }
```

**Replace** the combo-specific check (keep the general buffer check after it) with:

```csharp
            // Buffer input if locked within window
            if (input.ActiveSlot > 0 && (s.AnimLockTicks > 0 || s.HitstunTicks > 0) && s.BufferedSlot == 0)
            {
                // Combo chain buffer: same slot during active attack
                if (s.State == ActionState.Attacking && input.ActiveSlot == s.AttackSlot)
                {
                    bool airborne = !s.IsGrounded;
                    var ability = def.GetSlotAbility(input.ActiveSlot - 1, airborne);
                    ushort cd = AbilityExecutor.GetCooldown(s, input.ActiveSlot);
                    if (AbilityExecutor.CanBufferCombo(s, ability, cd))
                        s.BufferedSlot = input.ActiveSlot;
                }
```

### Step 3d — Replace the general buffer cooldown lookup (lines 162-178)

**Find:**
```csharp
                // General buffer: within window of unlock
                else if ((s.AnimLockTicks > 0 && s.AnimLockTicks <= InputBufferWindow) ||
                         (s.HitstunTicks > 0 && s.HitstunTicks <= InputBufferWindow))
                {
                    ushort cd = input.ActiveSlot switch
                    {
                        1 => s.Cooldown0,
                        2 => s.Cooldown1,
                        3 => s.Cooldown2,
                        4 => s.Cooldown3,
                        5 => s.Cooldown4,
                        6 => s.Cooldown5,
                        _ => 0,
                    };
                    if (cd == 0)
                        s.BufferedSlot = input.ActiveSlot;
                }
            }
```

**Replace** with:

```csharp
                // General buffer: within window of unlock
                else if ((s.AnimLockTicks > 0 && s.AnimLockTicks <= InputBufferWindow) ||
                         (s.HitstunTicks > 0 && s.HitstunTicks <= InputBufferWindow))
                {
                    ushort cd = AbilityExecutor.GetCooldown(s, input.ActiveSlot);
                    if (cd == 0)
                        s.BufferedSlot = input.ActiveSlot;
                }
            }
```

> **Verify**: `dotnet build Shared/SlopArena.Shared.csproj` compiles.
> Also build the full solution: `dotnet build` from repo root.

---

## Step 4 — Clean `PlayerController._UnhandledInput`

Remove per-ability type checks. All slots use the same pattern:
create ability via factory, activate it.

### Step 4a — Add public camera yaw getter

**Find** in PlayerController.cs around line 166 (after `public byte GetComboStage()`):
```
    public byte GetComboStage() => _movementComponent.State.ComboStage;
    public ushort GetComboTimerTicks() => _movementComponent.State.ComboTimerTicks;
    public Vector3 MoveDirection => _moveDirection;
```

**After `MoveDirection` line, insert:**

```csharp
    /// <summary>Camera yaw in radians (0 = looks along -Z). For abilities that need initial aim.</summary>
    public float GetCameraYaw() => _camera != null ? _camera.GetCameraYaw() : GlobalRotation.Y;
```

### Step 4b — Replace the RMB handler (lines 536-559)

**Find** the RMB block:
```csharp
            if (mb.ButtonIndex == MouseButton.Right && _combatComponent != null)
            {
                if (mb.Pressed)
                {
                    if (_movementComponent.IsGrounded)
                    {
                        var ability = AbilityFactory.Create(_playerClass, 1, false, _charDef);
                        if (ability is AerosolFlame)
                        {
                            ActivateAbility(ability);
                            GetViewport().SetInputAsHandled(); return;
                        }
                    }
                    else
                    {
                        ActivateAbility(AbilityFactory.Create(_playerClass, 1, true, _charDef));
                        GetViewport().SetInputAsHandled(); return;
                    }
                    // Ground RMB without AimedCharge: direct attack
                    _pendingSlotPress = 2;
                    GetViewport().SetInputAsHandled(); return;
                }
                // RMB release is handled by active ability's Tick
            }
```

**Replace** with:

```csharp
            if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && _combatComponent != null)
            {
                bool airborne = !_movementComponent.IsGrounded;
                ActivateAbility(AbilityFactory.Create(_playerClass, 1, airborne, _charDef));
                GetViewport().SetInputAsHandled(); return;
            }
```

Note: The `mb.Pressed` check is now in the `if` condition instead of nested.
The RMB release is still handled by `_activeAbility.Tick()` in `_Process`.

### Step 4c — Replace the Q handler (lines 566-577)

**Find** the Q block:
```csharp
        if (Input.IsActionJustPressed("ability_q"))
        {
            var qAbility = AbilityFactory.Create(_playerClass, 2, false, _charDef);
            if (qAbility is RoundBomb bomb)
            {
                float aimYaw = _camera != null ? _camera.GetCameraYaw() + Mathf.Pi : GlobalRotation.Y;
                GD.Print($"[PlayerCtrl] Q hold: cameraYaw={_camera?.GetCameraYaw():F3} charYaw={GlobalRotation.Y:F3} using={aimYaw:F3}");
                bomb.SetInitialAim(aimYaw, this);
                ActivateAbility(bomb);
                GetViewport().SetInputAsHandled(); return;
            }
            _pendingSlotPress = 3;
        }
```

**Replace** with:

```csharp
        if (Input.IsActionJustPressed("ability_q"))
        {
            ActivateAbility(AbilityFactory.Create(_playerClass, 2, false, _charDef));
            GetViewport().SetInputAsHandled(); return;
        }
```

> **Verify**: `dotnet build` compiles.

---

## Step 5 — Update `RoundBomb` to self-setup in `OnActivate`

Currently `RoundBomb` receives `aimYaw` + `sceneRoot` via `SetInitialAim()` before activation.
After Step 4, this call is removed. The ability must set itself up in `OnActivate`.

**File:** `Scripts/Abilities/RoundBomb.cs`

### Step 5a — Remove `SetInitialAim` method (lines 29-34)

**Delete** the entire `SetInitialAim` method:
```csharp
    public void SetInitialAim(float yaw, Node sceneRoot)
    {
        _aimYaw = yaw;
        _targetDistance = Data.ProjectileConfig!.Value.MaxRange;
        _sceneRoot = sceneRoot;
    }
```

### Step 5b — Update `OnActivate` (lines 36-65)

**Find** the line `_fired = false;` inside `OnActivate`.
**After** that line, **insert** setup code before the ground raycast:

```csharp
    public override void OnActivate(PlayerController player)
    {
        _fired = false;

        // Self-setup: camera yaw + π offset (camera looks -Z, server AimYaw=0 means +Z)
        _aimYaw = player.GetCameraYaw() + Mathf.Pi;
        _targetDistance = Data.ProjectileConfig!.Value.MaxRange;
        _sceneRoot = player.GetParent(); // parent is the world/arena node
```

The rest of `OnActivate` stays the same (ground raycast, Show indicators, emission).

### Step 5c — Update `_UnhandledInput` mouse motion gate (lines 562-564)

After removing the RoundBomb special case, verify the mouse motion gate still works.
In `_UnhandledInput`, find:
```csharp
        if (@event is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured
            && _activeAbility == null)
            _camera?.RotateCamera(mm.Relative);
```

This already works: when an ability is active, `_activeAbility != null`, so camera rotation
is blocked. Mouse motion events are forwarded to the ability via `_activeAbility.OnInput(@event)`
at the top of `_UnhandledInput` (lines 514-517). No change needed.

> **Verify**: `dotnet build` compiles.

---

## Step 6 — Simplify `PlayerController._PhysicsProcess` animation selection

Replace the ability-specific animation name lookup with the new `GetAnimationName()` helper.

**File:** `Scripts/Entities/PlayerController.cs`

### Step 6a — First attack transition (lines 700-730)

**Find:**
```csharp
            // First attack: transition FSM to "attack"
            if (simState == ActionState.Attacking && !_fsm.IsInState("attack"))
            {
                // Sim started an attack — play the FSM animation
                byte comboStage = simComboStage;
                var ability = _charDef.GetSlotAbility(slot > 0 ? slot - 1 : 0, !_movementComponent.IsGrounded);
                string animName = "melee";
                if (ability.AnimationNames != null && ability.AnimationNames.Length > 0)
                {
                    animName = ability.AnimationNames[comboStage % ability.AnimationNames.Length];
                    if (ability.AimedCharge.HasValue)
                        animName = ability.AimedCharge.Value.AttackAnimName;
                }
                var attackState = _fsm.GetAttackState();
                if (attackState != null)
                {
                    attackState.NextAnimName = animName;
                    _fsm.TransitionTo("attack");
                }

                // Trigger special effects for this ability slot (projectiles, etc.)
                if (slot > 0)
                {
                    var spellAbility = _charDef.GetSlotAbility(slot - 1, !_movementComponent.IsGrounded);
                    if (spellAbility.SpecialEffectKeys != null)
                    {
                        foreach (var key in spellAbility.SpecialEffectKeys)
                            AbilityRegistry.Execute(key, _combatComponent!);
                    }
                }
            }
```

**Replace** with:

```csharp
            // First attack: transition FSM to "attack"
            if (simState == ActionState.Attacking && !_fsm.IsInState("attack"))
            {
                // Sim started an attack — play the FSM animation
                var ability = _charDef.GetSlotAbility(slot > 0 ? slot - 1 : 0, !_movementComponent.IsGrounded);
                string animName = ability.GetAnimationName(simComboStage);
                var attackState = _fsm.GetAttackState();
                if (attackState != null)
                {
                    attackState.NextAnimName = animName;
                    _fsm.TransitionTo("attack");
                }
            }
```

Note: SpecialEffectKeys triggering is removed here. It will be handled in Step 7.

### Step 6b — Combo chain animation change (lines 685-698)

**Find:**
```csharp
            // Combo chain: animation changed (same "attack" state, next stage)
            if (simState == ActionState.Attacking && _fsm.IsInState("attack") &&
                simComboStage != _lastComboStage && simComboStage > 0)
            {
                var ability = _charDef.GetSlotAbility(slot > 0 ? slot - 1 : 0, !_movementComponent.IsGrounded);
                string animName = ability.AnimationNames != null && ability.AnimationNames.Length > 0
                    ? ability.AnimationNames[simComboStage % ability.AnimationNames.Length] : "melee";
                if (ability.AimedCharge.HasValue)
                    animName = ability.AimedCharge.Value.AttackAnimName;
                var attackState = _fsm.GetAttackState();
                if (attackState != null)
                {
                    attackState.ChainTo(animName);
                }
            }
```

**Replace** with:

```csharp
            // Combo chain: animation changed (same "attack" state, next stage)
            if (simState == ActionState.Attacking && _fsm.IsInState("attack") &&
                simComboStage != _lastComboStage && simComboStage > 0)
            {
                var ability = _charDef.GetSlotAbility(slot > 0 ? slot - 1 : 0, !_movementComponent.IsGrounded);
                string animName = ability.GetAnimationName(simComboStage);
                var attackState = _fsm.GetAttackState();
                if (attackState != null)
                {
                    attackState.ChainTo(animName);
                }
            }
```

> **Verify**: `dotnet build` compiles.

---

## Step 7 — Move SpecialEffectKeys triggering to Ability.Tick()

Special effects (hitbox spawning VFX, projectiles) were triggered on FSM transition
to "attack" in `_PhysicsProcess`. Now they fire from the ability's own `Tick()`
when it sets `ActiveSlot`, before deactivating.

Since every instant ability already has a `_fired` guard and returns `ActiveSlot` on
the firing frame, we only need to add the effect trigger there.

### Step 7a — Add a protected helper to the Ability base class

**File:** `Scripts/Abilities/Ability.cs`

**After** the `OnDeactivate` method, **add**:

```csharp
    /// <summary>
    /// Trigger special effects registered for this ability.
    /// Call in Tick() when setting ActiveSlot to fire.
    /// </summary>
    protected void TriggerEffects(PlayerController player)
    {
        if (Data.SpecialEffectKeys == null) return;
        var combat = player.GetCombatComponent();
        if (combat == null) return;
        foreach (var key in Data.SpecialEffectKeys)
            AbilityRegistry.Execute(key, combat);
    }
```

### Step 7b — Update instant abilities to trigger effects

For each instant ability, add `TriggerEffects(player)` before returning
the `ActiveSlot` result. The files to change:

**`MonkeyCombo.cs`** (line 20):
Replace `return new AbilityInputState { ActiveSlot = SlotNumber };` with:
```csharp
        TriggerEffects(player);
        return new AbilityInputState { ActiveSlot = SlotNumber };
```

**`AirLMB.cs`** — same pattern. Find the `return new AbilityInputState { ActiveSlot = SlotNumber };` in Tick(), add `TriggerEffects(player);` before it.

**`AirRMB.cs`** — same.

**`DynamiteJump.cs`** (line 17) — same.

**`DiveBomb.cs`** — same.

**`BigBoom.cs`** — same.

**`SimpleAttack.cs`** (line 15) — same.

**`AerosolFlame.cs`** (line 67, where `_fired = true` and returns `ActiveSlot`):
Add `TriggerEffects(player);` before `return new AbilityInputState { ActiveSlot = SlotNumber };`.

**`RoundBomb.cs`** (lines 74-81, the release branch):
Add `TriggerEffects(player);` before `return new AbilityInputState { ActiveSlot = SlotNumber, ... };`.

For `RoundBomb`, the code block becomes:
```csharp
        if (!Input.IsActionPressed("ability_q"))
        {
            _fired = true;
            UpdateIndicators(player);
            TriggerEffects(player);
            return new AbilityInputState
            {
                ActiveSlot = SlotNumber,
                AimYaw = _aimYaw,
                AimDistance = (ushort)Mathf.Clamp(_targetDistance * 100f, 0f, 6500f),
            };
        }
```

For `AerosolFlame`, the code block becomes:
```csharp
        if (!Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _fired = true;
            TriggerEffects(player);
            return new AbilityInputState { ActiveSlot = SlotNumber };
        }
```

> **Verify**: `dotnet build` compiles. If an ability file doesn't have `TriggerEffects`,
> check that it was updated. Every ability that returns `ActiveSlot` from `Tick()`
> must call `TriggerEffects(player)` first.

---

## Step 8 — Fix NPC ability path (unify with player)

NPCs currently use `UseAbility()` → `ExecuteSlot()` which bypasses the sim.
Fix: NPCs use `_pendingSlotPress` like players do.

### Step 8a — Fix NPC `BuildInputState` to read `_pendingSlotPress`

**File:** `Scripts/Entities/PlayerController.cs`

**Find** in the NPC branch of `BuildInputState` (around line 795):
```csharp
            input.Crouch = false; // NPCs don't crouch for now

            // Set move direction for animations
            _moveDirection = new Vector3(moveX, 0f, moveY).Normalized();
            _snappedInputDirection = new Vector2(moveX, moveY);

            return input;
```

**Insert** after `input.Crouch` and before `_moveDirection`:

```csharp
            // NPC abilities use the same ActiveSlot pipeline as player
            input.ActiveSlot = _pendingSlotPress;
            _pendingSlotPress = 0;
```

### Step 8b — Fix NPC `UseAbility` to set `_pendingSlotPress`

**File:** `Scripts/Entities/PlayerController.cs`

**Find** `UseAbility` (line 1172):
```csharp
    public void UseAbility(int slot)
    {
        if (!_isNPC) return; // Only NPCs can have direct ability calls
        ExecuteSlot(slot, charged: false, airborne: !_movementComponent.IsGrounded);
    }
```

**Replace** with:

```csharp
    /// <summary>
    /// Trigger an ability by slot index (for NPCs / BotController).
    /// Sets _pendingSlotPress so the sim picks it up on the next tick,
    /// same as player abilities.
    /// </summary>
    public void UseAbility(int slot)
    {
        if (!_isNPC) return;
        _pendingSlotPress = (byte)(slot + 1); // slot is 0-based, ActiveSlot is 1-based
    }
```

### Step 8c — Update `BotController` (if it calls UseAbility with different conventions)

**Check** `Scripts/AI/BotController.cs` for how it calls `UseAbility`.
If it passes 0 for LMB or 1 for RMB (0-based), no change needed (step 8b handles +1).

> **Verify**: `dotnet build` compiles.

---

## Step 9 — Remove dead `ExecuteSlot` and `ExecuteAttackStage`

With NPCs using the sim path, `ExecuteSlot` and `ExecuteAttackStage` are dead code.
These are large methods (lines 896-988 and 1243-end) with slot→cooldown switches
and FSM manipulation that the sim now handles.

### Step 9a — Remove `ExecuteSlot`

**Find** and **delete** the entire `ExecuteSlot` method (lines 892-988, from the `/// <summary>` comment through its closing `}`).

### Step 9b — Remove `ExecuteAttackStage`

**Find** and **delete** the entire `ExecuteAttackStage` method (around line 1243, from its `/// <summary>` through closing `}`).

### Step 9c — Check for remaining callers

After deletion, run `dotnet build`. If any callers of `ExecuteSlot` or `ExecuteAttackStage`
remain (besides the already-updated `UseAbility`), the build will fail — fix them by updating
to use `_pendingSlotPress` or removing the dead call site.

> **Verify**: `dotnet build` compiles with zero errors.

---

## Step 10 — Final verification

### Build both projects

```bash
cd ~/Documents/projects/SlopArena
dotnet build                    # client + Shared
dotnet build Server/SlopArena.Server.csproj   # server
```

### Runtime test checklist

1. **LMB (MonkeyCombo)**: Press LMB → character attacks. Spam LMB → 3-hit combo chains.
   Damage numbers appear. Hitboxes visible with F3 debug.
2. **RMB (AerosolFlame)**: Hold RMB → cone indicator appears, charge builds.
   Release RMB → flame fires, damage applies.
3. **Q (RoundBomb)**: Press Q → ground circle + arc indicator appear.
   Move mouse → aim moves. Release Q → projectile flies along arc, explodes on impact.
4. **E (DynamiteJump)**: Press E → explosion VFX at feet, character knocked upward.
5. **R (DiveBomb)**: Press R → attack fires.
6. **F (BigBoom)**: Press F → large AoE attack fires.
7. **NPC abilities**: Training mode NPC attacks and abilities work correctly.
8. **No console errors**: No red GD.PrintErr or exceptions in Godot output console.

---

## Summary of changes

| File | Change |
|------|--------|
| `Shared/AbilityExecutor.cs` | **NEW** — pure C# ability execution methods |
| `Shared/AttackData.cs` | **+1 method** — `GetAnimationName()` on `AbilityData` |
| `Shared/Simulation.cs` | **~80 lines removed, ~40 added** — replace slot switches with AbilityExecutor calls |
| `Scripts/Entities/PlayerController.cs` | **~100 lines removed, ~15 added** — simplify _UnhandledInput, _PhysicsProcess, NPC path, remove ExecuteSlot |
| `Scripts/Abilities/Ability.cs` | **+1 method** — `TriggerEffects()` helper |
| `Scripts/Abilities/RoundBomb.cs` | **-1 method, +3 lines** — self-setup in OnActivate |
| `Scripts/Abilities/AerosolFlame.cs` | **+1 line** — TriggerEffects on fire |
| `Scripts/Abilities/MonkeyCombo.cs` | **+1 line** — TriggerEffects on fire |
| `Scripts/Abilities/AirLMB.cs` | **+1 line** — TriggerEffects on fire |
| `Scripts/Abilities/AirRMB.cs` | **+1 line** — TriggerEffects on fire |
| `Scripts/Abilities/DynamiteJump.cs` | **+1 line** — TriggerEffects on fire |
| `Scripts/Abilities/DiveBomb.cs` | **+1 line** — TriggerEffects on fire |
| `Scripts/Abilities/BigBoom.cs` | **+1 line** — TriggerEffects on fire |
| `Scripts/Abilities/SimpleAttack.cs` | **+1 line** — TriggerEffects on fire |

**Net effect**: ~200 lines removed (scattered slot switches + dead code),
~80 lines added (centralized AbilityExecutor + helpers).
Creating a new ability now touches 3 files instead of 6+.
