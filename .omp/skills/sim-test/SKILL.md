---
name: sim-test
description: Run SlopArena Shared.Tests with an optional filter. Rebuilds Shared DLL first. Usage — user types "/sim-test" or "/sim-test knockback" or "/sim-test MankiKit". Call this whenever testing sim behavior after a Shared change.
disable-model-invocation: true
---

# sim-test

Run the simulation test suite. Always rebuild Shared first so Unity and tests stay in sync.

## Usage

```
/sim-test               — full suite
/sim-test <filter>      — filter by test class or method name substring
```

## Steps

1. Rebuild Shared:
   ```bash
   dotnet build ~/Projects/SlopArena/src/Shared/ --nologo -v q
   ```
   Expected last line: `Build succeeded.`

2. Run tests (with optional filter):
   ```bash
   # No filter:
   dotnet test ~/Projects/SlopArena/tests/Shared.Tests/ --nologo -v q 2>&1

   # With filter (replace FILTER with the argument the user passed):
   dotnet test ~/Projects/SlopArena/tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~FILTER" -v q 2>&1
   ```
   Expected: `Passed: N` (where N is the number of tests run)

   If the filter matches nothing, dotnet exits 0 and prints:
   `No test matches the given testcase filter …`
   There will be no 'Passed!' line. Surface this to the user as "filter matched nothing" — do not treat it as a test failure.

3. If tests fail, print the full output:

   ```bash
   # Full suite (no filter was used):
   dotnet test ~/Projects/SlopArena/tests/Shared.Tests/ --nologo 2>&1

   # Filtered run:
   dotnet test ~/Projects/SlopArena/tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~FILTER" 2>&1
   ```
   Then analyse the assertion and the relevant source file before reporting back.

## Test files

All tests live in `tests/Shared.Tests/`. Key files:
- `CombatMathTests.cs` — knockback scaling, facing
- `CombatPipelineTests.cs` — full hit pipeline
- `ServerSimulationTests.cs` — tick-level sim
- `MankiKitTests.cs` / `MankiLmbTests.cs` — Manki ability coverage
- `SpellResolverTests.cs` — hitbox collision math
- `DashTests.cs`, `PhysicsTests.cs` — movement
