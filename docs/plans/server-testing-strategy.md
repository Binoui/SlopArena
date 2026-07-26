# Server Testing Strategy: Exploration

> Brainstorming document — not a plan, not a spec. Lays out what's possible given the
> server-authoritative architecture, with tradeoffs and dependencies for each direction.

## Why This Matters

`ServerSimulation.Tick()` is pure C# math — zero Unity, zero I/O, zero randomness.
Every tick is deterministic given inputs. This means we can simulate entire matches
in a test, feed any input sequence, and inspect every byte of every entity's state.

We have ~22 test files and good unit coverage for abilities. But the architecture
unlocks things normal game projects can't do. The question is which to invest in.

---

## 1. Full-Match Simulation (Scenario Tests)

Simulate 300-600 ticks of actual back-and-forth gameplay between two characters
with canned input sequences. Assert on: who died, final positions, damage dealt,
cooldown states, interaction chains.

**Examples:**
- Manki LMB combo → FightGuy hitstun → Manki charges RMB → FightGuy dashes out → Manki fires Q bomb
- FightGuy air LMB → Manki recovers → both land → FightGuy dashes → LMB re-engage
- Both characters hold forward at each other: who wins the trade?

**Tradeoffs:**
- + Catches interactions between abilities (tick-order bugs, state leaks)
- + Documents real gameplay patterns as executable specs
- - Scenarios are time-consuming to write and maintain
- - Brittle: one timing change breaks dozens of scenarios
- - Input sequences are opaque — hard to tell what a 600-tick sequence is doing

**Effort:** Medium. Scenario runner is ~50 lines. Each scenario is 30-80 lines of boilerplate.

---

## 2. Property-Based / Invariant Tests

Run thousands of random input sequences and assert invariants that must always hold.
Uses FsCheck or a loop with random generation.

**Invariants worth checking:**
- Entity position never goes below `KillHeight` without dying and respawning
- `DamagePercent` never exceeds 999
- `AirTimeTicks` never overflows (guard exists at `ushort.MaxValue`)
- No two entities occupy intersecting space after any tick
- After N ticks of all-default input, entity state is fully deterministic (no float drift)
- ActionState transitions obey the state machine (e.g. no Attacking → Dashing without an interrupt path)

**Tradeoffs:**
- + Finds bugs you didn't think to look for
- + Extremely high ROI per line of test code
- + Catches regressions that scenario tests miss
- - Need to implement property generators for InputState sequences
- - Random failures are hard to reproduce without seed capture
- - Some invariants are genuinely hard to express (what's an illegal state?)

**Effort:** Low-Medium. Add FsCheck package, write 5-10 property tests. The real cost
is thinking of valid invariants.

---

## 3. Kit Regression Harness

Each character has a set of "signature scenarios" that capture their expected behavior.
When you tune MovementStats or change knockback math, the harness tells you every
shift instantly.

**Structure:**
```csharp
record KitScenario(
    string Name,
    CharacterDefinition Def,
    Action<CharacterState> Setup,       // position, facing
    InputSequence Inputs,               // per-tick inputs for this entity
    Action<CharacterState> Assert       // expectations on final state
);
```

**Per-character scenarios:**
- FightGuy (~6): LMB ground combo, air LMB, RMB charge variants, air RMB spike, dash→LMB warp
- Manki (~8): LMB combo, air LMB, RMB charge, air RMB spike, Q bomb short/charged, warp LMB, E bazooka

**Tradeoffs:**
- + Directly serves balance iteration: change one number, see what breaks
- + Each scenario is short (20-40 ticks), easy to understand and maintain
- + Clear pass/fail for each ability interaction
- - Only tests isolated ability uses, not full-match interactions
- - Per-character setup is manual: each new character needs scenarios

**Effort:** Medium-High for first character (infrastructure + scenarios), then Low per
additional character (copy pattern, change inputs/asserts).

---

## 4. Balance Snapshot (Golden File)

Simulate a fixed set of interactions, dump final state as JSON, check into git.
Diff tells you exactly what changed.

**What's in the snapshot:**
```json
{
  "scenario": "MankiLMB_vs_FightGuy_at_range_4",
  "tickCount": 120,
  "entities": {
    "1": { "PX": 1.2, "PZ": 0.3, "DamagePercent": 18, "Deaths": 0, ... },
    "100": { "PX": 4.5, "PZ": 0.1, "DamagePercent": 0, "Deaths": 0, ... }
  }
}
```

**Integration with Kit Harness:** The harness can optionally output golden files
instead of asserting inline. Review the diff, update golden file when changes are
intentional.

**Tradeoffs:**
- + One command tells you "this PR changed Manki's LMB damage by 15%"
- + Extremely easy to review in code review: see the golden diff
- + Zero assertion maintenance — the snapshot IS the assertion
- - Golden files desensitize: devs approve the diff without checking correctness
- - Fragile to noise: tiny animation timing changes produce big diff noise
- - Needs a mechanism to update goldens when changes are intentional

**Effort:** Low once regression harness exists. Add ~20 lines to write JSON per
scenario, plus a CLI flag or separate test run to regenerate goldens.

---

## 5. Agent-in-the-Loop Test Generation

An AI agent or CI bot generates test scenarios from natural language descriptions
and validates them against the simulation.

**How it works:**
1. Agent reads a character's kit spec (documentation + code)
2. Agent writes scenario: entity setup + input sequence + expected outcome
3. Scenario runs against simulation
4. If assertion fails, agent debug-loops: adjust inputs or expectations

**What it enables:**
- "Player at (0,0), enemy at (5,0), player holds forward and presses LMB → should warp to range and deal damage"
- "FightGuy at ledge (0,0), Manki at (3,0) using air RMB spike → FightGuy should bounce off ground"
- "Both characters jab at same time in range → should trade hits"

**Tradeoffs:**
- + Unlocks massive coverage with human-level reasoning
- + Adapts to new characters automatically (agent reads the kit)
- + Self-healing: agent can fix tests when behavior intentionally changes
- - Slow: each test generation loop takes 5-30 seconds
- - Flaky: agent may hallucinate impossible scenarios or wrong assertions
- - Needs infrastructure: agent needs a sandbox to compile + run + inspect results
- - Requires the regression harness (#3) as a foundation — the agent needs clean scenario primitives

**Effort:** High. Requires: regression harness (prerequisite), agent-execution sandbox,
prompt engineering, result validation. This is a multi-session build.

---

## 6. Simulation Fuzz Testing

Feed random valid inputs for thousands of ticks. Check postconditions every tick.
No assertions on gameplay correctness — just "doesn't crash, doesn't corrupt state."

**What it catches:**
- Crash-from-deep-state (null refs in edge-case state combinations)
- Ability lifecycle leaks (ability never calls OnEnd, stays active forever)
- Warp into void (warp target goes out of bounds)
- Hitstun lock states (character stuck in hitstun permanently)
- Float position drift (PX/PY/PZ accumulating small errors across thousands of ticks)

**Tradeoffs:**
- + Finds bugs nothing else finds
- + Extremely simple to implement (random InputState generator + loop)
- + Runs unattended in CI for any duration
- - Can't assert on gameplay correctness (no "this should have happened")
- - High false-positive rate: random inputs produce weird-but-legal states
- - Hard to reproduce: needs seed logging

**Effort:** Low. ~100 lines for the fuzzer + invariant checks. But interpreting
failures takes human time.

---

## Dependency Graph

```
Kit Regression Harness (#3)
  ├── Foundation for Agent-in-the-Loop (#5) — needs clean scenario primitives
  └── Foundation for Balance Snapshot (#4) — generates the scenarios to snapshot
    
Scenario Tests (#1) — independent, can start any time
Property Tests (#2) — independent, can start any time
Fuzz Testing (#6) — independent, can start any time
```

---

## What This Doc Is Not

This is not a plan. It's a menu. Each direction has a standalone "try it" cost
(low/medium/high) and a "live with it" cost (maintenance, drift, review burden).

When you want to commit to one, we write a spec — which scenarios, what assertions,
what infra, what goldens look like, how CI runs them. Until then, this sits in
`docs/plans/` as a reference.
