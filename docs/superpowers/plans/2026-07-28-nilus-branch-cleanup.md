# Nilus Branch Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the three remaining issues from the Nilus branch review: the AnimLockTicks trap in KistuRisingSlash and KistuCounter, and jump not being gated on AnimLockTicks engine-wide.

**Architecture:** Three minimal fixes — two in Kistu abilities (cache `_duration` at OnStart, compare `_ticks` against it instead of `s.AnimLockTicks`), one in Simulation.cs (add `s.AnimLockTicks == 0` to the jump gate). After fix, rebuild Shared DLL and run the full test suite.

**Tech Stack:** C# netstandard2.1 (Shared), xUnit (tests), `dotnet build` + `dotnet test`

## Global Constraints

- Shared/ is pure C# with zero Unity dependencies. No `UnityEngine.*` imports.
- All tick durations use `ushort`.
- Allman braces, tabs, `_camelCase` privates.
- Server-authoritative: server simulation is source of truth.
- `dotnet build src/Shared/ --nologo` after every Shared change.

---

### Task 1: Fix AnimLockTicks trap in KistuRisingSlash

**Files:**
- Modify: `src/Shared/Abilities/KistuRisingSlash.cs`

**Interfaces:**
- Consumes: `CharacterDefinition.GetSlotAbility(Slot, airborne: false)?.Stages[0].DurationTicks`
- Produces: `_duration` field (ushort), cached at OnStart, used in Tick for end-of-life check

**Problem:** `_ticks >= s.AnimLockTicks` compares an incrementing counter against a decrementing one (TickTimers decrements AnimLockTicks every tick at Simulation.cs:405). The two cross at half the stage duration, so the ability ends at tick 12 of an authored 24. The `riseTicks` param is 18 — unreachable, since the instance dies at tick 12.

**Fix:** Cache `_duration` at `OnStart`, compare `_ticks >= _duration` in `Tick`. Model: `KistuUltFlurry.cs:53` and `AirRmbAttack.cs:59`.

- [ ] **Step 1: Add `_duration` field**

In `KistuRisingSlash.cs`, add a private `ushort _duration` field alongside `_ticks`.

```csharp
private ushort _ticks;
private ushort _duration;
```

- [ ] **Step 2: Cache duration in OnStart**

After setting `s.AnimLockTicks`, cache the same value in `_duration`:

```csharp
var spec = def.GetSlotAbility(Slot, airborne: false);
_duration = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)24;
s.AnimLockTicks = _duration;
```

- [ ] **Step 3: Replace end-of-life check in Tick**

Change line 77 from:
```csharp
if (_ticks >= s.AnimLockTicks)
```
to:
```csharp
if (_ticks >= _duration)
```

- [ ] **Step 4: Rebuild Shared**

Run: `dotnet build src/Shared/ --nologo`
Expected: Build succeeded, 0 errors. Pre-existing warnings only.

---

### Task 2: Fix AnimLockTicks trap in KistuCounter

**Files:**
- Modify: `src/Shared/Abilities/KistuCounter.cs`

**Interfaces:**
- Consumes: `GetParam(def, "duration", 40f)` (already called in OnStart)
- Produces: `_duration` field (ushort), cached at OnStart, used in Tick for end-of-life check

**Problem:** Same trap as RisingSlash. `_ticks >= s.AnimLockTicks` crosses at tick 20 of 40. The counter window is `window_start=4` to `window_end=18`, so the window is reachable, but the ability ends at tick 20 instead of 40, giving the same recovery as a whiff even on a successful counter.

**Fix:** Same pattern — cache `_duration` at `OnStart`, compare against it in `Tick`.

- [ ] **Step 1: Add `_duration` field**

In `KistuCounter.cs`, alongside the existing `_ticks`:

```csharp
private ushort _ticks;
private ushort _duration;
private bool _countered;
```

- [ ] **Step 2: Cache duration in OnStart**

Replace the line setting `s.AnimLockTicks`:

```csharp
_duration = (ushort)GetParam(def, "duration", 40f);
s.AnimLockTicks = _duration;
```

- [ ] **Step 3: Replace end-of-life check in Tick**

Change line 60 from:
```csharp
if (_ticks >= s.AnimLockTicks)
```
to:
```csharp
if (_ticks >= _duration)
```

- [ ] **Step 4: Rebuild Shared**

Run: `dotnet build src/Shared/ --nologo`
Expected: Build succeeded, 0 errors.

---

### Task 3: Gate jump on AnimLockTicks

**Files:**
- Modify: `src/Shared/Simulation.cs:220`

**Interfaces:**
- Consumes: `s.AnimLockTicks` (decremented by TickTimers at Simulation.cs:405)
- Produces: Jump blocked during ability commitments

**Problem:** Line 220 only gates jump on hitstun and jump squat state. Dash is gated on `AnimLockTicks == 0` at line 252. Jump unconditionally fires during any committed ability, setting `State = JumpSquat`, which causes `TickAbilities` to drop the ability instance without `OnEnd` while still charging the full cooldown.

**Fix:** Add `s.AnimLockTicks == 0` to the jump condition at line 220.

- [ ] **Step 1: Add AnimLockTicks gate to jump**

Change line 220 from:
```csharp
if (input.Jump && s.JumpsLeft > 0 && s.HitstunTicks == 0 && s.State != ActionState.JumpSquat)
```
to:
```csharp
if (input.Jump && s.JumpsLeft > 0 && s.AnimLockTicks == 0 && s.HitstunTicks == 0 && s.State != ActionState.JumpSquat)
```

- [ ] **Step 2: Update the JumpBlocked debug log reason**

The `else if (input.Jump)` block at line 243 builds a reason string for blocked jumps. Add the `AnimLockTicks` case so blocked jumps during abilities are logged properly:

Change lines 243-249 from:
```csharp
else if (input.Jump)
{
    string reason = s.HitstunTicks > 0 ? "hitstun" :
        s.State == ActionState.JumpSquat ? "already_squatting" :
        s.JumpsLeft <= 0 ? "no_jumps" : "unknown";
    OnDebugLog?.Invoke($"[JumpBlocked] input.Jump=true but blocked by {reason}");
}
```
to:
```csharp
else if (input.Jump)
{
    string reason = s.AnimLockTicks > 0 ? "anim_lock" :
        s.HitstunTicks > 0 ? "hitstun" :
        s.State == ActionState.JumpSquat ? "already_squatting" :
        s.JumpsLeft <= 0 ? "no_jumps" : "unknown";
    OnDebugLog?.Invoke($"[JumpBlocked] input.Jump=true but blocked by {reason}");
}
```

- [ ] **Step 3: Rebuild Shared**

Run: `dotnet build src/Shared/ --nologo`
Expected: Build succeeded, 0 errors.

---

### Task 4: Run full test suite

**Files:**
- Tests: `tests/Shared.Tests/`

- [ ] **Step 1: Run all tests**

```bash
dotnet test tests/Shared.Tests/ --nologo
```

Expected: All tests pass (366+ tests, 0 failures). Pay special attention to:
- Any Kistu-related test that might have relied on the half-duration behavior
- `F_JumpCancelsTheUltAtFullCooldownCost` — this test was written to pin the PRE-FIX behavior (jump cancels ult). After the fix, this test SHOULD FAIL because jump is now gated. Update the test to assert the new correct behavior: jump is blocked during F, Nilus stays in Attacking.

- [ ] **Step 2: Update F_JumpCancelsTheUltAtFullCooldownCost test**

If this test exists in `NilusAbilityTests.cs`, find it and update the assertions:
- Before fix: jump cancels F, Nilus goes to Idle/JumpSquat, cooldown still charged
- After fix: jump is blocked during F, Nilus stays in Attacking, F completes normally

If the test does NOT exist (the paste said it was "outstanding"), write it:

```csharp
[Fact]
public void F_JumpIsBlocked_DuringEventHorizon()
{
    var sim = SimWithPlayer();
    sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } }); // Activate F

    // Tick through windup into drag phase (tick 80), press jump
    for (int i = 0; i < 80; i++)
        sim.Tick(new() { { 1, TestHelpers.Input(jump: true) } });

    var s = sim.GetState(1);
    // Jump is blocked by AnimLockTicks > 0; Nilus stays in Attacking
    Assert.Equal(ActionState.Attacking, s.State);
    Assert.Equal((byte)5, s.AttackSlot);
}
```

- [ ] **Step 3: Run tests again after any test updates**

```bash
dotnet test tests/Shared.Tests/ --nologo
```

Expected: All tests pass.

---

### Task 5: Commit and verify

- [ ] **Step 1: Commit all changes**

```bash
git add src/Shared/Abilities/KistuRisingSlash.cs src/Shared/Abilities/KistuCounter.cs src/Shared/Simulation.cs tests/Shared.Tests/
git commit -m "fix: gate jump on AnimLockTicks, fix half-duration trap in Kistu abilities

KistuRisingSlash and KistuCounter compared _ticks >= s.AnimLockTicks
(incrementing counter vs decrementing counter), ending at half the
authored duration. Cache _duration at OnStart and compare against it.

Jump was not gated on AnimLockTicks (only dash was), so pressing jump
during any committed ability cancelled it at full cooldown cost. Add
s.AnimLockTicks == 0 to the jump condition, same as the dash gate."
```

- [ ] **Step 2: Final verification**

```bash
dotnet build src/Shared/ --nologo && dotnet test tests/Shared.Tests/ --nologo
```

Expected: Build 0 errors, all tests pass.
