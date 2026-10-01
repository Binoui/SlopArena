---
name: sim-test
description: Run SlopArena Shared.Tests with an optional filter. Rebuilds Shared DLL first. Usage — user types "/sim-test" or "/sim-test knockback" or "/sim-test MankiKit". Call this whenever testing sim behavior after a Shared change.
disable-model-invocation: true
---

# sim-test
Use [`docs/testing.md`](../../../docs/testing.md) to choose the verification mode that
requires the Shared suite.

Run the simulation test suite. Always rebuild Shared first so Unity and tests stay in sync.

## Usage

```
/sim-test               — full suite
/sim-test <filter>      — filter by test class or method name substring
```

## Steps

First resolve and validate the game checkout using the [execution-root rules](../orient/SKILL.md#execution-root). Read that section only; a test run does not require repeating session orientation. Run every command below with the shell tool's `cwd` set to the canonical game root. Never use the workspace's Git root as the test root, and never silently replace an explicit worktree.

1. Rebuild Shared:
   ```bash
   dotnet build src/Shared/ --nologo -v q
   ```
   Expected last line: `Build succeeded.`

2. Run tests (with optional filter):
   ```bash
   # No filter:
   dotnet test tests/Shared.Tests/ --nologo -v q

   # With filter (replace FILTER with the argument the user passed):
   dotnet test tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~FILTER" -v q
   ```
   Expected: `Passed: N` (where N is the number of tests run)

   If the filter matches nothing, dotnet exits 0 and prints:
   `No test matches the given testcase filter …`
   There will be no 'Passed!' line. Surface this to the user as "filter matched nothing" — do not treat it as a test failure.

3. If tests fail, read the full output from the original run, using its retained artifact if the inline output was truncated. Do not rerun the same failure just to recover verbose output. Analyse the assertion and the relevant source before reporting.

## Test files

All tests live in `tests/Shared.Tests/`. Key files:
- `CombatMathTests.cs` — knockback scaling, facing
- `CombatPipelineTests.cs` — full hit pipeline
- `ServerSimulationTests.cs` — tick-level sim
- `MankiKitTests.cs` / `MankiLmbTests.cs` — Manki ability coverage
- `SpellResolverTests.cs` — hitbox collision math
- `DashTests.cs`, `PhysicsTests.cs` — movement
