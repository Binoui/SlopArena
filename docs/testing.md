# Simulation Unit Tests

> **The fastest feedback loop.** All simulation logic in `src/Shared/` is pure C# —
> testable via xUnit without Unity, a server, or any runtime. Build + test takes <3s.
>
> **Run this first after every `src/Shared/` change.**

## Running

```bash
# From repo root — build + test in one step
dotnet test tests/Shared.Tests/ --nologo

# Run a specific test category
dotnet test tests/Shared.Tests/ --nologo --filter "PhysicsTests"
dotnet test tests/Shared.Tests/ --nologo --filter "AbilityLifecycle"
dotnet test tests/Shared.Tests/ --nologo --filter "ServerSimulationTests"

# Run all tests: 63+ across 5 test suites (existing: ServerSimulation, SpellResolver,
# CombatMath; new: PhysicsTests, AbilityLifecycle, CombatIntegration, EdgeCase)
```

## Test Suites

| File | Tests | What it covers |
|------|-------|----------------|
| `ServerSimulationTests.cs` | 9 | Entity registration, void death, Q lifecycle, self-hit |
| `SpellResolverTests.cs` | 12 | Sphere/capsule collision, explosion, gravity, CanHitOwner |
| `CombatMathTests.cs` | 21 | Knockback formulas, angle math, DI calculations |
| `PhysicsTests.cs` | 12 | Jump chain, dash, landing, walk/sprint/friction, hitstun, data-driven attack expiry |
| `AbilityLifecycleTests.cs` | 5 | ServerAbility activation, data-driven duration, basic lifecycle |
| `CombatIntegrationTests.cs` | 2 | Two-entity tick stability |
| `EdgeCaseTests.cs` | 2 | Cooldown countdown, entity isolation |

> Abilities aren't fully implemented — ServerAbility tests verify basic activation
> (state transitions, AttackSlot wiring). Data-driven attacks (no ServerAbility)
> work fully through `SimulateTick`'s built-in expiry.

## Writing New Tests

**Use `TestHelpers`** (in `tests/Shared.Tests/TestHelpers.cs`) to avoid boilerplate:

```csharp
var arena = TestHelpers.TestArena();           // 200x200 flat heightmap
var sim = TestHelpers.MakeSim(arena);          // fresh ServerSimulation
var state = TestHelpers.PlayerState();          // entity 1, idle, grounded
state.PY = TestHelpers.MankiGroundPY;          // snap to ground (capsule half)
TestHelpers.RegisterPlayer(sim, Def, state);   // shorthand for sim.RegisterEntity(1, def, state)
var t0 = TestHelpers.TickN(sim, Input(activeSlot: 1), 1); // 1 tick with input, rest default
var after = TestHelpers.TickDefault(sim, 5);   // 5 ticks of default input
TestHelpers.AssertNear(9f, after.PZ, 1.0f);   // tolerance-based float equality
```

**Pattern for ability tests:**
```csharp
// Press slot → check state → tick duration → check Idle
TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3), 1);
Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
for (int i = 0; i < 60; i++) TestHelpers.TickDefault(sim, 1);
Assert.Equal(ActionState.Idle, sim.GetState(1).State);
```

**Always assert exact state, not side effects.** Test behavioral invariants:
wrong state = caught on the assertion; wrong side effect = silent regression.

---

# Testing & Verification

> How to verify that your changes work — for agents and contributors.

---

## Quick Verification (after ANY code change)

```bash
# Must pass with 0 errors
dotnet build --nologo

# Lint check
make lint
```

If `dotnet build` fails with errors in `Shared/`, check that you didn't import an engine type — Shared/ is pure C#.

---

## Sandbox Testing (Unity Editor)

The fastest way to test gameplay changes:

1. Open `client/Unity/` in Unity Hub
2. Press **Play**
3. Select a character (Manki or Bunny)

**What to test:**
- Movement: WASD, space (jump/double jump), shift (dash)
- Combat: LMB combo, RMB (hold), Q/E/R/F abilities
- Targeting: Tab cycles target, scroll wheel zooms

---

## Local PvP Testing (2 instances)

Test the real server-authoritative multiplayer:

**Terminal 1 — Server:**
```bash
dotnet run --project Server/SlopArena.Server.csproj
```
Output: `[Match:...] Listening on UDP 9876, waiting for 2 players...`

**Terminal 2 & 3 — Clients:**
Build the Unity client (`File → Build Settings → Build`) and run two instances. Both connect automatically via `NetworkClient`.

**What to verify:**
- Both players appear on each other's screens
- Attacks register damage on the opponent (check server console for hit logs)
- Void death → respawn works
- Match ends after 3 deaths with score display in console

**Server console signals:**
- `Player 1 connected` / `Player 2 connected — countdown started!`
- `GO!` — match started
- `Player 1 eliminated! Player 2 wins! (3-0)` — match ended
- `Player 1 timed out — stopping match.` — disconnect detected

---

## Running the Headless Server

```bash
# Build and run
dotnet run --project Server/SlopArena.Server.csproj

# Default port: 9876, arena: pit, both players: Manki
# Future CLI args: --port 8765 --arena split --class Bunny
```

The server runs at 60Hz with `ServerSimulation` (hit detection + hurtboxes + void death).

---

## Running Tools

```bash
# Inspect a GLB file
python tools/inspect_glb.py assets/characters/manki/manki.glb

# Validate baked skeleton data
python tools/read_skeleton_bin.py data/manki_skeleton.bin
```


---

## Common Failure Modes

| Symptom | Likely Cause | Check |
|---------|-------------|-------|
 | Build fails with engine type errors | Used engine types in Shared/ | Remove engine reference, use `System.MathF` |
| Character invisible in sandbox | Model not loaded | Check `bakedDataPath` in CharacterDefinition |
| Attacks don't connect | Hitbox offset wrong or TriggerTick > DurationTicks | Check `HitboxEvent` values, check console for `[HITBOX]` log |
| Opponent doesn't move in PvP | Server not running or wrong entity ID | Check server console, verify `OpponentEntityId = 2` |
| Rollback spam in console | Prediction threshold too tight or server desync | Check `distSq > 0.25f` threshold in MatchManager |
| Spell VFX invisible | `StatusSpells.AddToScene` commented out | Check `Scripts/Spells/StatusSpells.cs:177` |

---

## Profiling / Debug

```bash
# Count GD.Print() calls (should stay under 15 files)
grep -r "GD.Print" Scripts/ --include="*.cs" -l | wc -l

# Find long methods (>150 lines)
find Scripts -name "*.cs" -exec awk '/public|private|protected/ && /\(/ {s=NR} /^    \}/ {if(NR-s>150) print FILENAME":"s" ("NR-s" lines)"}' {} \;
```
