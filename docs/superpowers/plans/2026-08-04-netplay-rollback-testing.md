# Netplay & Rollback Testing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the netplay/rollback stack a real test net: a tested, lossless server input buffer, a two-sim convergence harness (authoritative server sim ↔ client rollback sim over the real packet codecs), and a property-based fuzz of the rollback path.

**Architecture:** Three independent layers. (1) `TickInputBuffer` — a pure, tick-ordered input queue in `src/Shared` that the game server (`MatchInstance`) uses instead of its raw `List` ops; single-tick inputs (jump/slot presses) can never be dropped by a backlog, and missing ticks never stall the sim (held-last-input). (2) `NetplayHarness` — an in-process "two sims on a wire" fixture: one `ServerSimulation` (authority) and one `RollbackSimulator` (client), connected by the real `CharacterStatePacket.FromState` → `ServerEntityPacket` codecs with configurable RTT delay and packet loss; asserts exact/≈ convergence. (3) FsCheck property test driving the harness with random inputs + random loss, asserting the crash class (KeyNotFoundException, poisoned state dicts, NaN) never fires and the self state re-converges after an idle loss-free tail.

**Tech Stack:** C# / .NET 8 (Shared targets netstandard2.1), xUnit 2.9, FsCheck 3.3.4 (already referenced by `tests/Shared.Tests`). No new test projects, no Unity, no UDP.

## Global Constraints

- `src/Shared/` targets **netstandard2.1** — `TickInputBuffer` must avoid APIs newer than that (plain `List<T>`, tuples, `is uint x` patterns are fine; no `Index`/`Range`, no `System.Linq`).
- **Server sim is the source of truth.** Tests assert the client converges to the server, never the reverse.
- The sim is deterministic (no RNG in gameplay code). Exact float equality is expected when both sides consume identical inputs; `TestHelpers.AssertNear` is for the opponent (hit-dependent) comparisons.
- Follow existing test conventions: `TestHelpers.TestArena()`, `TestHelpers.MankiDef`, `TestHelpers.PlayerState(x, z)`, `TestHelpers.Input(...)`, `TestHelpers.GroundPY(def)`, `TestHelpers.AssertNear`. Seed-replay pattern from `SimulationInvariantTests` (`[Property(MaxTest = 1, ...)]`, `PositiveInt seed`).
- After any Shared change run `dotnet build src/Shared/ --nologo` (auto-copies the DLL to Unity Plugins). After server changes run `dotnet build src/Server/ --nologo`.
- Test commands during development are filtered: `dotnet test tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~<Class>"`. Run the full suite at the end of each task.
- NEVER run project-wide formatters/linters.
- Commit after each task (Conventional Commits — repo convention; squash at merge).

---

### Task 1: `TickInputBuffer` — lossless in-order server input queue

Extract the server's per-player input queue into a tested pure class, then swap `MatchInstance` onto it. This locks in the netplay input-model fix (in-order consumption, held-last for gaps, no newest-only drops) with unit tests — the class of bug that killed jumps/attacks in the first live 2-client test.

**Files:**
- Create: `src/Shared/Rollback/TickInputBuffer.cs`
- Create: `tests/Shared.Tests/TickInputBufferTests.cs`
- Modify: `src/Server/MatchInstance.cs` (usings; `PlayerSlot.Queue`; `ReceiveInputs` push site; `Tick()` consumption block; `PrimeTickCounter`)

**Interfaces:**
- Consumes: `SlopArena.Shared.InputState` (struct), `System.Collections.Generic.List<T>`.
- Produces (used by Task 1's MatchInstance edits and by nothing else):
  - `public sealed class SlopArena.Shared.Rollback.TickInputBuffer`
  - `public int Count { get; }`
  - `public uint? MaxTick { get; }` — highest queued tick, `null` when empty (buffer is always sorted ascending)
  - `public void Push(uint tick, InputState input)` — insert sorted; replace an existing entry with the same tick
  - `public bool TryTake(uint tick, out InputState input)` — remove + return the entry for exactly `tick`; `false` if absent
  - `public void Prune(uint upToTick)` — remove every entry with `tick <= upToTick`
  - `public void Clear()`

- [ ] **Step 1: Write the failing tests**

Create `tests/Shared.Tests/TickInputBufferTests.cs`:

```csharp
using Xunit;

namespace SlopArena.Shared.Tests;

public class TickInputBufferTests
{
    private static InputState In(byte slot) => new InputState { ActiveSlot = slot };

    [Fact]
    public void Push_KeepsEntriesSortedByTick()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(5, In(5));
        buf.Push(3, In(3));
        buf.Push(4, In(4));
        Assert.Equal(3, buf.Count);
        Assert.True(buf.TryTake(3, out var a));
        Assert.True(buf.TryTake(4, out var b));
        Assert.True(buf.TryTake(5, out var c));
        Assert.Equal((byte)3, a.ActiveSlot);
        Assert.Equal((byte)4, b.ActiveSlot);
        Assert.Equal((byte)5, c.ActiveSlot);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void Push_SameTick_ReplacesExisting()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(4, In(1));
        buf.Push(4, In(2));
        Assert.Equal(1, buf.Count);
        Assert.True(buf.TryTake(4, out var input));
        Assert.Equal((byte)2, input.ActiveSlot);
    }

    [Fact]
    public void TryTake_MissingTick_ReturnsFalse_LeavesBufferIntact()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(4, In(1));
        Assert.False(buf.TryTake(5, out _));
        Assert.Equal(1, buf.Count);
    }

    [Fact]
    public void Prune_RemovesEntriesAtOrBelow()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(3, In(3));
        buf.Push(4, In(4));
        buf.Push(5, In(5));
        buf.Prune(4);
        Assert.Equal(1, buf.Count);
        Assert.True(buf.TryTake(5, out _));
    }

    [Fact]
    public void MaxTick_NullWhenEmpty()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        Assert.Null(buf.MaxTick);
        buf.Push(7, In(7));
        Assert.Equal((uint)7, buf.MaxTick);
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        buf.Push(1, In(1));
        buf.Clear();
        Assert.Equal(0, buf.Count);
        Assert.Null(buf.MaxTick);
    }

    [Fact]
    public void Burst_IntermediateTicksSurvive_NoNewestOnlyDrop()
    {
        // Regression for the old FlushQueue "newest only" bug: a 5-tick burst arriving
        // before the next sim tick must keep every intermediate input consumable —
        // a single-tick jump or slot press must never be silently discarded.
        var buf = new SlopArena.Shared.Rollback.TickInputBuffer();
        for (uint t = 1; t <= 5; t++) buf.Push(t, In((byte)t));
        for (uint t = 1; t <= 5; t++)
        {
            Assert.True(buf.TryTake(t, out var input), $"tick {t} was dropped");
            Assert.Equal((byte)t, input.ActiveSlot);
        }
        Assert.Equal(0, buf.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (type missing)**

Run: `dotnet test tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~TickInputBufferTests"`
Expected: FAIL to build — `The type or namespace name 'TickInputBuffer' could not be found`.

- [ ] **Step 3: Implement `TickInputBuffer`**

Create `src/Shared/Rollback/TickInputBuffer.cs`:

```csharp
using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// A tick-ordered buffer of one player's inputs (netplay input model). The server
    /// consumes ONE input per sim tick — the input whose tick equals the current sim
    /// tick, in arrival-irrelevant order. Unlike a newest-only queue, intermediate
    /// ticks are never dropped: a single-tick jump or slot press survives any backlog
    /// or burst. Missing ticks are handled by the caller via held-last-input.
    /// </summary>
    public sealed class TickInputBuffer
    {
        private readonly List<(uint Tick, InputState Input)> _entries = new();

        /// <summary>Number of queued inputs.</summary>
        public int Count => _entries.Count;

        /// <summary>Highest queued tick, or null when empty. Valid because the buffer
        /// is always kept sorted ascending by tick.</summary>
        public uint? MaxTick => _entries.Count > 0 ? _entries[_entries.Count - 1].Tick : null;

        /// <summary>Insert an input, keeping the buffer sorted by tick ascending.
        /// A duplicate tick replaces the existing entry.</summary>
        public void Push(uint tick, InputState input)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Tick == tick)
                {
                    _entries[i] = (tick, input); // replace duplicate
                    return;
                }
                if (_entries[i].Tick > tick)
                {
                    _entries.Insert(i, (tick, input));
                    return;
                }
            }
            _entries.Add((tick, input));
        }

        /// <summary>Remove and return the input for exactly <paramref name="tick"/>.
        /// Returns false (and leaves the buffer untouched) when absent.</summary>
        public bool TryTake(uint tick, out InputState input)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Tick == tick)
                {
                    input = _entries[i].Input;
                    _entries.RemoveAt(i);
                    return true;
                }
                if (_entries[i].Tick > tick)
                    break; // sorted ascending — not present
            }
            input = default;
            return false;
        }

        /// <summary>Drop every entry with tick ≤ <paramref name="upToTick"/> (consumed).</summary>
        public void Prune(uint upToTick)
        {
            int remove = 0;
            while (remove < _entries.Count && _entries[remove].Tick <= upToTick)
                remove++;
            if (remove > 0)
                _entries.RemoveRange(0, remove);
        }

        public void Clear() => _entries.Clear();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~TickInputBufferTests"`
Expected: `Passed! - Failed: 0, Passed: 7` (build of src/Shared succeeds first).

- [ ] **Step 5: Swap `MatchInstance` onto `TickInputBuffer`**

Edit `src/Server/MatchInstance.cs` — five changes:

1. Usings — add `using SlopArena.Shared.Rollback;` after the existing `using SlopArena.Shared;`.

2. `PlayerSlot.Queue` field type:

```csharp
			public List<(uint tick, InputState input)> Queue { get; } = new();
```
becomes:
```csharp
			public TickInputBuffer Queue { get; } = new();
```

3. `ReceiveInputs` push site — replace the manual duplicate-prevention loop:

```csharp
					// Prevent duplicates
					bool exists = false;
					for (int i = 0; i < slot.Queue.Count; i++)
					{
						if (slot.Queue[i].tick == clientTick)
						{ exists = true; break; }
					}
					if (!exists)
						slot.Queue.Add((clientTick, inputState));
```
becomes:
```csharp
					// TickInputBuffer.Push replaces same-tick duplicates.
					slot.Queue.Push(clientTick, inputState);
```

4. `Tick()` consumption block — replace the raw-list scan:

```csharp
				// Drop already-consumed ticks; keep everything newer. Consuming in tick
				// order (not "newest only") means single-tick inputs — jump presses, slot
				// presses — survive any backlog or burst instead of being silently dropped.
				slot.Queue.RemoveAll(q => q.tick <= _serverTick);
				if (slot.Queue.Count == 0) continue;
				anyPending = true;

				// Input for THIS tick, or hold the last-known input (same semantics as the
				// client's prediction replay). A missing tick never stalls the sim.
				InputState input = slot.LastInput;
				for (int i = 0; i < slot.Queue.Count; i++)
				{
					if (slot.Queue[i].tick == targetTick)
					{
						input = slot.Queue[i].input;
						slot.Queue.RemoveAt(i);
						break;
					}
				}
				slot.LastInput = input;
				inputs[slot.EntityId] = input;
```
becomes:
```csharp
				// Drop already-consumed ticks; keep everything newer. Consuming in tick
				// order (not "newest only") means single-tick inputs — jump presses, slot
				// presses — survive any backlog or burst instead of being silently dropped.
				slot.Queue.Prune(_serverTick);
				if (slot.Queue.Count == 0) continue;
				anyPending = true;

				// Input for THIS tick, or hold the last-known input (same semantics as the
				// client's prediction replay). A missing tick never stalls the sim.
				InputState input = slot.LastInput;
				if (slot.Queue.TryTake(targetTick, out var queuedInput))
					input = queuedInput;
				slot.LastInput = input;
				inputs[slot.EntityId] = input;
```

5. `PrimeTickCounter` — replace the last-element access:

```csharp
			uint maxQueued = 0;
			foreach (var slot in _slots)
				if (slot.Queue.Count > 0)
					maxQueued = Math.Max(maxQueued, slot.Queue[slot.Queue.Count - 1].tick);
```
becomes:
```csharp
			uint maxQueued = 0;
			foreach (var slot in _slots)
				if (slot.Queue.MaxTick is uint maxTick)
					maxQueued = Math.Max(maxQueued, maxTick);
```

- [ ] **Step 6: Build the server and run the full test suite**

Run: `dotnet build src/Server/ --nologo`
Expected: `Build succeeded` (catches any leftover raw-list usages).

Run: `dotnet test tests/Shared.Tests/ --nologo`
Expected: `Passed! - Failed: 0` (full suite, ~490 tests including the 7 new ones).

- [ ] **Step 7: Commit**

```bash
git add src/Shared/Rollback/TickInputBuffer.cs tests/Shared.Tests/TickInputBufferTests.cs src/Server/MatchInstance.cs
git commit -m "feat(netplay): add TickInputBuffer — lossless in-order server input queue"
```

---

### Task 2: Two-sim convergence harness + convergence tests

An in-process "two sims on a wire" fixture: authoritative `ServerSimulation` + client `RollbackSimulator`, connected through the REAL packet codecs (`CharacterStatePacket.FromState` → `ServerEntityPacket`) with configurable RTT delay and packet loss. Asserts the client converges to the server — exact for the self entity, ≈ for the opponent (its predicted sim has no self hurtbox, so cross-hits legitimately diverge damage).

**Files:**
- Create: `tests/Shared.Tests/NetplayHarness.cs`
- Create: `tests/Shared.Tests/RollbackConvergenceTests.cs`

**Interfaces:**
- Consumes: `ServerSimulation`, `RollbackSimulator` (`SlopArena.Shared.Rollback`), `ServerEntityPacket`, `CharacterStatePacket.FromState`, `TestHelpers` (TestArena, MankiDef, PlayerState, Input, GroundPY, AssertNear). All public — no production changes in this task.
- Produces (used by Task 3):
  - `internal sealed class NetplayHarness` in `SlopArena.Shared.Tests`
  - `public const ulong SelfId = 1;` `public const ulong OpponentId = 2;`
  - `public NetplayHarness(ArenaDefinition arena, CharacterDefinition def, int delayTicks = 0, int dropEvery = 0)` — `delayTicks` = RTT in ticks (packets applied that many ticks after generation); `dropEvery` = drop every Nth packet (0 = no loss)
  - `public void Step(InputState in1, InputState in2)` — one tick: server ticks (1→in1, 2→in2), client predicts self with in1, then delayed packets are applied (`ReconcileSelf` + `IngestOpponentBatch`)
  - `public CharacterState ServerState(ulong id)`, `public CharacterState ClientState(ulong id)`
  - `public static void AssertSelfConverged(NetplayHarness h)` — exact wire equality of entity 1 (client local sim vs server)
  - `public static void AssertOpponentConverged(NetplayHarness h, float tolerance = 0.001f)` — ≈ equality of entity 2 (positions/velocities/state/grounded/facing)

- [ ] **Step 1: Write the harness**

Create `tests/Shared.Tests/NetplayHarness.cs`:

```csharp
using System;
using System.Collections.Generic;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// In-process netplay simulation: an authoritative ServerSimulation ("the server")
/// and a RollbackSimulator ("the client") wired through the real packet codecs
/// (CharacterStatePacket.FromState → ServerEntityPacket) with configurable packet
/// delay (RTT) and loss. No UDP, no threads — deterministic.
///
/// Per tick, in bridge order:
///   1. server ticks with (in1, in2)                    → authoritative truth
///   2. client predicts self with in1 (RollbackSimulator.Tick)
///   3. delayed server packets are applied              → ReconcileSelf + IngestOpponentBatch
///
/// Both sides consume identical inputs, so with no loss the client converges to the
/// server exactly (the sim is deterministic). The opponent's PredictedTrack has no
/// self hurtbox, so cross-hits make damage/knockback legitimately diverge — hence
/// AssertOpponentConverged uses tolerance.
/// </summary>
internal sealed class NetplayHarness
{
    public const ulong SelfId = 1;
    public const ulong OpponentId = 2;

    private readonly ServerSimulation _server;
    private readonly RollbackSimulator _client;
    private readonly int _delayTicks;
    private readonly int _dropEvery; // 0 = no loss
    private readonly Queue<(uint Tick, ServerEntityPacket Self, ServerEntityPacket Opp)> _inFlight = new();
    private uint _serverTick;

    public NetplayHarness(ArenaDefinition arena, CharacterDefinition def, int delayTicks = 0, int dropEvery = 0)
    {
        _delayTicks = delayTicks;
        _dropEvery = dropEvery;

        // Both entities spawn grounded on the arena floor; the same initial states
        // are registered on both sides so the trace starts converged.
        var p1 = TestHelpers.PlayerState();
        p1.PY = TestHelpers.GroundPY(def);
        var p2 = TestHelpers.PlayerState(x: 10f);
        p2.PY = TestHelpers.GroundPY(def);

        _server = new ServerSimulation(arena);
        _server.RegisterEntity(SelfId, def, p1);
        _server.RegisterEntity(OpponentId, def, p2);

        _client = new RollbackSimulator(arena, SelfId);
        _client.RegisterEntity(SelfId, def, p1);
        _client.RegisterEntity(OpponentId, def, p2);
    }

    /// <summary>One tick. in1/in2 are fed to the server for entities 1/2; the client
    /// predicts self with in1.</summary>
    public void Step(InputState in1, InputState in2)
    {
        _serverTick++;
        _server.Tick(new Dictionary<ulong, InputState> { { SelfId, in1 }, { OpponentId, in2 } });

        _client.Tick(new Dictionary<ulong, InputState> { { SelfId, in1 } });

        var selfPacket = new ServerEntityPacket
        {
            EntityId = SelfId, Tick = _serverTick,
            State = CharacterStatePacket.FromState(_server.GetState(SelfId), _serverTick),
            HasInput = true, Input = in1,
        };
        var oppPacket = new ServerEntityPacket
        {
            EntityId = OpponentId, Tick = _serverTick,
            State = CharacterStatePacket.FromState(_server.GetState(OpponentId), _serverTick),
            HasInput = true, Input = in2,
        };
        _inFlight.Enqueue((_serverTick, selfPacket, oppPacket));

        if (_inFlight.Count > _delayTicks)
        {
            var (_, self, opp) = _inFlight.Dequeue();
            if (_dropEvery == 0 || _serverTick % _dropEvery != 0)
            {
                _client.ReconcileSelf(self);
                _client.IngestOpponentBatch(new[] { opp });
            }
        }
    }

    public CharacterState ServerState(ulong id) => _server.GetState(id);
    public CharacterState ClientState(ulong id) => _client.GetState(id);

    /// <summary>Entity 1 (self) must equal the server on every wire field. Exact: both
    /// sides run the same deterministic sim with identical inputs. Note: MatchState is
    /// never set by the sim on either side (both default), so it stays comparable —
    /// if a future change sets it, normalize it here before comparing.</summary>
    public static void AssertSelfConverged(NetplayHarness h)
    {
        var expected = CharacterStatePacket.FromState(h.ServerState(SelfId));
        var actual = CharacterStatePacket.FromState(h.ClientState(SelfId));
        Assert.Equal(expected, actual);
    }

    /// <summary>Entity 2 (opponent) must track the server within tolerance. Damage and
    /// knockback may legitimately diverge (PredictedTrack has no self hurtbox), so only
    /// trajectory fields are compared.</summary>
    public static void AssertOpponentConverged(NetplayHarness h, float tolerance = 0.001f)
    {
        var s = h.ServerState(OpponentId);
        var c = h.ClientState(OpponentId);
        TestHelpers.AssertNear(s.PX, c.PX, tolerance);
        TestHelpers.AssertNear(s.PY, c.PY, tolerance);
        TestHelpers.AssertNear(s.PZ, c.PZ, tolerance);
        TestHelpers.AssertNear(s.VX, c.VX, tolerance);
        TestHelpers.AssertNear(s.VY, c.VY, tolerance);
        TestHelpers.AssertNear(s.VZ, c.VZ, tolerance);
        Assert.Equal(s.State, c.State);
        Assert.Equal(s.IsGrounded, c.IsGrounded);
        TestHelpers.AssertNear(s.FacingYaw, c.FacingYaw, tolerance);
    }
}
```

- [ ] **Step 2: Write the convergence tests**

Create `tests/Shared.Tests/RollbackConvergenceTests.cs`:

```csharp
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Two-sim netplay convergence: authoritative ServerSimulation vs client
/// RollbackSimulator over the real packet codecs, with RTT delay and packet loss.
/// The scripted traces avoid cross-hits (attacks happen while the entities are far
/// apart) so the opponent converges exactly too.
/// </summary>
public class RollbackConvergenceTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

    private static NetplayHarness Harness(int delayTicks = 0, int dropEvery = 0)
        => new NetplayHarness(TestHelpers.TestArena(), Def, delayTicks, dropEvery);

    [Fact]
    public void MovementTrace_ConvergesExact_NoDelay()
    {
        var h = Harness();
        for (int t = 0; t < 120; t++)
            h.Step(TestHelpers.Input(moveX: 1f), TestHelpers.Input(moveX: -1f));
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }

    [Fact]
    public void JumpDashAttackTrace_ConvergesExact_WithRttDelay()
    {
        // 2-tick RTT; attacks happen at tick 5-12 while the entities are ~10m apart
        // (Manki LMB range is far shorter), so no cross-hit lands and both sides
        // stay exactly converged — including the opponent's Complex→Predictable
        // re-registration after its attack ends.
        var h = Harness(delayTicks: 2);
        for (int t = 0; t < 240; t++)
        {
            InputState in1 = TestHelpers.Input(moveX: 1f,
                jump: t == 20 || t == 100, dash: t == 40);
            InputState in2 = TestHelpers.Input(moveX: -1f,
                jump: t == 30 || t == 110, dash: t == 50,
                activeSlot: t is >= 5 and < 12 ? (byte)1 : (byte)0);
            h.Step(in1, in2);
        }
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }

    [Fact]
    public void PacketLoss_ReconvergesAfterLastReceivedPacket()
    {
        // Drop every 5th packet (ticks 5, 10, …). Missed opponent packets make the
        // prediction diverge until the next packet corrects it; after the trace,
        // an idle flush drains the RTT window so the final reconcile + replay
        // re-converges exactly.
        var h = Harness(delayTicks: 2, dropEvery: 5);
        for (int t = 0; t < 240; t++)
        {
            InputState in1 = TestHelpers.Input(moveX: 1f, jump: t == 20, dash: t == 40);
            InputState in2 = TestHelpers.Input(moveX: -1f, jump: t == 30, dash: t == 50);
            h.Step(in1, in2);
        }
        for (int t = 0; t < 8; t++) h.Step(default, default); // flush RTT window
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }

    [Fact]
    public void OpponentAttack_RawTrackThenReRegistration_ConvergesAfterComplexEnds()
    {
        // Entity 2 attacks early while ~9.5m from entity 1 (no cross-hit): the client
        // must route the Complex state to RawTrack, then re-register + rebuild the
        // predicted track when the attack ends, and land back on exact convergence.
        var h = Harness();
        for (int t = 0; t < 120; t++)
        {
            InputState in1 = TestHelpers.Input();
            InputState in2 = TestHelpers.Input(moveX: -0.5f,
                activeSlot: t is >= 5 and < 12 ? (byte)1 : (byte)0);
            h.Step(in1, in2);
        }
        NetplayHarness.AssertSelfConverged(h);
        NetplayHarness.AssertOpponentConverged(h);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~RollbackConvergenceTests"`
Expected: `Passed! - Failed: 0, Passed: 4`.

These tests lock in EXISTING behavior — they should pass on the current code. **If one fails, it is a genuine finding, not a test bug.** Before touching the test, check: did a scripted attack land a cross-hit (entities within ~3m at the attack tick)? Then the opponent's damage legitimately diverges — adjust the trace (attack earlier/farther) and add a comment. Anything else (self state not exactly equal, KeyNotFoundException, non-finite values) is a real rollback bug to investigate.

- [ ] **Step 4: Commit**

```bash
git add tests/Shared.Tests/NetplayHarness.cs tests/Shared.Tests/RollbackConvergenceTests.cs
git commit -m "test(netplay): add two-sim convergence harness over real codecs"
```

---

### Task 3: Rollback fuzz — no crash + self re-convergence

Property test driving `NetplayHarness` with random inputs and random loss/delay. Mirrors the established `SimulationInvariantTests` pattern (seeded `PositiveInt`, fixed trace length). Asserts the crash class that bit live netplay — KeyNotFoundException on `_defs`/`_states`, poisoned dicts, NaN drift — plus exact self re-convergence after an idle loss-free tail.

**Files:**
- Create: `tests/Shared.Tests/RollbackInvariantTests.cs`

**Interfaces:**
- Consumes: `NetplayHarness` (Task 2), `TestHelpers`, FsCheck (`FsCheck`, `FsCheck.Xunit`).

- [ ] **Step 1: Write the property test**

Create `tests/Shared.Tests/RollbackInvariantTests.cs`:

```csharp
using System;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Property-based fuzz of the netplay/rollback client path: random inputs + random
/// packet loss/delay through NetplayHarness. Asserts the crash class (KeyNotFound on
/// defs/states, poisoned dictionaries, NaN drift) never fires, plus exact self-state
/// re-convergence after an idle loss-free tail.
///
/// Entity 1's ActiveSlot is forced to 0: the self entity's own attacks can diverge
/// between the local sim (mirror opponent) and the server (real opponent) on combo
/// chain, which would legitimately break exact equality. Entity 2's inputs are fully
/// random — they exercise the opponent prediction path (Complex routing, RawTrack,
/// re-registration, lunge, hits on entity 1 server-side).
///
/// On failure FsCheck prints a seed — pass it to the PositiveInt parameter to replay:
///   "Falsifiable, with seed: 12345"
/// </summary>
public class RollbackInvariantTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

    [Property(MaxTest = 1, EndSize = 300)]
    public void Rollback_DeepFuzz_NoCrash_Converges(PositiveInt seed)
    {
        var rng = new Random(seed.Item);
        var arena = TestHelpers.TestArena();

        int delayTicks = rng.Next(0, 4);
        int dropEvery = rng.Next(3) == 0 ? 0 : rng.Next(4, 9); // ~1/3 of runs lossless
        int traceTicks = 300;
        int tailTicks = delayTicks + 30; // idle + loss-free tail ⇒ re-convergence

        var h = new NetplayHarness(arena, Def, delayTicks, dropEvery);

        for (int tick = 0; tick < traceTicks; tick++)
        {
            var in1 = RandomInput(rng);
            in1.ActiveSlot = 0; // self never attacks (see class doc)
            var in2 = RandomInput(rng);
            h.Step(in1, in2);

            AssertFinite(h, tick);
        }

        // Idle, loss-free tail: every tick Predictable and every packet received, so
        // the final reconcile + replay must re-converge the self state exactly.
        for (int tick = 0; tick < tailTicks; tick++)
        {
            h.Step(default, default);
            AssertFinite(h, traceTicks + tick);
        }

        NetplayHarness.AssertSelfConverged(h);
    }

    private static void AssertFinite(NetplayHarness h, int tick)
    {
        foreach (var id in new[] { NetplayHarness.SelfId, NetplayHarness.OpponentId })
        {
            var s = h.ClientState(id);
            Assert.True(Enum.IsDefined(typeof(ActionState), s.State),
                $"Tick {tick}, entity {id}: invalid ActionState {s.State}");
            Assert.InRange(s.DamagePercent, 0, 999);
            Assert.True(
                float.IsFinite(s.PX) && float.IsFinite(s.PY) && float.IsFinite(s.PZ) &&
                float.IsFinite(s.VX) && float.IsFinite(s.VY) && float.IsFinite(s.VZ),
                $"Tick {tick}, entity {id}: non-finite position/velocity ({s.PX}, {s.PY}, {s.PZ}) / ({s.VX}, {s.VY}, {s.VZ})");
            Assert.True(s.HitstunTicks <= 60,
                $"Tick {tick}, entity {id}: HitstunTicks={s.HitstunTicks} > 60 (stuck)");
        }
    }

    /// <summary>Random valid InputState (mirrors SimulationInvariantTests.RandomInput).</summary>
    private static InputState RandomInput(Random rng)
    {
        var input = new InputState
        {
            MoveX = (float)(rng.NextDouble() * 2.0 - 1.0),
            MoveY = (float)(rng.NextDouble() * 2.0 - 1.0),
            Up = rng.Next(4) == 0,
            Down = rng.Next(4) == 0,
            Left = rng.Next(4) == 0,
            Right = rng.Next(4) == 0,
            Jump = rng.Next(8) == 0,
            Dash = rng.Next(8) == 0,
            Crouch = rng.Next(10) == 0,
            ActiveSlot = rng.Next(7) == 0 ? (byte)rng.Next(1, 7) : (byte)0,
            IsAiming = rng.Next(10) == 0,
        };
        input.FacingYaw = (short)rng.Next(-18000, 18001);
        input.AimYaw = (short)rng.Next(-18000, 18001);
        input.AimDistance = (ushort)rng.Next(0, 6501);
        input.AimPitch = (short)rng.Next(-9000, 9001);
        // Target entity 1, 2, or none — exercises mirror target-lock (guarded lookup).
        input.TargetEntityId = (byte)rng.Next(0, 3);
        return input;
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/Shared.Tests/ --nologo --filter "FullyQualifiedName~RollbackInvariantTests"`
Expected: `Passed! - Failed: 0, Passed: 1`.

If it falsifies, the printed seed replays the exact failing trace; investigate the divergence before adjusting anything. A failing trace that ends non-converged usually means a Complex state (hitstun from a cross-hit) survived into the idle tail — the tail is `delayTicks + 30`; hitstun is ≤ 60 ticks. If that happens, verify the mechanism first, then extend `tailTicks` (documented) — do not loosen `AssertSelfConverged`.

- [ ] **Step 3: Run the full suite and commit**

Run: `dotnet test tests/Shared.Tests/ --nologo`
Expected: `Passed! - Failed: 0` (full suite, ~495 tests).

```bash
git add tests/Shared.Tests/RollbackInvariantTests.cs
git commit -m "test(netplay): add rollback fuzz — no crash + self re-convergence"
```

---

## Out of scope (noted, not planned)

- **UDP loopback test for `MatchInstance`** (real sockets, fake 60Hz client): would test the thread/UDP glue; `TickInputBuffer` + convergence tests cover the logic. Needs a `tests/Server.Tests` project — a future plan if the glue proves flaky.
- **Unity PlayMode wiring test for `PvPMatch` entity registration**: MonoBehaviour scene wiring is heavy to test; the registration behavior is locked by `RollbackSimulatorTests.IngestOpponentBatch_UnregisteredOpponent_FallsBackToRawTrack_NoThrow`. A seam (extracting PvPMatch's registration into a plain class) is the precondition — flagged, not built.

## Self-Review

- **Spec coverage:** Every item from the agreed ladder is a task: input-buffer extraction + tests (Task 1), two-sim convergence + loss/delay (Task 2), fuzz (Task 3). Optional items (UDP loopback, PlayMode) are explicitly scoped out with rationale.
- **Placeholder scan:** All steps contain concrete code; commands have expected output; no "TBD"/"add error handling" placeholders.
- **Type consistency:** `TickInputBuffer` API used in Task 1's MatchInstance edits matches its implementation exactly (`Push`, `TryTake`, `Prune`, `MaxTick`, `Count`, `Clear`). `NetplayHarness` members referenced by Task 3 (`SelfId`, `OpponentId`, `Step`, `ClientState`, `AssertSelfConverged`, `AssertFinite` helper) match Task 2's definitions. `TestHelpers.Input` named args (`moveX`, `jump`, `dash`, `activeSlot`) match the real signature.
