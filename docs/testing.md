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

# Run all tests: 390 across 33 test suites

## Test Suites

 | File | Tests | What it covers |
 |------|-------|----------------|
 | `AbilityLifecycleTests.cs` | 31 | Per-ability lifecycle — all abilities use ServerAbility subclasses |
 | `AirDashAnalysisTests.cs` | 2 | Aerial dash hover bug: FloatWindow restart during air dash |
 | `AttackIdleReTriggerTests.cs` | 5 | Attack → idle bugs: held-input re-trigger, halved AnimLockTicks |
 | `AttackToIdleTests.cs` | 15 | Attacking → Idle transitions for every Manki/FightGuy ability |
 | `AttackToIdleVelocityTests.cs` | 10 | Velocity zeroed on Attacking → Idle (ServerAbility.EndAbility) |
 | `BakedAnimationDataTests.cs` | 8 | .bin format loader (matches SlopArenaBaker output) |
 | `CharacterStatePacketTests.cs` | 3 | Packet round-trip serialization, size, AnimIndex |
 | `ClipExtrapolationTests.cs` | 9 | Extrapolation modes (None/Hold/Continuous), velocity projection |
 | `CombatIntegrationTests.cs` | 2 | Two-entity tick stability |
 | `CombatMathTests.cs` | 21 | Circle/cone intersection, knockback direction, projectile launch |
 | `CombatPipelineTests.cs` | 14 | **Full-pipeline combat** — LMB/Q hit NPCs, warp states, knockback profiles |
 | `DashTests.cs` | 12 | Dash transitions, cooldown, cancel, velocity dead-zone |
 | `EdgeCaseTests.cs` | 2 | Input buffering, cooldown countdown, entity isolation |
 | `FacingDirectionTests.cs` | 2 | Facing yaw stability through hitstun |
 | `FightGuyAbilityTests.cs` | 38 | FightGuy LMB/Q/E/R/F activation, hitbox collision, mark system, homing, launcher |
 | `FightGuyKitRegressionTests.cs` | 5 | Golden kit regression for FightGuy |
 | `HitstunAnimationTierTests.cs` | 7 | 3-tier hitstun animation (damage → HitstunLevel, clip tiers) |
 | `HostedServerConfigTests.cs` | 3 | HostedServerConfig server.json builder (ADR-0005) |
 | `KistuAbilityTests.cs` | 13 | Kistu kit: RMB charge, E dash, R launcher + charge-stock, Q counter |
 | `LedgeSnapTests.cs` | 6 | Ledge snap auto-grab near stage edge |
 | `LobbyPayloadCodecTests.cs` | 16 | SignalR lobby JSON payload mapping (issue #33) |
 | `MankiKitRegressionTests.cs` | 7 | Golden kit regression for Manki (LMB, RMB, AirRMB, Overclock, Q) |
 | `MankiKitTests.cs` | 6 | Bazooka rocket-jump, grapple tether, AirLMB, AirRMB |
 | `MankiLmbTests.cs` | 16 | Manki LMB combo: 3-hit chain, lunge, bone hitboxes, input buffering |
 | `MasterServerClientTests.cs` | 13 | MasterServerClient with canned HTTP responses |
 | `MatchStartRequestCodecTests.cs` | 9 | Match-start request: char classes + dynamic entity IDs (ADR-0008) |
 | `NilusAbilityTests.cs` | 36 | Nilus kit: registration + Q/E/R/F data-driven slots |
 | `NilusKitRegressionTests.cs` | 9 | Golden kit regression for Nilus (full-state EntitySnapshot diff) |
 | `PhysicsTests.cs` | 20 | State machine transitions and movement physics |
 | `RehitZoneTests.cs` | 9 | Hitbox.RehitIntervalTicks lingering zones |
 | `ServerLogParserTests.cs` | 5 | ServerLogParser master-server registration detection (ADR-0005) |
 | `ServerSimulationTests.cs` | 24 | Sim core: respawn, cooldowns, soft-lock targeting, rotation |
 | `SpellResolverTests.cs` | 12 | Sphere/capsule collision, owner skip, ground explosions, gravity |
 
 > **Ground-truth tests** (`CombatPipelineTests`) are the most important for understanding how combat
 > works end to end. They exercise the full pipeline: Input → ServerAbility → Hitbox → Collision →
 > Damage/Knockback/Hitstun. Agents and contributors should read these first.
Abilities have per-slot ServerAbility coverage. Data-driven abilities (no ServerAbility) work
fully through `SimulateTick`'s built-in expiry.


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
```

If `dotnet build` fails with errors in `Shared/`, check that you didn't import an engine type — Shared/ is pure C#.

---

## Sandbox Testing (Unity Editor)

The fastest way to test gameplay changes:

1. Open `client/Unity/` in Unity Hub
2. Press **Play**
3. Select a character (Manki or FightGuy)

**What to test:**
- Movement: WASD, space (jump/double jump), shift (dash)
- Combat: LMB combo, RMB (hold), Q/E/R/F abilities
- Targeting: Tab cycles target, scroll wheel zooms

---

## Local PvP Testing (2 instances)

Test the real server-authoritative multiplayer:

**Terminal 1 — Server:**
```bash
dotnet run --project src/Server/
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
dotnet run --project src/Server/

# Default port: 9876, arena: pit, both players: Manki
# Future CLI args: --port 8765 --arena split --class FightGuy
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
| Spell VFX invisible | ProjectileVFXManager not registered | Check `TrainingMatch` wires `ProjectileVFXManager` — see `docs/systems/spell-vfx.md` |

