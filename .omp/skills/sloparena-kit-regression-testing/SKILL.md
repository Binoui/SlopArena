---
name: sloparena-kit-regression-testing
description: Write and maintain golden-snapshot regression tests for character kits (KitScenario, AssertGoldenScenario, REGENERATE_GOLDENS) in tests/Shared.Tests. Use whenever adding a new character/ability, changing damage/knockback/timing numbers, or the user asks to "pin down", "lock in", "add regression coverage for", or "add golden tests for" a kit — also trigger on any mention of REGENERATE_GOLDENS, KitScenario, or a failing/stale golden diff.
---

# SlopArena Kit Regression Testing

`tests/Shared.Tests/` has a golden-snapshot harness (`KitScenario` + `AssertGoldenScenario`) purpose-built for `ServerSimulation.Tick()` — pure C#, deterministic, no Unity, no RNG. It runs N ticks of canned input against a character (optionally vs. an NPC dummy) and diffs the resulting `CharacterState` against a committed JSON file in `tests/Shared.Tests/Golden/`. This is the standard way new abilities and characters get regression-protected; every existing kit (Manki, FightGuy, Kistu, Nilus) uses it.

**Nothing auto-discovers characters.** No harness enumerates `CharacterClass` — a new character or ability only gets covered when you hand-write its `<Character>KitRegressionTests.cs`. Don't assume adding a `CharacterDefinition` "just works" with existing tests.

## The pieces

| File | Role |
|---|---|
| `KitScenario.cs` | `KitScenario` record (Def, Setup, Inputs, SnapshotTick, TotalTicks, optional NpcSetup/NpcDef/NpcAssert); `InputSequence` (sparse per-tick input via `.Press(tick, slot)` / `.Set(tick, InputState)`); `ScenarioRunner.Run()` drives the sim loop. |
| `KitScenarioTests.cs` | Base class every `<Character>KitRegressionTests` extends. `AssertScenario` runs + calls your inline `Assert` — use for a single sharp numeric invariant. `AssertGoldenScenario` runs + diffs against the JSON file — use for broad kit-behavior pinning (the default). Also holds `MankiGpy`/`FightGuyGpy`/`NilusGpy` ground-Y helpers. |
| `GoldenSnapshot.cs` | `EntitySnapshot` — the gameplay-relevant subset of `CharacterState` that gets pinned (position, velocity, damage, deaths, combo stage, hitstun, airtime, charge, cooldowns×6, buff timer, jump/dash resources, invincibility). Deliberately excludes noisy/transient fields (raw input, facing yaw, warp data). Float fields compare to 3 decimal places, so animation-timing float drift doesn't false-fail. |
| `TestHelpers.cs` | `PlayerState()`/`NpcState()` (PY defaults to 0 — ungrounded, you must set `PY = Gpy`), `GroundPY(def)`, `CombatDef` (a Manki clone with a plain full-body capsule hurtbox — use as `NpcDef` for hit-confirm scenarios so you don't need baked skeleton data), `TestArena()`. |
| `Golden/*.json` | One file per scenario, named from `KitScenario.Name` (spaces/slashes → `_`). Renaming `Name` orphans the old file — delete it by hand. |

## Writing a new regression file

Create `tests/Shared.Tests/<Character>KitRegressionTests.cs` extending `KitScenarioTests`. One `[Fact]` per ability stage/branch you want pinned. Shape, from `FightGuyKitRegressionTests.cs`:

```csharp
public class KistuKitRegressionTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.KistuDef;
    private static float Gpy => TestHelpers.GroundPY(Def);

    [Fact]
    public void LMB_Stage1_HitsNpcForDamage()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Kistu LMB Hit Confirm",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },                     // golden covers assertions; leave empty
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 12,                      // comment WHY: stage1 hitbox active (trigger=6, dur=6)
            TotalTicks = 80,
        });
    }
}
```

**`SnapshotTick` is the whole point — get it from the ability spec, not by guessing.** It must land while the hitbox/effect is actually active (mid-ability), otherwise the golden pins a boring settled/idle state and the test stops meaningfully protecting anything. Look up the ability's trigger tick + duration and comment the reasoning inline (see the `// stage 1 hitbox active (trigger=7, dur=6)` style comments in the existing files) — the next person changing timing needs that context to know if their change should move the tick too.

Use `AssertScenario` + a hand-written `Assert.Equal(...)` instead of golden when you're locking down one specific invariant on genuinely new infrastructure (e.g. "self-damage is capped") rather than broad kit behavior — see `MankiKitTests.cs`'s bazooka self-damage test for the pattern. Golden scenarios are for "this kit behaves the same as before"; hand-written asserts are for "this one new mechanic does exactly X."

## The regenerate workflow (follow every step — skipping steps is how a wrong golden gets committed)

1. **Run filtered, expect FAIL.** `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~<Character>KitRegressionTests" --nologo`. Expected: `Golden file not found` for each new scenario. This proves the test is actually consulting the golden mechanism, not silently passing against nothing.
2. **Generate.** `REGENERATE_GOLDENS=1 dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~<Character>KitRegressionTests" --nologo`. Writes the JSON files, test run reports PASS (it always passes on generation — that's not a signal of correctness).
3. **Inspect before trusting — this is the step people skip.** Open every generated `Golden/*.json` and check the numbers against the character's design spec (`docs/characters/<name>.md`) or the ability's intended damage/knockback/distance. A golden that pins *wrong* behavior is worse than no golden at all — it actively defends a bug against future fixes. If a value contradicts the spec, fix the implementation, not the assertion, then regenerate.
4. **Re-run without the env var, expect PASS.** `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~<Character>KitRegressionTests" --nologo`. Now it's comparing against the committed file — confirms the harness round-trips (serializes/deserializes/compares) cleanly.
5. **Run the full suite.** `dotnet test tests/Shared.Tests/ --nologo`. Confirms your change didn't silently regress an unrelated kit's golden.
6. **Rebuild Shared if you touched sim code.** `dotnet build src/Shared/ --nologo` — Unity only sees the DLL, not the source.
7. **Commit the test file and its `Golden/*.json` together**, same commit as the implementation change. Reviewers read the JSON diff as the changelog ("NPC DamagePercent 4→6") — never split code and golden across commits.

## Updating goldens for an intentional behavior change

When a balance/timing change legitimately breaks an existing golden (test fails, diff shows real numbers moved): verify the new numbers are correct first, `REGENERATE_GOLDENS=1` scoped to just the affected test class, review the JSON diff line-by-line (this IS the code review artifact — "PZ changed from 2.1 to 2.5" tells the reviewer exactly what shifted), then commit the regenerated golden alongside the behavior change. Never regenerate broadly ("just to be safe") — a wide regenerate silently swallows unrelated regressions that should have failed loudly.

## Reference material

- `docs/plans/server-testing-implementation.md` — the original design rationale for this harness (why golden over hand-asserted, what's excluded from snapshots and why).
- `docs/plans/2026-07-28-nilus-implementation.md`, Task "Write the regression scenarios" (search `NilusKitRegressionTests`) — a full worked example of adding golden coverage for a brand-new character, step by step, including the exact commands and expected output at each step.
