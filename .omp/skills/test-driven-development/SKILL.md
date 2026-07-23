---
name: test-driven-development
description: Use when implementing features or fixing bugs test-first, or when the user mentions "red-green-refactor", "write a test first", or "TDD".
---

# Test-Driven Development

TDD is the red → green loop. This skill defines what makes that loop produce tests worth keeping.

When exploring the codebase, respect existing test conventions in `tests/Shared.Tests/`. Match naming, structure, and seam choices already present.

## What a good test is

Tests verify behavior through public interfaces, not implementation details. Code can change entirely; tests shouldn't. A good test reads like a specification: "player takes knockback when hit at 50%" tells you exactly what capability exists. It survives refactors because it doesn't care about internal structure.

**Expected values must come from an independent source of truth** — a known-good literal, a worked example, the spec. Never recompute the expected value the same way the code does (`expect(add(a, b)).toBe(a + b)` is tautological).

## Seams — where tests go

A **seam** is the public boundary you test at. Tests live at seams, never against internals.

**Confirm the seam before writing the test.** Ask: "What's the public interface, and which seams should we test?" No test is written at an unconfirmed seam. In SlopArena this typically means:
- `ServerSimulation.Tick()` for simulation behavior
- `CombatMath` / `SpellResolver` static methods for hit detection
- Packet serialization round-trips for netcode

## Anti-patterns

- **Implementation-coupled** — mocks internal collaborators, tests private methods, or verifies through a side channel. The tell: the test breaks on refactor but behavior hasn't changed.
- **Tautological** — the assertion recomputes expected the same way the code does. Passes by construction, can never catch a bug.
- **Horizontal slicing** — writing all tests first, then all implementation. Work in **vertical slices** instead: one test → one implementation → repeat.

## Rules of the loop

1. **Red before green.** Write the failing test first. Run it. Watch it fail. Then write only enough code to pass it.
2. **One slice at a time.** One seam, one test, one minimal implementation per cycle.
3. **Refactoring is not part of the loop.** It belongs after the green, during review — not during the red→green cycle.

Run the single test file after each cycle. Run the full suite (`dotnet test tests/Shared.Tests/`) once at the end.
