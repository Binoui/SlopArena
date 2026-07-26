# Server Testing: Implementation Status

Tracking document for the [server-testing-strategy.md](./server-testing-strategy.md) directions.
Each section tracks one item from the strategy menu: implementation notes, acceptance criteria, and status.

---

## Implemented

### ✅ 2. Property-Based / Invariant Tests + 6. Simulation Fuzz Testing

**Merged into one test** — FsCheck-based deep-fuzz run that serves both roles.

**Implementation:**

`tests/Shared.Tests/SimulationInvariantTests.cs`
- Single `[Property(MaxTest = 1, EndSize = 500)]` test
- Two entities (player + NPC, both Manki) spawned at ground-level on a flat arena
- 500 ticks of random inputs per run
- **Invariants asserted every tick:**
  - No exceptions from `Tick()`
  - Valid `ActionState` enum
  - `DamagePercent` ∈ [0, 999]
  - `AirTimeTicks` not at overflow cap
  - Positions + velocities finite (no NaN/Inf drift)
  - Hitstun ≤ 60 ticks (stuck-state detection)
  - Entity below `KillHeight` must have `Deaths > 0`

**Random input generator:**
- Continuous MoveX/MoveY in [-1, 1]
- Low-probability digital inputs (3-12% per button)
- ~15% ability presses (uniform slot 1-6)
- ~10% aiming
- Full yaw/pitch/range/entity-target variation

**Seed capture:** FsCheck prints `Falsifiable, with seed: <N>` on failure. Re-run with `new PositiveInt(N)`.

**Run:**
```
dotnet test --filter "SimulationInvariantTests"
```

**Effort:** Low. ~120 lines of test code + 2 NuGet packages.

---

### ✅ 3. Kit Regression Harness

**Status:** Implemented (12 of ~14 scenarios from plan).

**Dependency:** Independent. Foundation for #4 and #5.

**Infrastructure:**

| File | Purpose |
|---|---|
| `tests/Shared.Tests/KitScenario.cs` | `InputSequence`, `KitScenario`, `ScenarioRunner` |
| `tests/Shared.Tests/KitScenarioTests.cs` | Abstract base with `AssertScenario` helper |

- Baked skeleton data loaded from disk via `TestHelpers.LoadBakedData()` (same as real server)
- `CharacterState` is a struct — `Setup` uses `Func<CharacterState>` with C# `with` expressions to avoid copy loss
- Optional NPC support via `NpcSetup`/`NpcAssert` for hit-confirm tests

**Scenario tests:**

| File | Scenarios |
|---|---|
| `tests/Shared.Tests/MankiKitRegressionTests.cs` | 7 scenarios |
| `tests/Shared.Tests/FightGuyKitRegressionTests.cs` | 5 scenarios |

**Manki (7):**
- `LMB_FullCombo_ReturnsToIdle` — 3-stage chain, state machine
- `LMB_HitConfirm_DealsDamageToNpc` — stage 1 hit connects, damage ≥ 4
- `AirLMB_FullCombo_ReturnsToIdle` — 2-stage airborne chain
- `RMB_Activation_EntersAttacking` — ChargeAttack hold+release executes
- `AirRMB_Activation_SetsAttacking` — airborne spike punch
- `Overclock_ActivatesBuff` — self-buff, `BuffRemainingTicks > 0`
- `Q_Activation_EntersAttacking` — AimedProjectile throw animation

**FightGuy (5):**
- `LMB_FullCombo_ReturnsToIdle` — 4-stage chain, state machine
- `LMB_HitConfirm_DealsDamageToNpc` — stage 1 hit connects, damage ≥ 4
- `AirLMB_FullCombo_ReturnsToIdle` — rising kick → spike
- `RMB_Uncharged_ReturnsToIdle` — ChargeAttack uppercut
- `AirRMB_Activation_ReturnsToIdle` — Helicopter spike

**Run:**
```
dotnet test --filter "KitRegression"
```

**Not covered (from plan):** Manki RMB charged variant, Manki Q bomb NPC hit-confirm. Both need complex charge/release timing or projectile travel that doesn't fit the current single-focus scenario runner well.

**Effort:** ~200 lines infrastructure + ~200 lines scenarios.
---

## Backlog

### ◻ 1. Full-Match Scenario Tests

**Status:** Not started. Independent — can start any time.

Simulate 300-600 ticks of canned back-and-forth gameplay.

**Tradeoff:** Most brittle of all options, but catches cross-ability interactions.

### ✅ 4. Balance Snapshot (Golden File)

**Status:** Implemented on top of #3.

**Implementation:**

| File | Purpose |
|---|---|
| `tests/Shared.Tests/GoldenSnapshot.cs` | `EntitySnapshot`, `StateSnapshot`, serialization + file I/O |
| `tests/Shared.Tests/KitScenarioTests.cs` | `AssertGoldenScenario()` — compare or regenerate |
| `tests/Shared.Tests/Golden/*.json` | 12 golden files, one per scenario |

**How it works:**
- `AssertGoldenScenario()` runs the scenario, captures final entity state as `StateSnapshot`
- Compares field-by-field against the golden file (`tests/Shared.Tests/Golden/{Name}.json`)
- Fields: position, velocity, damage, deaths, combo stage, hitstun, airtime, charge, cooldowns, buffs, jump/dash resources, invincibility
- Transient fields (input state, facing angles, warp data, knockback velocity) excluded from snapshot
- Float comparisons use 3 decimal precision to filter epsilon noise
- Fields at default values are omitted from JSON (`JsonIgnoreCondition.WhenWritingDefault`)

**Regenerate goldens:**
```
REGENERATE_GOLDENS=1 dotnet test --filter "KitRegression"
```

**On intentional behavior changes:**
1. Verify the change is correct
2. Run with `REGENERATE_GOLDENS=1` to rewrite golden files
3. Commit updated goldens alongside the code change
4. PR review includes the golden diff ("PX changed from 2.1 to 2.5")

**Effort:** ~100 lines infrastructure. Zero changes to existing scenario definitions.

## Dependency Graph (from strategy doc)

```
Kit Regression Harness (#3)
  ├── Foundation for Agent-in-the-Loop (#5)
  └── Foundation for Balance Snapshot (#4)
    
Scenario Tests (#1) — independent
Property+Fuzz Tests (#2/#6) ✅ — independent, DONE
```
