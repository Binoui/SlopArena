# Kit Regression Harness — Implementation Plan

Based on item **#3** from [server-testing-strategy.md](./server-testing-strategy.md).
Implementation target: a reusable scenario runner + per-character signature scenarios that capture expected ability behavior.

---

## Infrastructure

### `InputSequence` — per-entity input definition

A thin wrapper around a `Dictionary<int, InputState>` keyed by tick offset.
Default input is `default(InputState)` for unspecified ticks.

```csharp
public class InputSequence
{
    private readonly Dictionary<int, InputState> _inputs = new();

    public InputSequence Set(int tick, InputState input)
    {
        _inputs[tick] = input;
        return this;
    }

    public InputState ForTick(int tick) =>
        _inputs.TryGetValue(tick, out var v) ? v : default;
}
```

Convenience builders:

```csharp
// Single input at tick 0, rest default
InputSequence.OnePress(byte activeSlot)

// Builder: .Set(0, activeSlot: 1).Set(10, activeSlot: 1)
```

### `KitScenario` — scenario definition record

```csharp
public class KitScenario
{
    public string Name { get; init; }
    public CharacterDefinition Def { get; init; }
    public Action<CharacterState> Setup { get; init; }   // position, facing
    public InputSequence Inputs { get; init; }            // per-tick inputs
    public Action<CharacterState> Assert { get; init; }   // pass/fail on final state
}
```

### `ScenarioRunner` — execution engine

```csharp
public static class ScenarioRunner
{
    /// <summary>
    /// Run a self-contained kit scenario.
    /// Creates fresh arena + sim, spawns the entity, applies Setup,
    /// feeds Inputs for its duration, returns final state.
    /// </summary>
    public static CharacterState Run(KitScenario scenario, int totalTicks)
}
```

### Baked animation data

**Load from disk like the real server.** The .bin files are at `data/manki_skeleton.bin` / `data/fightguy_skeleton.bin` (project root).

`ScenarioRunner` adds a helper mirroring `MatchInstance.LoadBakedData`:

```csharp
// TestHelpers.cs
public static BakedAnimationData? LoadBakedData(CharacterDefinition def)
{
    if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
    string relative = def.BakedDataPath.Replace("res://", ""); // "data/manki_skeleton.bin"
    string path = Path.Combine("..", "..", relative);          // from test project dir
    if (!File.Exists(path)) return null;
    return BakedAnimationData.LoadFromBin(File.ReadAllBytes(path));
}
```

Then `ScenarioRunner.Run()` passes it:
```csharp
sim.RegisterEntity(1, scenario.Def, initialState, TestHelpers.LoadBakedData(scenario.Def));
```

This gives bone-accurate hurtbox resolution in tests, same fidelity as real matches.
No fallback to capsule approximations.

### Test base class

`KitScenarioTests` — abstract base with a `RunScenario` helper, so each scenario is a one-liner:

```csharp
public abstract class KitScenarioTests
{
    protected static ArenaDefinition Arena;
    protected static CharacterDefinition Manki;
    protected static CharacterDefinition FightGuy;
    protected static float MankiGpy, FightGuyGpy;

    /// <summary>
    /// Run a scenario and assert on final state.
    /// auto-computes totalTicks from max input tick + generous margin.
    /// </summary>
    protected static void AssertScenario(KitScenario scenario, int? overrideTotalTicks = null);
}
```

---

## File Structure

| File | Purpose |
|---|---|
| `tests/Shared.Tests/KitScenario.cs` | `KitScenario`, `InputSequence`, `ScenarioRunner` classes |
| `tests/Shared.Tests/KitScenarioTests.cs` | Abstract test base class |
| `tests/Shared.Tests/MankiKitRegressionTests.cs` | Manki's ~8 scenarios |
| `tests/Shared.Tests/FightGuyKitRegressionTests.cs` | FightGuy's ~6 scenarios |

**Effort:** ~200 lines for infrastructure, ~50 lines per character test file.

---

## Manki Scenarios (~8)

### 1. LMB Ground Combo — full chain

**Setup:** Player at (0, 0, 0), grounded, facing Z+.
**Inputs:** LMB (activeSlot=1) at tick 0, chain LMB at tick [stage end - 5], chain LMB at stage 2 end.
**Duration:** ~160 ticks (Stage1=40 + Stage2=35 + Stage3=45 + margin)
**Assert:** Final state = Idle. `DamagePercent` = 0 (no target). ActionState transitions: Idle → Attacking → Attacking → Attacking → Idle.

### 2. LMB Ground Combo — hit confirmed (vs dummy)

**Setup:** Player at (0, 0, 0), NPC at (2, 0, 0), facing each other. Both grounded.
**Inputs:** Player LMB at tick 0. NPC no input.
**Duration:** 50 ticks.
**Assert:** NPC `DamagePercent` ≥ 4 (stage 1 hit). NPC `HitstunTicks` > 0. Player in stage 2 (chained) or stage 1 recovery.

### 3. Air LMB — two-hit combo

**Setup:** Player airborne at (0, 1.5, 0), NPC at (2, 0, 0) on ground.
**Inputs:** Air LMB (activeSlot=1 while airborne) at tick 0, chain at stage end.
**Duration:** ~50 ticks.
**Assert:** Player lands after combo. Both stages fire (hitbox events trigger). NPC takes damage.

### 4. RMB Aerosol — normal uncharged flame

**Setup:** Player at (0, 0, 0), NPC at (2, 0, 0), facing Z+, both grounded.
**Inputs:** RMB (activeSlot=2) at tick 0. No charge hold (immediate release).
**Duration:** 20 ticks.
**Assert:** Flame hitbox spawns (NPC takes 8 damage). Damage upgrade: if there was a `ChargedStage` path, assert uncharged version used.

### 5. RMB Aerosol — charged flame

**Setup:** Same as 4.
**Inputs:** RMB (activeSlot=2) at tick 0. Hold (IsAiming=true) for 60 ticks (past 45 tick ChargeHoldTicks), then release.
**Duration:** 80 ticks.
**Assert:** Charged flame — bigger damage (14), bigger range. NPC hit confirms.

### 6. Air RMB — downward spike

**Setup:** Player airborne at (0, 3, 0), NPC grounded at (2, 0, 0).
**Inputs:** Air RMB (activeSlot=2 while airborne) at tick 0.
**Duration:** 40 ticks.
**Assert:** NPC is spiked — VY becomes negative. NPC bounces off ground (bounce/slide state).

### 7. Q Bomb — short throw (uncharged)

**Setup:** Player at (0, 0, 0), NPC at (5, 0, 0). Both grounded.
**Inputs:** Q (activeSlot=3) at tick 0, with IsAiming=true, AimYaw=0, AimDistance=500 (5m). No charge hold.
**Duration:** 100 ticks.
**Assert:** Projectile spawns. On arrival: explosion hitbox triggers. NPC takes explosion damage (10).

### 8. Overclock — self-buff

**Setup:** Player at (0, 0, 0), grounded.
**Inputs:** F (activeSlot=6) at tick 0.
**Duration:** 60 ticks.
**Assert:** `BuffRemainingTicks > 0`. Buff remains active for 480 ticks. No hit to entity (self-buff only).

---

## FightGuy Scenarios (~6)

### 1. LMB Ground Combo — full chain

**Setup:** Player at (0, 0, 0), grounded, facing Z+.
**Inputs:** LMB (activeSlot=1) at tick 0, chain LMB at three subsequent stage ends.
**Duration:** ~200 ticks (40+32+42+56 + margin).
**Assert:** Final state = Idle. All 4 stages execute (combo stage 0→1→2→3→reset). `DamagePercent` = 0 (no target).

### 2. LMB Ground Combo — hit confirmed (vs dummy)

**Setup:** Player at (0, 0, 0), NPC at (2, 0, 0), facing each other. Both grounded.
**Inputs:** Player LMB at tick 0. NPC no input.
**Duration:** 50 ticks.
**Assert:** NPC `DamagePercent` ≥ 4. NPC hitstun > 0.

### 3. Air LMB — rising kick into spike

**Setup:** Player airborne at (0, 1.5, 0), NPC at (2, 0, 0).
**Inputs:** Air LMB (activeSlot=1 while airborne) at tick 0, chain at stage end.
**Duration:** 70 ticks.
**Assert:** Stage 1 (rising two-hit) then stage 2 (downward spike). NPC takes all 3 hits (4+6+8=18 damage minimum).

### 4. RMB Uppercut — uncharged

**Setup:** Player at (0, 0, 0), NPC at (2, 0, 0), both grounded.
**Inputs:** RMB (activeSlot=2) at tick 0. No charge hold.
**Duration:** 45 ticks.
**Assert:** Uppercut hits — NPC takes 6 damage × 3 hits = 18. NPC launched upward (VY > 0).

### 5. RMB Uppercut — charged

**Setup:** Same as 4.
**Inputs:** RMB (activeSlot=2) at tick 0. Hold (IsAiming=true) for 200 ticks, then release.
**Duration:** 250 ticks.
**Assert:** Charged uppercut — NPC takes 14 damage × 3 hits = 42. Knockback is stronger (higher BaseKnockback).

### 6. Air RMB Helicopter — downward spike

**Setup:** Player airborne at (0, 3, 0), NPC at (2, 0, 0).
**Inputs:** Air RMB (activeSlot=2 while airborne) at tick 0.
**Duration:** 40 ticks.
**Assert:** NPC takes 7 damage. NPC spiked downward (VY negative) and bounces off ground.

---

## Implementation Order

1. **Infrastructure** — `InputSequence`, `KitScenario`, `ScenarioRunner` (~100 lines)
2. **Test base** — `KitScenarioTests` with `AssertScenario` helper (~50 lines)
3. **Manki scenarios** — pick 3 of the 8 first (LMB ground combo, Air LMB, RMB normal) to validate the runner works
4. **Remaining Manki scenarios** — add rest
5. **FightGuy scenarios** — all 6

---

## Key Design Questions

1. **Single-entity vs two-entity scenarios?** The current plan tracks only the primary entity's inputs. For hit-confirm scenarios (counting damage on NPC), the NPC gets `default(InputState)` throughout. If we later need both entities acting (e.g. "NPC dashes away"), we extend `InputSequence` to a two-player schema.

2. **Damage assertions — exact vs range?** Prefer range assertions (`≥` base damage) to avoid brittleness. Knockback scaling depends on `DamagePercent`, so a hit-confirmed scenario's second hit deals slightly more knockback than the first.

3. **How to handle chaining timing?** The chain window is `DurationTicks - ChainWindowTicks`. Buffer input at `stage.DurationTicks - stage.ChainWindowTicks + 1` to guarantee chain fires on stage end. (Mirrors `ChainToStage2` in MankiLmbTests.)
