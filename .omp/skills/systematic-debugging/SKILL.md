---
name: systematic-debugging
description: "4-phase root cause debugging: understand bugs before fixing."
version: 1.1.0
author: Hermes Agent (adapted from obra/superpowers)
license: MIT
platforms: [linux, macos, windows]
metadata:
  hermes:
    tags: [debugging, troubleshooting, problem-solving, root-cause, investigation]
    related_skills: [test-driven-development, plan, subagent-driven-development]
---

# Systematic Debugging

## Overview

Random fixes waste time and create new bugs. Quick patches mask underlying issues.

**Core principle:** ALWAYS find root cause before attempting fixes. Symptom fixes are failure.

**Violating the letter of this process is violating the spirit of debugging.**

## The Iron Law

```
NO FIXES WITHOUT ROOT CAUSE INVESTIGATION FIRST
```

If you haven't completed Phase 1, you cannot propose fixes.

## When to Use

Use for ANY technical issue:
- Test failures
- Bugs in production
- Unexpected behavior
- Performance problems
- Build failures
- Integration issues

**Use this ESPECIALLY when:**
- Under time pressure (emergencies make guessing tempting)
- "Just one quick fix" seems obvious
- You've already tried multiple fixes
- Previous fix didn't work
- You don't fully understand the issue

**Don't skip when:**
- Issue seems simple (simple bugs have root causes too)
- You're in a hurry (rushing guarantees rework)
- Someone wants it fixed NOW (systematic is faster than thrashing)

## The Four Phases

You MUST complete each phase before proceeding to the next.

---

## Phase 1: Root Cause Investigation

**BEFORE attempting ANY fix:**

### 1. Read Error Messages Carefully

- Don't skip past errors or warnings
- They often contain the exact solution
- Read stack traces completely
- Note line numbers, file paths, error codes

**Action:** Use `read_file` on the relevant source files. Use `search_files` to find the error string in the codebase.

### 2. Reproduce Consistently

- Can you trigger it reliably?
- What are the exact steps?
- Does it happen every time?
- If not reproducible → gather more data, don't guess

**Action:** Use the `terminal` tool to run the failing test or trigger the bug:

```bash
# Run specific failing test
pytest tests/test_module.py::test_name -v

# Run with verbose output
pytest tests/test_module.py -v --tb=long
```

### 3. Check Recent Changes

- What changed that could cause this?
- Git diff, recent commits
- New dependencies, config changes

**Action:**

```bash
# Recent commits
git log --oneline -10

# Uncommitted changes
git diff

# Changes in specific file
git log -p --follow src/problematic_file.py | head -100
```

### 4a. Add Targeted Debug Logs to See Actual Values

When data flow involves **multiplexed paths** (the same variable is written from several callers, or a value passes through a pipeline where any stage could transform it), don't theorize about what the value IS — print it.

**Trigger examples from real sessions:**

```
// Bad: "I think dashDir is coming from _moveDirection"
// Good: print the actual value at the point of use

GD.Print($"[Dash] facingYaw={state.FacingYaw:F3} moveDir=({_moveDirection.X:F2},{_moveDirection.Z:F2}) dashDir=({dashDirX:F2},{dashDirZ:F2})");
```

One line revealed `dashDir=(-1.00,0.00)` (full left) while `moveDir=(0.00,0.96)` (nearly straight forward) — the bug was obvious: the forward component was hardcoded to 0 in another file, not in the direction computation.

**When to add a print** (any of these):
- A value is computed in one file and consumed in another
- A value is written from multiple call sites
- A value should be X but the symptom suggests Y
- A value is a function of other values and any could be wrong
- You're about to add a fix "because it must be this"

**Format:** Print BOTH the expected/correct value AND the actual value side by side, so the user (and you) see the delta immediately.

**Cost of skipping this step:** 4+ speculative fixes (ground buffer, rising edge, airFrames, vy check) when one print would have shown `dashDir=(-1,0)` in 10 seconds.

### 4b. Gather Evidence in Multi-Component Systems

**WHEN system has multiple components (API → service → database, CI → build → deploy):**

**BEFORE proposing fixes, add diagnostic instrumentation:**

For EACH component boundary:
- Log what data enters the component
- Log what data exits the component
- Verify environment/config propagation
- Check state at each layer

Run once to gather evidence showing WHERE it breaks.
THEN analyze evidence to identify the failing component.
THEN investigate that specific component.

### 5. Trace Data Flow

**WHEN error is deep in the call stack:**

- Where does the bad value originate?
- What called this function with the bad value?
- Keep tracing upstream until you find the source
- Fix at the source, not at the symptom

**Action:** Use `search_files` to trace references:

```python
# Find where the function is called
search_files("function_name(", path="src/", file_glob="*.py")

# Find where the variable is set
search_files("variable_name\\s*=", path="src/", file_glob="*.py")
```

### Phase 1 Completion Checklist

- [ ] Error messages fully read and understood
- [ ] Issue reproduced consistently
- [ ] Recent changes identified and reviewed
- [ ] Evidence gathered (logs, state, data flow)
- [ ] Problem isolated to specific component/code
- [ ] Root cause hypothesis formed

**STOP:** Do not proceed to Phase 2 until you understand WHY it's happening.

---

## Phase 2: Pattern Analysis

**Find the pattern before fixing:**

### 1. Find Working Examples

- Locate similar working code in the same codebase
- What works that's similar to what's broken?

**Action:** Use `search_files` to find comparable patterns:

```python
search_files("similar_pattern", path="src/", file_glob="*.py")
```

### 2. Compare Against References

- If implementing a pattern, read the reference implementation COMPLETELY
- Don't skim — read every line
- Understand the pattern fully before applying

### 3. Identify Differences

- What's different between working and broken?
- List every difference, however small
- Don't assume "that can't matter"

### 4. Understand Dependencies

- What other components does this need?
- What settings, config, environment?
- What assumptions does it make?

---

## Phase 3: Hypothesis and Testing

**Scientific method:**

### 1. Form a Single Hypothesis

- State clearly: "I think X is the root cause because Y"
- Write it down
- Be specific, not vague

### 1.b. Explain to User Before Fixing

This is the single most important step. **Skipping it erodes trust and wastes time.**

**Trigger phrases — when you hear any of these, you already violated this step:**
- "stop doing that", "step back", "explain before changing"
- "you're slowing us down", "tu ralentis"
- "ne fais pas des changements sans m'expliquer" (direct quote from session)
- "arrete de modif des trucs random en boucle"

Don't wait for the next occurrence to correct it — the correction is to ALWAYS explain before touching files. The user saying these in ANY language (English, French, etc.) is a first-class signal.

**Cost of violation (from real session):** 7+ back-and-forth rounds of speculative fixes (grounded buffer → rising edge → airFrames → vy check → buffer removal → scale change → tolerance) when one systematic trace would have found the root cause on the first pass.

**Before every fix, write the hypothesis in your response:**
- What is the root cause?
- Where exactly (file + line)?
- What specific change do you propose?
- What will you observe if the fix is correct?

**Wait for explicit approval** (e.g., "vas y", "go ahead", "yes") before editing any file. If the user says nothing or seems uncertain, DO NOT proceed — ask for confirmation. Silent patching erodes trust. A 30-second explanation upfront saves 10 minutes of undoing wrong assumptions.

This is especially important when the fix touches multiple files or changes architecture.

### 2. Test Minimally

- Make the SMALLEST possible change to test the hypothesis
- One variable at a time
- Don't fix multiple things at once

### 3. Verify Before Continuing

- Did it work? → Phase 4
- Didn't work? → Form NEW hypothesis
- DON'T add more fixes on top

### 4. When You Don't Know

- Say "I don't understand X"
- Don't pretend to know
- Ask the user for help
- Research more

---

## Phase 4: Implementation

**Fix the root cause, not the symptom:**

### 1. Create Failing Test Case

- Simplest possible reproduction
- Automated test if possible
- MUST have before fixing
- Use the `test-driven-development` skill

### 2. Implement Single Fix

- Address the root cause identified
- ONE change at a time
- No "while I'm here" improvements
- No bundled refactoring

### 3. Verify Fix

```bash
# Run the specific regression test
pytest tests/test_module.py::test_regression -v

# Run full suite — no regressions
pytest tests/ -q
```

### 4. If Fix Doesn't Work — The Rule of Three

- **STOP.**
- Count: How many fixes have you tried?
- If < 3: Return to Phase 1, re-analyze with new information
- **If ≥ 3: STOP and question the architecture (step 5 below)**
- DON'T attempt Fix #4 without architectural discussion

### 5. If 3+ Fixes Failed: Question Architecture

**Pattern indicating an architectural problem:**
- Each fix reveals new shared state/coupling in a different place
- Fixes require "massive refactoring" to implement
- Each fix creates new symptoms elsewhere

**STOP and question fundamentals:**
- Is this pattern fundamentally sound?
- Are we "sticking with it through sheer inertia"?
- Should we refactor the architecture vs. continue fixing symptoms?

**Discuss with the user before attempting more fixes.**

This is NOT a failed hypothesis — this is a wrong architecture.

---

## Red Flags — STOP and Follow Process

If you catch yourself thinking:
- "Quick fix for now, investigate later"
- "Just try changing X and see if it works"
- "Add multiple changes, run tests"
- "Skip the test, I'll manually verify"
- "It's probably X, let me fix that"
- "I don't fully understand but this might work"
- "Pattern says X but I'll adapt it differently"
- "Here are the main problems: [lists fixes without investigation]"
- Proposing solutions before tracing data flow
- **"One more fix attempt" (when already tried 2+)**
- **Each fix reveals a new problem in a different place**
- **Patching files without first explaining what you found**
- **Implementing without user approval**

**ALL of these mean: STOP. Return to Phase 1.**

**If 3+ fixes failed:** Question the architecture (Phase 4 step 5).

## Common Rationalizations

| Excuse | Reality |
|--------|---------|
| "Issue is simple, don't need process" | Simple issues have root causes too. Process is fast for simple bugs. |
| "Emergency, no time for process" | Systematic debugging is FASTER than guess-and-check thrashing. |
| "Just try this first, then investigate" | First fix sets the pattern. Do it right from the start. |
| "I'll write test after confirming fix works" | Untested fixes don't stick. Test first proves it. |
| "Multiple fixes at once saves time" | Can't isolate what worked. Causes new bugs. |
| "Reference too long, I'll adapt the pattern" | Partial understanding guarantees bugs. Read it completely. |
| "I see the problem, let me fix it" | Seeing symptoms ≠ understanding root cause. |
| "I found the bug, let me just patch it now" | You found the symptom. Explain first, get approval, THEN fix. Silent patching erodes trust. |

## Quick Reference

| Phase | Key Activities | Success Criteria |
|-------|---------------|------------------|
| **1. Root Cause** | Read errors, reproduce, check changes, gather evidence, trace data flow | Understand WHAT and WHY |
| **2. Pattern** | Find working examples, compare, identify differences | Know what's different |
| **3. Hypothesis** | Form theory, test minimally, one variable at a time | Confirmed or new hypothesis |
| **4. Implementation** | Create regression test, fix root cause, verify | Bug resolved, all tests pass |

## Hermes Agent Integration

### Investigation Tools

Use these Hermes tools during Phase 1:

- **`search_files`** — Find error strings, trace function calls, locate patterns
- **`read_file`** — Read source code with line numbers for precise analysis
- **`terminal`** — Run tests, check git history, reproduce bugs
- **`web_search`/`web_extract`** — Research error messages, library docs

### With delegate_task

For complex multi-component debugging, dispatch investigation subagents:

```python
delegate_task(
    goal="Investigate why [specific test/behavior] fails",
    context="""
    Follow systematic-debugging skill:
    1. Read the error message carefully
    2. Reproduce the issue
    3. Trace the data flow to find root cause
    4. Report findings — do NOT fix yet

    Error: [paste full error]
    File: [path to failing code]
    Test command: [exact command]
    """,
    toolsets=['terminal', 'file']
)
```

## SlopArena-Specific Reference Files

For detailed patterns discovered during SlopArena debugging sessions, see:
- `references/sloparena-ground-tolerance-debug.md` — Jump snapped back by large PlatformLandTolerance
- `references/sloparena-duplicate-jump-bug.md` — Double jump broken by dual jump processing in SimulateTick + ServerApp rebuild trap

## Godot-Specific Debugging Patterns

### Pattern: Embedded editor hides mouse events
When mouse motion and click events don't reach `_UnhandledInput` in the Godot editor, see:
- `references/godot-embedded-editor-input.md` — full diagnosis and `_Input`-based fix

### Pattern: Viewport vs window size + Godot -Z forward convention
When UI elements (crosshair, overlays) appear off-center or projectile/target directions are inverted, see:
- `references/godot-coordinate-conventions.md` — Window vs GetVisibleRect, Sin/Cos with -Z forward

### Pattern: Struct copy trap

When a C# struct field write doesn't persist: the culprit is almost always writing to a copy.

```
 component.State.Field = value;  // modifies a COPY, discarded
 ref var s = ref component.State; s.Field = value;  // modifies the ORIGINAL
```

**Fix:** Always use `ref var local = ref container.FieldName;` before writing to struct fields. The `ref` local gets a reference to the actual field in memory, not a copy.

```
// BROKEN — writes to discarded copy:
component.State.AnimLockTicks = 6;

// FIXED — writes to original:
ref var s = ref component.State;
s.AnimLockTicks = 6;
```

**Detection:** the value resets to default (0/false) next frame. All reads get the current value (fresh copy — reads are always correct), but writes vanish. Compare the struct definition — if `public struct X`, every field write through a class property/field returns a copy.

**Cross-file scan:** when a struct copy bug is found in one file, search EVERY file that writes to that struct's fields. In Godot, CharacterState is the typical struct that gets written from dozens of sites (PlayerController, AttackState, MovementComponent, states). Simulation uses `ref CharacterState s` (correct — it takes the parameter by reference), but non-simulation paths like `this.State.X = val` are all broken. Don't just fix the one you found — grep for the pattern systematically, because "one site has this bug" means every site written in the same style has it too.

**Systematic scan:** when a struct copy bug is found, search every file that modifies the struct's fields for the same pattern. In Godot, CharacterState is the typical struct that gets written from dozens of sites (PlayerController, AttackState, MovementComponent, Simulation). Simulation uses `ref CharacterState s` (correct), but MovementComponent methods often use `this.State.X = val` (wrong).

### Pattern: Dash direction overridden by FSM (also in _startDash)

When dash goes sideways but jump works fine:

1. The direction set in `DashState.SetDirection()` is passed from the **FSM layer** (PlayerController._PhysicsProcess) to `Movement.StartDash()` in `DashState.Enter()`.
2. But the **simulation** already called `StartDash()` during `SimulateTick()` — so the FSM overwrites the sim's result.
3. Root cause in this case: `SimulateTick` at line 127 called `StartDash(ref s, stats, input.MoveX, 0f)` — **MoveY hardcoded to 0**, so the forward component was always zero.

**Debugging:** Add a print showing `moveDir` (input) vs `dashDir` (computed) — the discrepancy will be immediately visible.

**Fix:** Both the simulation's call and the FSM's call need to pass `input.MoveY` correctly. Remove redundant `Movement.StartDash()` from `DashState.Enter()` since the simulation already handled it.

### Pattern: Duplicate AnimationTree node in .tscn (Godot Editor artifact)

When a `.tscn` file has TWO `AnimationTree` nodes — one at the root and one nested as a child of the first — it causes spurious warnings about `double AnimationTree` and `invalid root_node path` even when paths appear correct.

**Root cause:** An unknown Editor action (possibly copy-paste or undo/redo corruption) duplicated the AnimationTree node with parent set to the first AnimationTree instead of `.`.

**Detection:** Search `.tscn` for two `type=\"AnimationTree\"` entries. The second will have `parent=\"AnimationTree\"` instead of `parent=\".\"`.

**Fix:** Delete the nested duplicate (lines `[node name=\"AnimationTree\" ... parent=\"AnimationTree\"...]` through the next `[node` statement). The remaining AnimationTree with `parent=\".\"` is the correct one. No code changes needed — just clean up the resource file.

### Pattern: Jump immediately snapped back (ground tolerance too high)

When pressing space makes the FSM transition to 'air' but the character never leaves the ground (logs show count=3 landing within 1-3 frames of the 'air' transition), the culprit is almost always the simulation's `PlatformLandTolerance`.

**Root cause chain:**

1. Jump sets `VY = JumpForce` (e.g. 10).
2. Same tick: gravity reduces VY, then `PY += VY * TickDt` lifts the character ~15cm.
3. Ground check: `PY <= groundY + PlatformLandTolerance` — if tolerance is ≥ 15cm (e.g. 0.2m), `IsGrounded` stays `true`.
4. Since `IsGrounded` is true: `PY` snaps to `groundY`, `VY` snaps to 0. **The jump is erased.**
5. The AirState waits N grounded frames, then transitions to landing — happening 3 frames after the "jump".

**Fix:** Set `PlatformLandTolerance` small enough that the very first frame of a jump breaks it. Use `0.02f` (2cm).

**Detection:** Check the simulation's ground-check constants in `Shared/Simulation.cs`.
Always verify the simulation's IsGrounded actually changes before adding FSM-side guards.

**Broader implication:** When debugging "entity won't leave ground" or "jump lasts 0 frames", ALWAYS check ground detection tolerances and whether IsGrounded=true triggers a position/velocity snap.

### Pattern: Don't stack speculative fixes

When you add a grounded buffer, a minimum-airtime check, AND a rising-edge detector all for the same symptom (flickering landing), you create a patch stack where each layer interferes. The user saying "step back, stop making random changes" means you violated the systematic-debugging process. Revert ALL speculative patches, make ONE targeted fix with a clear hypothesis, test before adding a second layer.

### Pattern: _Process vs _PhysicsProcess race

When two values that should decrement together cause a premature state transition:

```
_Process (frame-rate)          _PhysicsProcess (physics-rate, e.g. 60Hz)
                              |
                   MovementComponent.Tick() -> TickTimers -> counterA--
                              |
                   TickStartup() -> counterB--, returns false
                              |
OnProcess: sees
counterA==0, counterB==0
-> premature transition!
```

**Fix:** add a guard variable checked in OnProcess() alongside the decrementing counters. The guard is set to true before the countdown and false only after resolution completes. This bridges the gap between the last decrement reaching 0 and the resolution logic running.

**General detection:** when a state transitions too early and the counters look correct in theory, it's likely a timing gap between _Process (C# FSM state update) and _PhysicsProcess (simulation + timer ticks). Add GD.Print with both values to both paths to see the exact order.

### Pattern: Premature combo/chain abort from stale timer

When a buffered-input combo system reuses a timer field that's always 0:

```
Simulation.TickTimers: if (s.ComboTimerTicks == 0) s.ComboStage = 0;
```

If ComboTimerTicks is always 0 (because the combo no longer uses timer-based windows), this resets the combo **every tick**. The symptom: chain inputs are accepted (buffered) but never advance to the next stage because ComboStage is reset to 0 before the buffer consumer reads it.

**Fix:** remove the timer-based expiry when switching to a purely input-buffered combo system. Let IdleState.Enter() (which sets ComboStage = 0) be the only reset point.

**Related:** when removing a timer/window system, audit ALL places that read or write the now-always-zero field — both in _Process/_PhysicsProcess (the C# side) AND in Simulation.TickTimers (the shared simulation side). Missing one creates a hard-to-find silent reset.

### Pattern: Animation-advance index off-by-one

When a combo system tracks stages via ComboStage, computing the animation index as ComboStage - 1 is correct for the first hit (stage 0 -> index 0) but WRONG for chain hits (stage 1 -> index 0 again -> always plays stage 1 anim).

**Fix:** index with ComboStage directly, not ComboStage - 1.

### Pattern: Event handler fires after state change

When an _UnhandledInput handler (e.g., attack button) calls a method that changes FSM state, the change takes effect immediately — then _Process runs the NEW state's OnProcess in the same frame. If the NEW state doesn't expect to be active yet, it may transition away.

**Detection:** add a GD.Print at the top of the suspect state's OnProcess. If the first print shows lock=0 after the state was just entered, the write didn't persist (struct copy trap) or was cleared before the state ran.

### Debugging Print Strategy for FSM Timing Bugs

When the FSM transitions too early but counters look correct in code:

1. Add prints to `SetPendingResolve` showing what value was set
2. Add prints to `OnProcess` showing what value is READ
3. Add prints to `Enter()`/`Exit()` to see state transitions
4. Run the game, observe the exact sequence

Key insight: the OnProcess print tells you what the state ACTUALLY sees. If lock=6 was set but OnProcess shows lock=0, the write was a struct copy. If lock=6 was set and OnProcess shows lock=5, the decrement path works but the race could still happen on the last tick. This is how you distinguish the two bug classes.

### With test-driven-development

When fixing bugs:
1. Write a test that reproduces the bug (RED)
2. Debug systematically to find root cause
3. Fix the root cause (GREEN)
4. The test proves the fix and prevents regression

## Real-World Impact

From debugging sessions:
- Systematic approach: 15-30 minutes to fix
- Random fixes approach: 2-3 hours of thrashing
- First-time fix rate: 95% vs 40%
- New bugs introduced: Near zero vs common

**No shortcuts. No guessing. Systematic always wins.**
