# Rollback Netcode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` (recommended) or `executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **This plan's own recommendation (see design discussion in the preceding session): use `executing-plans` (single continuous ownership), not `subagent-driven-development`.** Tasks 1–5 form one tightly-coupled state machine (`RollbackSimulator`) where a fresh-subagent-per-task risks integration-seam bugs across LocalTrack/PredictedTrack. Tasks 6–11 (Unity-facing) also depend on a stable Editor session for verification, which favors one continuous worker over parallel dispatch. If you still want subagent-per-task, at minimum keep Tasks 1–5 with one agent.

**Goal:** Implement the three-track rollback design from `docs/plans/2026-08-02-rollback-netcode.md` / `docs/adr/0011-rollback-scope-track-model.md`: a continuously-running local sim for the player (LocalTrack), rebuild-and-replay prediction for opponents in movement states (PredictedTrack), and raw server display for opponents mid-attack/hitstun/warp (RawTrack).

**Architecture:** `src/Shared/Rollback/` houses three pure-C# classes (`ActionStateClassifier`, `LocalTrack`, `PredictedTrack`) composed by `RollbackSimulator`, which exposes the same shape as `ISimulationBridge` plus two extra ingestion methods. `RollbackSimulationBridge` (Unity) is the thin adapter wiring it to `NetworkClient`. `PvPMatch` swaps to it, replacing `NetworkSimulationBridge` (deleted).

**One design gap closed during this planning pass, not decided in the prior grilling session — flagging it explicitly:** `ServerSimulation.Tick()`'s target-lock logic (`ProcessTargetLock`) does `var target = _states[targetId]` with a **direct dictionary indexer**, unconditionally, for *any* entity whose input carries a nonzero `TargetEntityId` — which `InputController.BuildInputState` sets from screen-center proximity on **every frame an opponent is near screen-center, attacking or not**. If `LocalTrack` ran a self-only `ServerSimulation` (no opponents registered), this throws `KeyNotFoundException` the moment an opponent is on screen — not an edge case, the common case. Task 3 below fixes this: `LocalTrack` mirrors read-only snapshots of every other entity's current best-known state into its private sim before each `Tick()`, purely so `_states[targetId]` lookups resolve. This doesn't change any decided track semantics (mirrored entities are never rendered from LocalTrack, never advance meaningfully — they're overwritten fresh every frame), it just makes LocalTrack's "self, always continuous" claim not crash.

**Tech Stack:** C# / .NET 8 (`src/Shared`, netstandard2.1), xUnit (`tests/Shared.Tests`), Unity 6000.0.78f1 / C# (`client/Unity`).

## Global Constraints

- Shared code (`src/Shared/`) targets netstandard2.1, zero `UnityEngine` imports, `MathF` only, no RNG (audited clean — keep it that way).
- `dotnet build src/Shared/ --nologo` after every Shared change (auto-copies the DLL to Unity Plugins) — run this instead of trusting an editor.
- `dotnet test tests/Shared.Tests/` — run the filtered class after each task; full suite at the end of Task 5 and again at the end of Task 11.
- Wire format is a hard contract: `CharacterStatePacket.Size` and `ServerEntityPacket.{BaseSize,RelaySize,MaxSize,NoInputSize}` are asserted by existing tests (`CharacterStatePacketTests.Size_MatchesActualSerializedLayout`, `ServerEntityPacketTests.SizeConstants_AssertWireLayout`) — Task 1 updates both, on purpose, to the new D10-widened sizes.
- Conventional Commits, one commit per step-group as shown; final squash happens at `sloparena-finish-branch` time, not during this plan.
- Follow `.omp/AGENTS.md` Debugging Protocol: this plan already has the "vas y" for the design (grilling session concluded) — no further per-file pause is required mid-plan, but stop and ask before any step that isn't literally described here.

---

### Task 1: Widen `CharacterStatePacket` wire format (D10)

**Files:**
- Modify: `src/Shared/CharacterStatePacket.cs`
- Modify: `tests/Shared.Tests/CharacterStatePacketTests.cs`
- Modify: `tests/Shared.Tests/ServerEntityPacketTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `CharacterStatePacket` gains 14 fields — `AirTimeTicks (ushort)`, `DashDurationTicks (ushort)`, `DashDirX (float)`, `DashDirZ (float)`, `DashCooldownTicks (ushort)`, `AirDodgesLeft (byte)`, `JumpsLeft (byte)`, `InvincibilityTicks (ushort)`, `TurnaroundTicks (ushort)`, `DirHoldTicks (ushort)`, `IsSprinting (bool)`, `LastDirX (float)`, `LastDirZ (float)`, `WasAirborneDuringKnockback (bool)`. `Size` becomes `95`. New method `void ApplyTo(ref CharacterState s)` — overwrites only the wire-carried fields of an existing `CharacterState` in place, leaving every other field untouched (unlike `ToState()`, which builds a fresh `CharacterState` and zeroes everything not on the wire). Task 3 (`LocalTrack`) is the consumer of `ApplyTo`.

- [ ] **Step 1: Write the failing tests**

In `tests/Shared.Tests/CharacterStatePacketTests.cs`, extend the existing `original` in `RoundTrip_PreservesAllFields` and its asserts, update the size test, and add the new `ApplyTo` test:

```csharp
// In RoundTrip_PreservesAllFields, add to the `original` initializer (after Cooldown5 = 66,):
            AirTimeTicks = 37,
            DashDurationTicks = 9,
            DashDirX = 0.6f,
            DashDirZ = -0.8f,
            DashCooldownTicks = 20,
            AirDodgesLeft = 1,
            JumpsLeft = 2,
            InvincibilityTicks = 15,
            TurnaroundTicks = 4,
            DirHoldTicks = 11,
            IsSprinting = true,
            LastDirX = 1f,
            LastDirZ = 0f,
            WasAirborneDuringKnockback = true,

// After the existing Cooldown5 assert, add:
        Assert.Equal(original.AirTimeTicks, restored.AirTimeTicks);
        Assert.Equal(original.DashDurationTicks, restored.DashDurationTicks);
        Assert.Equal(original.DashDirX, restored.DashDirX);
        Assert.Equal(original.DashDirZ, restored.DashDirZ);
        Assert.Equal(original.DashCooldownTicks, restored.DashCooldownTicks);
        Assert.Equal(original.AirDodgesLeft, restored.AirDodgesLeft);
        Assert.Equal(original.JumpsLeft, restored.JumpsLeft);
        Assert.Equal(original.InvincibilityTicks, restored.InvincibilityTicks);
        Assert.Equal(original.TurnaroundTicks, restored.TurnaroundTicks);
        Assert.Equal(original.DirHoldTicks, restored.DirHoldTicks);
        Assert.Equal(original.IsSprinting, restored.IsSprinting);
        Assert.Equal(original.LastDirX, restored.LastDirX);
        Assert.Equal(original.LastDirZ, restored.LastDirZ);
        Assert.Equal(original.WasAirborneDuringKnockback, restored.WasAirborneDuringKnockback);

// Replace the Size_MatchesActualSerializedLayout body:
    [Fact]
    public void Size_MatchesActualSerializedLayout()
    {
        // 63 bytes base (locked pre-rollback) + 32 bytes of D10 movement-resource
        // fields (AirTimeTicks..WasAirborneDuringKnockback) = 95. Lock the constant:
        // a silent Size change would break every packet on the wire.
        Assert.Equal(95, CharacterStatePacket.Size);

        var packet = CharacterStatePacket.FromState(new CharacterState { AimPitch = 1f, LastDirX = 2f });
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer); // throws if Size is too small
        var restored = CharacterStatePacket.Deserialize(buffer);
        Assert.Equal(1f, restored.AimPitch);
        Assert.Equal(2f, restored.LastDirX);
    }

    [Fact]
    public void ApplyTo_OverwritesOnlyWireFields_PreservesRest()
    {
        // Arrange: a CharacterState with a non-wire field set (AttackElapsedTicks is
        // never on the wire — this proves ApplyTo doesn't zero it, unlike ToState()).
        var target = new CharacterState
        {
            PX = 1f,
            AttackElapsedTicks = 500, // NOT carried by CharacterStatePacket — must survive
            AirTimeTicks = 999,       // IS carried — must be overwritten
        };
        var packet = CharacterStatePacket.FromState(new CharacterState { PX = 42f, AirTimeTicks = 7 });

        // Act
        packet.ApplyTo(ref target);

        // Assert
        Assert.Equal(42f, target.PX);       // wire field overwritten
        Assert.Equal((ushort)7, target.AirTimeTicks); // wire field overwritten
        Assert.Equal((ushort)500, target.AttackElapsedTicks); // non-wire field preserved
    }
```

In `tests/Shared.Tests/ServerEntityPacketTests.cs`, update `SizeConstants_AssertWireLayout`:

```csharp
    [Fact]
    public void SizeConstants_AssertWireLayout()
    {
        // Downlink max packet size is a wire contract (issue #80, widened per ADR-0011/D10):
        // 107B base (8 entityId + 4 tick + 95 CharacterStatePacket) + 1B flag + 19B input.
        Assert.Equal(8 + 4 + CharacterStatePacket.Size, ServerEntityPacket.BaseSize);
        Assert.Equal(107, ServerEntityPacket.BaseSize);
        Assert.Equal(1 + InputState.Size, ServerEntityPacket.RelaySize);
        Assert.Equal(20, ServerEntityPacket.RelaySize);
        Assert.Equal(127, ServerEntityPacket.MaxSize);
        Assert.Equal(108, ServerEntityPacket.NoInputSize);
        // Uplink format untouched: 19B InputState (31B full uplink packet with entityId+tick)
        Assert.Equal(19, InputState.Size);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~CharacterStatePacketTests|FullyQualifiedName~ServerEntityPacketTests" --nologo`
Expected: FAIL — `CharacterStatePacket` has no `AirTimeTicks`/etc. members yet (compile error), and the two size asserts fail against the old 63/75/95/76 values.

- [ ] **Step 3: Implement the widened packet**

In `src/Shared/CharacterStatePacket.cs`, add the 14 fields after `Cooldown0..5`, widen `Size`, extend `FromState`/`ToState`/`Serialize`/`Deserialize`, and add `ApplyTo`:

```csharp
        // ── D10: movement-resource fields (ADR-0011) — needed for PredictedTrack's
        // rebuild-and-replay of Predictable ActionStates (Idle/Dashing/JumpSquat/AirDodging)
        // to be byte-identical. None of these touch the ability-instance or hitbox layer.
        public ushort AirTimeTicks;
        public ushort DashDurationTicks;
        public float DashDirX, DashDirZ;
        public ushort DashCooldownTicks;
        public byte AirDodgesLeft;
        public byte JumpsLeft;
        public ushort InvincibilityTicks;
        public ushort TurnaroundTicks;
        public ushort DirHoldTicks;
        public bool IsSprinting;
        public float LastDirX, LastDirZ;
        public bool WasAirborneDuringKnockback;

        /// <summary>95 bytes — 63 base + 32 D10 movement-resource fields.</summary>
        public const int Size = 4 + 4 + 4 + 4 + 4 + 4 + 4 + 1 + 1 + 2 + 1 + 1 + 1 + 4 + 1 + 2 + 1 + 1 + 4 + 1 + 2 + 2 + 2 + 2 + 2 + 2 + 2
            + 2 + 2 + 4 + 4 + 2 + 1 + 1 + 2 + 2 + 2 + 1 + 4 + 4 + 1;
```

Add to `FromState` (inside the `new CharacterStatePacket { ... }` initializer, after `Cooldown5 = s.Cooldown5,`):

```csharp
                AirTimeTicks = s.AirTimeTicks,
                DashDurationTicks = s.DashDurationTicks,
                DashDirX = s.DashDirX,
                DashDirZ = s.DashDirZ,
                DashCooldownTicks = s.DashCooldownTicks,
                AirDodgesLeft = s.AirDodgesLeft,
                JumpsLeft = s.JumpsLeft,
                InvincibilityTicks = s.InvincibilityTicks,
                TurnaroundTicks = s.TurnaroundTicks,
                DirHoldTicks = s.DirHoldTicks,
                IsSprinting = s.IsSprinting,
                LastDirX = s.LastDirX,
                LastDirZ = s.LastDirZ,
                WasAirborneDuringKnockback = s.WasAirborneDuringKnockback,
```

Add to `ToState` (inside its `new CharacterState { ... }` initializer, after `Cooldown5 = Cooldown5,`):

```csharp
                AirTimeTicks = AirTimeTicks,
                DashDurationTicks = DashDurationTicks,
                DashDirX = DashDirX,
                DashDirZ = DashDirZ,
                DashCooldownTicks = DashCooldownTicks,
                AirDodgesLeft = AirDodgesLeft,
                JumpsLeft = JumpsLeft,
                InvincibilityTicks = InvincibilityTicks,
                TurnaroundTicks = TurnaroundTicks,
                DirHoldTicks = DirHoldTicks,
                IsSprinting = IsSprinting,
                LastDirX = LastDirX,
                LastDirZ = LastDirZ,
                WasAirborneDuringKnockback = WasAirborneDuringKnockback,
```

Add to `Serialize` (after the existing `Cooldown5` write at offset 61):

```csharp
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(63, 2), AirTimeTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(65, 2), DashDurationTicks);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(67, 4), BitConverter.SingleToInt32Bits(DashDirX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(71, 4), BitConverter.SingleToInt32Bits(DashDirZ));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(75, 2), DashCooldownTicks);
            buffer[77] = AirDodgesLeft;
            buffer[78] = JumpsLeft;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(79, 2), InvincibilityTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(81, 2), TurnaroundTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(83, 2), DirHoldTicks);
            buffer[85] = IsSprinting ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(86, 4), BitConverter.SingleToInt32Bits(LastDirX));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(90, 4), BitConverter.SingleToInt32Bits(LastDirZ));
            buffer[94] = WasAirborneDuringKnockback ? (byte)1 : (byte)0;
```

Add to `Deserialize` (after the existing `Cooldown5` read, before `return packet;`):

```csharp
            packet.AirTimeTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(63, 2));
            packet.DashDurationTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(65, 2));
            packet.DashDirX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(67, 4)));
            packet.DashDirZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(71, 4)));
            packet.DashCooldownTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(75, 2));
            packet.AirDodgesLeft = buffer[77];
            packet.JumpsLeft = buffer[78];
            packet.InvincibilityTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(79, 2));
            packet.TurnaroundTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(81, 2));
            packet.DirHoldTicks = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(83, 2));
            packet.IsSprinting = buffer[85] != 0;
            packet.LastDirX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(86, 4)));
            packet.LastDirZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(90, 4)));
            packet.WasAirborneDuringKnockback = buffer[94] != 0;
```

Add the new method (after `Deserialize`, before the closing `}` of the struct):

```csharp
        /// <summary>
        /// Overwrite only the fields this packet carries on an existing CharacterState,
        /// in place. Unlike ToState() (which builds a fresh CharacterState and leaves every
        /// non-wire field at its default), this preserves everything ApplyTo doesn't touch —
        /// used by LocalTrack (ADR-0011), which must patch its own full-fidelity self state
        /// with the server's authoritative wire fields without clobbering fields the wire
        /// doesn't carry (e.g. AttackElapsedTicks, knockback velocity).
        /// </summary>
        public void ApplyTo(ref CharacterState s)
        {
            s.PX = PositionX; s.PY = PositionY; s.PZ = PositionZ;
            s.VX = VelocityX; s.VY = VelocityY; s.VZ = VelocityZ;
            s.State = (ActionState)CurrentActionState;
            s.IsGrounded = IsGrounded;
            s.StateTicks = StateDurationFrames;
            s.AttackSlot = AttackSlot;
            s.ComboStage = ComboStage;
            s.AnimIndex = AnimIndex;
            s.FacingYaw = FacingYaw;
            s.MatchState = MatchState;
            s.BuffRemainingTicks = BuffRemainingTicks;
            s.BuffActiveFlags = BuffActiveFlags;
            s.HitstunLevel = HitstunLevel;
            s.AimPitch = AimPitch;
            s.Deaths = Deaths;
            s.DamagePercent = DamagePercent;
            s.Cooldown0 = Cooldown0; s.Cooldown1 = Cooldown1; s.Cooldown2 = Cooldown2;
            s.Cooldown3 = Cooldown3; s.Cooldown4 = Cooldown4; s.Cooldown5 = Cooldown5;
            s.AirTimeTicks = AirTimeTicks;
            s.DashDurationTicks = DashDurationTicks;
            s.DashDirX = DashDirX; s.DashDirZ = DashDirZ;
            s.DashCooldownTicks = DashCooldownTicks;
            s.AirDodgesLeft = AirDodgesLeft;
            s.JumpsLeft = JumpsLeft;
            s.InvincibilityTicks = InvincibilityTicks;
            s.TurnaroundTicks = TurnaroundTicks;
            s.DirHoldTicks = DirHoldTicks;
            s.IsSprinting = IsSprinting;
            s.LastDirX = LastDirX; s.LastDirZ = LastDirZ;
            s.WasAirborneDuringKnockback = WasAirborneDuringKnockback;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~CharacterStatePacketTests|FullyQualifiedName~ServerEntityPacketTests" --nologo`
Expected: PASS, all tests in both classes.

- [ ] **Step 5: Rebuild the Shared DLL**

Run: `dotnet build src/Shared/ --nologo`
Expected: build succeeds, DLL copied to `client/Unity/Assets/Plugins/SlopArena.Shared/`.

- [ ] **Step 6: Commit**

```bash
git add src/Shared/CharacterStatePacket.cs tests/Shared.Tests/CharacterStatePacketTests.cs tests/Shared.Tests/ServerEntityPacketTests.cs
git commit -m "feat(netcode): widen CharacterStatePacket with D10 movement-resource fields (ADR-0011)"
```

---

### Task 2: `ActionStateClassifier` — Predictable/Complex partition (D9)

**Files:**
- Create: `src/Shared/Rollback/ActionStateClassifier.cs`
- Test: `tests/Shared.Tests/ActionStateClassifierTests.cs`

**Interfaces:**
- Consumes: `SlopArena.Shared.ActionState` (existing enum).
- Produces: `static bool ActionStateClassifier.IsPredictable(ActionState state)`. Consumed by Task 3 (`LocalTrack`), Task 4 (`PredictedTrack`'s caller), Task 5 (`RollbackSimulator`).

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;

namespace SlopArena.Shared.Tests;

public class ActionStateClassifierTests
{
    [Theory]
    [InlineData(ActionState.Idle, true)]
    [InlineData(ActionState.Dashing, true)]
    [InlineData(ActionState.JumpSquat, true)]
    [InlineData(ActionState.AirDodging, true)]
    [InlineData(ActionState.Attacking, false)]
    [InlineData(ActionState.Hitstun, false)]
    [InlineData(ActionState.Warping, false)]
    [InlineData(ActionState.Sliding, false)] // unused by any code path — not a Predictable member
    public void IsPredictable_MatchesADR0011Partition(ActionState state, bool expected)
    {
        Assert.Equal(expected, SlopArena.Shared.Rollback.ActionStateClassifier.IsPredictable(state));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~ActionStateClassifierTests" --nologo`
Expected: FAIL — `SlopArena.Shared.Rollback` namespace / `ActionStateClassifier` type doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// The Predictable/Complex ActionState partition (ADR-0011, D9). Predictable states
    /// depend only on fields the wire carries (see CharacterStatePacket's D10 fields) —
    /// PredictedTrack and LocalTrack's correction path may safely re-simulate through them.
    /// Complex states depend on the ServerAbility instance layer and/or SpellResolver's
    /// hitbox/projectile list, neither of which is ever serialized — entities in these
    /// states must never be rebuilt from a snapshot (RawTrack for opponents; LocalTrack
    /// skips its own correction replay through them, see LocalTrack.ReconcileWithServer).
    /// </summary>
    public static class ActionStateClassifier
    {
        public static bool IsPredictable(ActionState state) => state is
            ActionState.Idle or ActionState.Dashing or ActionState.JumpSquat or ActionState.AirDodging;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~ActionStateClassifierTests" --nologo`
Expected: PASS, all 8 cases.

- [ ] **Step 5: Commit**

```bash
git add src/Shared/Rollback/ActionStateClassifier.cs tests/Shared.Tests/ActionStateClassifierTests.cs
git commit -m "feat(netcode): add ActionStateClassifier for the Predictable/Complex partition (D9)"
```

---

### Task 3: `LocalTrack` — continuous self sim, snap correction, opponent mirroring

**Files:**
- Create: `src/Shared/Rollback/LocalTrack.cs`
- Test: `tests/Shared.Tests/LocalTrackTests.cs`

**Interfaces:**
- Consumes: `ServerSimulation` (ctor `(ArenaDefinition, IMatchRule?)`, `RegisterEntity(ulong,CharacterDefinition,CharacterState,BakedAnimationData?)`, `SetState(ulong,CharacterState)`, `GetState(ulong)`, `Tick(Dictionary<ulong,InputState>)`, `Resolver`); `CharacterStatePacket.ApplyTo(ref CharacterState)` (Task 1); `ActionStateClassifier.IsPredictable(ActionState)` (Task 2); `ServerEntityPacket` (existing).
- Produces:
  - `LocalTrack(ArenaDefinition arena, ulong entityId, IMatchRule? rule = null)`
  - `void RegisterEntity(CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked)`
  - `CharacterState Tick(InputState input)`
  - `void SyncOpponentMirror(ulong id, CharacterDefinition def, CharacterState state)`
  - `void ReconcileWithServer(ServerEntityPacket packet)`
  - `CharacterState GetState()`
  - `SpellResolver? Resolver { get; }`
  - `int CorrectionCount { get; }` (debug overlay, Task 10)

  Consumed by Task 5 (`RollbackSimulator`).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class LocalTrackTests
{
    private const ulong SelfId = 1;
    private const ulong OpponentId = 2;

    [Fact]
    public void Tick_AdvancesLikeServerSimulation_ForIdleMovement()
    {
        // A LocalTrack ticked with a rightward-move input should move exactly like a
        // plain ServerSimulation given the same input — no divergence for a fresh sim.
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(def, TestHelpers.PlayerState());

        var reference = TestHelpers.MakeSim(arena);
        TestHelpers.RegisterPlayer(reference, def, TestHelpers.PlayerState());

        var input = TestHelpers.Input(moveX: 1f);
        CharacterState localResult = default;
        for (int i = 0; i < 10; i++)
            localResult = track.Tick(input);
        var referenceResult = TestHelpers.TickN(reference, input, 10);

        Assert.Equal(referenceResult.PX, localResult.PX);
        Assert.Equal(referenceResult.PZ, localResult.PZ);
        Assert.Equal(referenceResult.State, localResult.State);
    }

    [Fact]
    public void ReconcileWithServer_SnapsPositionWhenServerDisagrees_DuringPredictableWindow()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(def, TestHelpers.PlayerState());

        // Advance 5 ticks of pure idle (Predictable) — matches the ring's recorded ticks 1..5.
        CharacterState state = default;
        for (int i = 0; i < 5; i++)
            state = track.Tick(default);
        Assert.Equal(0, track.CorrectionCount);

        // Server disagrees on tick 3's position (simulated packet loss / float drift).
        var serverPacket = new CharacterStatePacket
        {
            PositionX = state.PX + 5f, // deliberately wrong vs. what we actually had at tick 3
            CurrentActionState = (byte)ActionState.Idle,
        };
        track.ReconcileWithServer(new ServerEntityPacket { EntityId = SelfId, Tick = 3, State = serverPacket });

        Assert.Equal(1, track.CorrectionCount);
        // After replaying ticks 4-5 forward from the corrected tick-3 base with zero input,
        // PX should now reflect the server's correction, not the original run's value.
        Assert.Equal(state.PX + 5f, track.GetState().PX);
    }

    [Fact]
    public void ReconcileWithServer_SkipsCorrection_WhenPacketTickOutsideWindow()
    {
        var track = new SlopArena.Shared.Rollback.LocalTrack(TestHelpers.TestArena(), SelfId);
        track.RegisterEntity(TestHelpers.MankiDef, TestHelpers.PlayerState());
        for (int i = 0; i < 3; i++) track.Tick(default);

        // Tick 999 was never in this LocalTrack's history — must be a no-op, not a crash.
        track.ReconcileWithServer(new ServerEntityPacket { EntityId = SelfId, Tick = 999, State = default });

        Assert.Equal(0, track.CorrectionCount);
    }

    [Fact]
    public void SyncOpponentMirror_PreventsTargetLockCrash()
    {
        // Regression test for the KeyNotFoundException risk: ServerSimulation.Tick()
        // indexes _states[targetId] directly whenever input.TargetEntityId != 0, for ANY
        // entity, attacking or not. A self-only LocalTrack sim with no opponents registered
        // must not crash when the player has an opponent soft-locked on screen.
        var arena = TestHelpers.TestArena();
        var track = new SlopArena.Shared.Rollback.LocalTrack(arena, SelfId);
        track.RegisterEntity(TestHelpers.MankiDef, TestHelpers.PlayerState());
        track.SyncOpponentMirror(OpponentId, TestHelpers.MankiDef, TestHelpers.PlayerState(x: 5f));

        var input = new InputState { TargetEntityId = (byte)OpponentId };
        var ex = Record.Exception(() => track.Tick(input));

        Assert.Null(ex);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~LocalTrackTests" --nologo`
Expected: FAIL — `SlopArena.Shared.Rollback.LocalTrack` doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// The self entity's continuously-running ServerSimulation (ADR-0011). Never rebuilt
    /// from a received snapshot — fed the player's true InputState every tick. Corrected by
    /// patching wire-serialized fields onto its own full-fidelity history when the server
    /// packet disagrees, replayed forward only across a Predictable-state suffix (D9) —
    /// a Complex tick anywhere in the replay range means "trust the live sim", never rebuilt.
    ///
    /// Also mirrors other entities' current best-known states in as read-only lookup
    /// targets: ServerSimulation.ProcessTargetLock indexes _states[targetId] directly for
    /// any entity whose input carries a nonzero TargetEntityId (screen-center soft-lock,
    /// set every frame an opponent is near screen center — not attack-only). Without a
    /// mirror, that throws KeyNotFoundException the moment an opponent is on screen.
    /// </summary>
    public sealed class LocalTrack
    {
        private readonly ServerSimulation _sim;
        private readonly ulong _entityId;
        private readonly List<(uint Tick, CharacterState State, InputState Input)> _history = new();
        private readonly HashSet<ulong> _mirrored = new();
        private const int WindowCap = 30;
        private uint _localTick;

        public int CorrectionCount { get; private set; }

        public LocalTrack(ArenaDefinition arena, ulong entityId, IMatchRule? rule = null)
        {
            _sim = new ServerSimulation(arena, rule);
            _entityId = entityId;
        }

        public void RegisterEntity(CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
        {
            _sim.RegisterEntity(_entityId, def, initialState, baked);
            _history.Clear();
            _localTick = 0;
            _history.Add((0, _sim.GetState(_entityId), default));
        }

        /// <summary>Register-or-update a read-only mirror of another entity, purely so
        /// ServerSimulation's target-lock lookups resolve. Never rendered from this track.</summary>
        public void SyncOpponentMirror(ulong id, CharacterDefinition def, CharacterState state)
        {
            if (_mirrored.Add(id))
                _sim.RegisterEntity(id, def, state);
            else
                _sim.SetState(id, state);
        }

        public CharacterState Tick(InputState input)
        {
            _sim.Tick(new Dictionary<ulong, InputState> { { _entityId, input } });
            var state = _sim.GetState(_entityId);
            _localTick++;
            _history.Add((_localTick, state, input));
            if (_history.Count > WindowCap) _history.RemoveAt(0);
            return state;
        }

        /// <summary>Apply a received packet for the self entity (D4). Only actually replays
        /// when every ticked state from the packet's tick to "now" was Predictable — a Complex
        /// tick anywhere in that suffix means the live sim (with its real, never-rebuilt
        /// ability instance) is trusted as-is instead.</summary>
        public void ReconcileWithServer(ServerEntityPacket packet)
        {
            int idx = _history.FindIndex(h => h.Tick == packet.Tick);
            if (idx < 0) return; // outside the window — trust the continuous sim, self-heals next packet

            for (int i = idx; i < _history.Count; i++)
                if (!ActionStateClassifier.IsPredictable(_history[i].State.State))
                    return;

            CorrectionCount++;

            var corrected = _history[idx].State;
            packet.State.ApplyTo(ref corrected);
            _sim.SetState(_entityId, corrected);
            _history[idx] = (_history[idx].Tick, corrected, _history[idx].Input);

            for (int i = idx + 1; i < _history.Count; i++)
            {
                _sim.Tick(new Dictionary<ulong, InputState> { { _entityId, _history[i].Input } });
                _history[i] = (_history[i].Tick, _sim.GetState(_entityId), _history[i].Input);
            }
        }

        public CharacterState GetState() => _sim.GetState(_entityId);
        public SpellResolver? Resolver => _sim.Resolver;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~LocalTrackTests" --nologo`
Expected: PASS, all 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Shared/Rollback/LocalTrack.cs tests/Shared.Tests/LocalTrackTests.cs
git commit -m "feat(netcode): add LocalTrack — continuous self sim with snap correction (ADR-0011)"
```

---

### Task 4: `PredictedTrack` — batch rebuild-and-replay for opponents

**Files:**
- Create: `src/Shared/Rollback/PredictedTrack.cs`
- Test: `tests/Shared.Tests/PredictedTrackTests.cs`

**Interfaces:**
- Consumes: `ServerSimulation` (same surface as Task 3); `ServerEntityPacket`; `CharacterStatePacket.ToState()` (existing).
- Produces:
  - `PredictedTrack(ArenaDefinition arena, IMatchRule? rule = null)`
  - `bool IsTracking(ulong id)`
  - `void ApplyBatch(IReadOnlyList<ServerEntityPacket> packets, uint currentLocalTick, IReadOnlyDictionary<ulong, CharacterDefinition> defs, IReadOnlyDictionary<ulong, BakedAnimationData?> baked)`
  - `void StopTracking(ulong id)`
  - `CharacterState GetState(ulong id)`
  - `uint LastFrontierTicks { get; }` (debug overlay, Task 10)

  Consumed by Task 5 (`RollbackSimulator`).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class PredictedTrackTests
{
    private const ulong OpponentId = 2;

    private static ServerEntityPacket MakePacket(uint tick, CharacterState state, bool hasInput, InputState input = default)
        => new ServerEntityPacket
        {
            EntityId = OpponentId,
            Tick = tick,
            State = CharacterStatePacket.FromState(state, tick),
            HasInput = hasInput,
            Input = input,
        };

    [Fact]
    public void ApplyBatch_RegistersAndTracksOnFirstPacket()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var packet = MakePacket(10, TestHelpers.PlayerState(x: 3f), hasInput: false);
        track.ApplyBatch(new[] { packet }, currentLocalTick: 10, defs, baked);

        Assert.True(track.IsTracking(OpponentId));
        Assert.Equal(3f, track.GetState(OpponentId).PX);
    }

    [Fact]
    public void ApplyBatch_ReplaysFrontierWithHeldLastInput()
    {
        // The batch confirms tick 10; the local clock is already at tick 13 (3-tick RTT).
        // The relayed input (moving +X) should be held for those 3 frontier ticks.
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var movingInput = TestHelpers.Input(moveX: 1f);
        var packet = MakePacket(10, TestHelpers.PlayerState(), hasInput: true, movingInput);
        track.ApplyBatch(new[] { packet }, currentLocalTick: 13, defs, baked);

        Assert.Equal(3u, track.LastFrontierTicks);

        // Reference: a plain ServerSimulation confirmed at the same base, ticked 3 times
        // with the same held input, should land at the same position.
        var reference = TestHelpers.MakeSim(arena);
        reference.RegisterEntity(OpponentId, def, TestHelpers.PlayerState());
        var referenceResult = TestHelpers.TickN(reference, movingInput, 3);

        Assert.Equal(referenceResult.PX, track.GetState(OpponentId).PX);
    }

    [Fact]
    public void ApplyBatch_NoInputMarker_HoldsDefaultNotLastRelayed()
    {
        // hasInput=false must reproduce the server's default(InputState) path exactly (D2) —
        // not silently reuse whatever was last relayed.
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };

        var packet = MakePacket(10, TestHelpers.PlayerState(), hasInput: false);
        track.ApplyBatch(new[] { packet }, currentLocalTick: 11, defs, baked);

        var reference = TestHelpers.MakeSim(arena);
        reference.RegisterEntity(OpponentId, def, TestHelpers.PlayerState());
        var referenceResult = TestHelpers.TickDefault(reference, 1);

        Assert.Equal(referenceResult.PX, track.GetState(OpponentId).PX);
    }

    [Fact]
    public void StopTracking_RemovesEntityFromPrediction()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var track = new SlopArena.Shared.Rollback.PredictedTrack(arena);
        var defs = new Dictionary<ulong, CharacterDefinition> { { OpponentId, def } };
        var baked = new Dictionary<ulong, BakedAnimationData?> { { OpponentId, null } };
        track.ApplyBatch(new[] { MakePacket(1, TestHelpers.PlayerState(), false) }, 1, defs, baked);
        Assert.True(track.IsTracking(OpponentId));

        track.StopTracking(OpponentId);

        Assert.False(track.IsTracking(OpponentId));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~PredictedTrackTests" --nologo`
Expected: FAIL — `SlopArena.Shared.Rollback.PredictedTrack` doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// Rebuild-and-replay for opponent entities currently in a Predictable ActionState (D9).
    /// Owns ONE shared ServerSimulation for every tracked opponent, matching how the real
    /// server also sims everyone together — so hurtbox/collision checks between two
    /// tracked opponents behave consistently. Callers (RollbackSimulator, Task 5) must
    /// route Complex-state packets elsewhere (RawTrack) — this class only ever sees
    /// Predictable-state packets and calls StopTracking when an entity leaves that partition.
    /// </summary>
    public sealed class PredictedTrack
    {
        private readonly ServerSimulation _sim;
        private readonly Dictionary<ulong, InputState> _lastKnownInput = new();
        private readonly HashSet<ulong> _registered = new();
        private const uint WindowCap = 30;

        public uint LastFrontierTicks { get; private set; }

        public PredictedTrack(ArenaDefinition arena, IMatchRule? rule = null) => _sim = new ServerSimulation(arena, rule);

        public bool IsTracking(ulong id) => _registered.Contains(id);

        /// <summary>
        /// Apply one network drain's worth of Predictable-state packets, then replay the
        /// frontier (ConfirmedTick..currentLocalTick) using held-last inputs (D5), capped at
        /// WindowCap ticks as a desync guard.
        /// </summary>
        public void ApplyBatch(IReadOnlyList<ServerEntityPacket> packets, uint currentLocalTick,
            IReadOnlyDictionary<ulong, CharacterDefinition> defs,
            IReadOnlyDictionary<ulong, BakedAnimationData?> baked)
        {
            if (packets.Count == 0) { LastFrontierTicks = 0; return; }

            uint maxConfirmedTick = 0;
            foreach (var packet in packets)
            {
                var confirmedState = packet.State.ToState();
                confirmedState.EntityId = packet.EntityId;

                if (_registered.Add(packet.EntityId))
                    _sim.RegisterEntity(packet.EntityId, defs[packet.EntityId], confirmedState,
                        baked.TryGetValue(packet.EntityId, out var b) ? b : null);
                else
                    _sim.SetState(packet.EntityId, confirmedState);

                _lastKnownInput[packet.EntityId] = packet.HasInput ? packet.Input : default;
                if (packet.Tick > maxConfirmedTick) maxConfirmedTick = packet.Tick;
            }

            uint frontierTicks = currentLocalTick > maxConfirmedTick ? currentLocalTick - maxConfirmedTick : 0;
            if (frontierTicks > WindowCap) frontierTicks = WindowCap;
            LastFrontierTicks = frontierTicks;

            for (uint i = 0; i < frontierTicks; i++)
            {
                var inputs = new Dictionary<ulong, InputState>(_registered.Count);
                foreach (var id in _registered)
                    if (_lastKnownInput.TryGetValue(id, out var input))
                        inputs[id] = input;
                _sim.Tick(inputs);
            }
        }

        public void StopTracking(ulong id)
        {
            if (_registered.Remove(id))
            {
                _sim.RemoveEntity(id);
                _lastKnownInput.Remove(id);
            }
        }

        public CharacterState GetState(ulong id) => _sim.GetState(id);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~PredictedTrackTests" --nologo`
Expected: PASS, all 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Shared/Rollback/PredictedTrack.cs tests/Shared.Tests/PredictedTrackTests.cs
git commit -m "feat(netcode): add PredictedTrack — batch rebuild-and-replay for opponents (ADR-0011)"
```

---

### Task 5: `RollbackSimulator` — orchestrator (LocalTrack + PredictedTrack + RawTrack)

**Files:**
- Create: `src/Shared/Rollback/RollbackSimulator.cs`
- Test: `tests/Shared.Tests/RollbackSimulatorTests.cs`

**Interfaces:**
- Consumes: `LocalTrack` (Task 3), `PredictedTrack` (Task 4), `ActionStateClassifier.IsPredictable` (Task 2), `ServerEntityPacket`, `CharacterStatePacket.ToState()`.
- Produces (this is the type Task 7's Unity bridge wraps):
  - `RollbackSimulator(ArenaDefinition arena, ulong selfEntityId, IMatchRule? rule = null)`
  - `void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)`
  - `void Tick(Dictionary<ulong, InputState> inputs)` — reads only `inputs[selfEntityId]`; matches `ISimulationBridge.Tick`'s existing call site in `PvPMatch` exactly (only the local player is ever in that dict — see `PvPMatch.OnMatchFixedUpdate`).
  - `void IngestOpponentBatch(IReadOnlyList<ServerEntityPacket> packets)` — packets for entities other than `selfEntityId` only.
  - `void ReconcileSelf(ServerEntityPacket packet)` — the self entity's own packet only.
  - `CharacterState GetState(ulong id)`
  - `Dictionary<ulong, CharacterState> GetAllStates()`
  - `SpellResolver? Resolver { get; }`
  - `int CorrectionCount { get; }`, `uint LastFrontierTicks { get; }` (debug overlay, Task 10)

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class RollbackSimulatorTests
{
    private const ulong SelfId = 1;
    private const ulong OpponentId = 2;

    private static ServerEntityPacket MakePacket(ulong entityId, uint tick, CharacterState state, bool hasInput = false, InputState input = default)
        => new ServerEntityPacket
        {
            EntityId = entityId,
            Tick = tick,
            State = CharacterStatePacket.FromState(state, tick),
            HasInput = hasInput,
            Input = input,
        };

    [Fact]
    public void SelfEntity_UsesLocalTrack_OpponentIdle_UsesPredictedTrack()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        sim.Tick(new Dictionary<ulong, InputState> { { SelfId, TestHelpers.Input(moveX: 1f) } });
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 1, TestHelpers.PlayerState(x: 10f)) });

        // Self moved (LocalTrack advanced it); opponent reflects the ingested packet.
        Assert.NotEqual(0f, sim.GetState(SelfId).PX);
        Assert.Equal(10f, sim.GetState(OpponentId).PX);
    }

    [Fact]
    public void OpponentEnteringComplexState_SwitchesToRawTrack_NoLongerRebuilt()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        // Tick 1: opponent Idle — PredictedTrack picks it up.
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 1, TestHelpers.PlayerState(x: 10f)) });
        Assert.Equal(10f, sim.GetState(OpponentId).PX);

        // Tick 2: server reports the opponent now Attacking, at a new position — Complex state,
        // must land on RawTrack: rendered exactly as reported, no re-simulation.
        var attackingState = TestHelpers.PlayerState(x: 11f);
        attackingState.State = ActionState.Attacking;
        sim.IngestOpponentBatch(new[] { MakePacket(OpponentId, 2, attackingState) });

        Assert.Equal(11f, sim.GetState(OpponentId).PX);
        Assert.Equal(ActionState.Attacking, sim.GetState(OpponentId).State);
    }

    [Fact]
    public void ReconcileSelf_RoutesToLocalTrack_IncrementsCorrectionCount()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        for (int i = 0; i < 3; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { SelfId, default } });

        var wrongState = TestHelpers.PlayerState(x: 999f);
        sim.ReconcileSelf(MakePacket(SelfId, 1, wrongState));

        Assert.Equal(1, sim.CorrectionCount);
        Assert.Equal(999f, sim.GetState(SelfId).PX);
    }

    [Fact]
    public void GetAllStates_IncludesSelfAndEveryRegisteredOpponent()
    {
        var arena = TestHelpers.TestArena();
        var def = TestHelpers.MankiDef;
        var sim = new SlopArena.Shared.Rollback.RollbackSimulator(arena, SelfId);
        sim.RegisterEntity(SelfId, def, TestHelpers.PlayerState());
        sim.RegisterEntity(OpponentId, def, TestHelpers.PlayerState(x: 10f));

        var all = sim.GetAllStates();

        Assert.True(all.ContainsKey(SelfId));
        Assert.True(all.ContainsKey(OpponentId));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~RollbackSimulatorTests" --nologo`
Expected: FAIL — `SlopArena.Shared.Rollback.RollbackSimulator` doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// Composes LocalTrack (self), PredictedTrack (opponents in a Predictable ActionState),
    /// and RawTrack (opponents in a Complex ActionState — just the latest received state,
    /// no simulation) into one entity-addressable surface (ADR-0011). Shape matches
    /// ISimulationBridge deliberately — RollbackSimulationBridge (Task 7) is a thin wrapper.
    /// </summary>
    public sealed class RollbackSimulator
    {
        private readonly LocalTrack _local;
        private readonly PredictedTrack _predicted;
        private readonly Dictionary<ulong, CharacterState> _rawTrackLatest = new();
        private readonly Dictionary<ulong, CharacterDefinition> _defs = new();
        private readonly Dictionary<ulong, BakedAnimationData?> _baked = new();
        private readonly ulong _selfId;
        private uint _localTick;

        public RollbackSimulator(ArenaDefinition arena, ulong selfEntityId, IMatchRule? rule = null)
        {
            _selfId = selfEntityId;
            _local = new LocalTrack(arena, selfEntityId, rule);
            _predicted = new PredictedTrack(arena, rule);
        }

        public int CorrectionCount => _local.CorrectionCount;
        public uint LastFrontierTicks => _predicted.LastFrontierTicks;

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
        {
            _defs[id] = def;
            _baked[id] = baked;
            if (id == _selfId)
                _local.RegisterEntity(def, initialState, baked);
            else
                _rawTrackLatest[id] = initialState; // opponents start on RawTrack until their first packet
        }

        /// <summary>Advance the self entity one tick. Mirrors every other known entity's
        /// current best-known state into LocalTrack first (target-lock crash fix, Task 3).</summary>
        public void Tick(Dictionary<ulong, InputState> inputs)
        {
            foreach (var id in _defs.Keys)
                if (id != _selfId)
                    _local.SyncOpponentMirror(id, _defs[id], GetState(id));

            var input = inputs.TryGetValue(_selfId, out var i) ? i : default;
            _local.Tick(input);
            _localTick++;
        }

        /// <summary>Feed one network drain's worth of opponent packets. Splits by ActionState
        /// (D9): Predictable entities go to PredictedTrack, Complex entities go to RawTrack.</summary>
        public void IngestOpponentBatch(IReadOnlyList<ServerEntityPacket> packets)
        {
            var predictable = new List<ServerEntityPacket>();
            foreach (var packet in packets)
            {
                var state = packet.State.ToState();
                if (ActionStateClassifier.IsPredictable(state.State))
                {
                    predictable.Add(packet);
                    _rawTrackLatest.Remove(packet.EntityId);
                }
                else
                {
                    _predicted.StopTracking(packet.EntityId);
                    state.EntityId = packet.EntityId;
                    _rawTrackLatest[packet.EntityId] = state;
                }
            }
            if (predictable.Count > 0)
                _predicted.ApplyBatch(predictable, _localTick, _defs, _baked);
        }

        /// <summary>Feed the self entity's own received packet (LocalTrack correction, D4).</summary>
        public void ReconcileSelf(ServerEntityPacket packet) => _local.ReconcileWithServer(packet);

        public CharacterState GetState(ulong id)
        {
            if (id == _selfId) return _local.GetState();
            if (_predicted.IsTracking(id)) return _predicted.GetState(id);
            return _rawTrackLatest.TryGetValue(id, out var s) ? s : default;
        }

        public Dictionary<ulong, CharacterState> GetAllStates()
        {
            var result = new Dictionary<ulong, CharacterState> { [_selfId] = _local.GetState() };
            foreach (var id in _defs.Keys)
                if (id != _selfId) result[id] = GetState(id);
            return result;
        }

        public SpellResolver? Resolver => _local.Resolver;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Shared.Tests/ --filter "FullyQualifiedName~RollbackSimulatorTests" --nologo`
Expected: PASS, all 4 tests.

- [ ] **Step 5: Run the full Shared suite**

Run: `dotnet test tests/Shared.Tests/ --nologo`
Expected: PASS — every existing suite (ability lifecycle, combat pipeline, physics, etc.) unaffected; Tasks 1–5's new tests included.

- [ ] **Step 6: Rebuild the Shared DLL**

Run: `dotnet build src/Shared/ --nologo`
Expected: build succeeds, DLL copied to Unity Plugins.

- [ ] **Step 7: Commit**

```bash
git add src/Shared/Rollback/RollbackSimulator.cs tests/Shared.Tests/RollbackSimulatorTests.cs
git commit -m "feat(netcode): add RollbackSimulator orchestrator — LocalTrack + PredictedTrack + RawTrack (ADR-0011)"
```

---

### Task 6: `NetworkClient` — raw packet accessor for the rollback bridge

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/Network/NetworkClient.cs`

**Interfaces:**
- Consumes: `ServerEntityPacket` (existing, `_receivedQueue: ConcurrentQueue<ServerEntityPacket>` already populated by `ReceiveLoop`).
- Produces: `List<ServerEntityPacket> ReceiveEntityPackets()` — drains the raw queue, keeping `Tick`/`HasInput`/`Input` intact (unlike the old `ReceiveStates()`, which discarded them). Removes `ReceiveStates()` (its only caller, `NetworkSimulationBridge`, is deleted in Task 9 — clean cutover, no other callers per repo-wide grep). Consumed by Task 7's `RollbackSimulationBridge`.

There is no Shared-side unit test for this file (`NetworkClient` is a `MonoBehaviour`, outside `tests/Shared.Tests`'s reach) — verification is a Unity Editor compile, folded into this task's steps.

- [ ] **Step 1: Replace `ReceiveStates()` with `ReceiveEntityPackets()`**

In `client/Unity/Assets/Scripts/Runtime/Network/NetworkClient.cs`, replace the `ReceiveStates()` method (currently lines 119–133) with:

```csharp
        /// <summary>
        /// Drain the receive queue into raw per-entity packets — tick, hasInput/Input relay,
        /// and state all intact. RollbackSimulationBridge routes self packets to
        /// RollbackSimulator.ReconcileSelf and everything else to IngestOpponentBatch.
        /// </summary>
        public List<ServerEntityPacket> ReceiveEntityPackets()
        {
            var result = new List<ServerEntityPacket>();
            while (_receivedQueue.TryDequeue(out var entry))
            {
                result.Add(entry);
                LastServerTick = entry.Tick;
            }
            return result;
        }
```

(No new `using` needed — `List<T>` and `ServerEntityPacket` are already in scope via `System.Collections.Generic` and `SlopArena.Shared`.)

- [ ] **Step 2: Verify no remaining references to `ReceiveStates`**

Run: `grep -rn "ReceiveStates" client/Unity/Assets/Scripts/`
Expected: no output (Task 9 deletes the one caller, `NetworkSimulationBridge`, but do this check now — if it prints anything other than the method you just renamed away, stop and re-check before continuing).

- [ ] **Step 3: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/Network/NetworkClient.cs
git commit -m "feat(netcode): replace NetworkClient.ReceiveStates with raw ReceiveEntityPackets"
```

---

### Task 7: `RollbackSimulationBridge` — Unity `ISimulationBridge` adapter

**Files:**
- Create: `client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs`

**Interfaces:**
- Consumes: `RollbackSimulator` (Task 5, full surface); `NetworkClient.SendInput(InputState,uint)` / `.ReceiveEntityPackets()` (Task 6); `ISimulationBridge` (existing interface, unchanged).
- Produces: `RollbackSimulationBridge : ISimulationBridge`, constructed `(ArenaDefinition arena, NetworkClient client, ulong selfEntityId, IMatchRule? rule = null)`. Also exposes `CorrectionCount`/`LastFrontierTicks` passthroughs (not part of `ISimulationBridge` — read via the concrete field, same pattern `NetworkSimulationBridge`/`LocalSimulationBridge` already use for bridge-specific extras). Consumed by Task 9 (`PvPMatch`) and Task 10 (debug overlay).

No headless test — verified via the Unity Editor batchmode compile gate (Task 9's verification step covers this file too, since it can't compile in isolation without `PvPMatch` wiring it in).

- [ ] **Step 1: Implement**

```csharp
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using SlopArena.Client.Network;

namespace SlopArena.Client.Simulation
{
    /// <summary>
    /// ISimulationBridge backed by RollbackSimulator (ADR-0011): the self entity predicts
    /// continuously (LocalTrack); opponents predict while in a Predictable ActionState
    /// (PredictedTrack) and render raw from the server otherwise (RawTrack). Replaces
    /// NetworkSimulationBridge for PvPMatch — Training keeps LocalSimulationBridge.
    /// </summary>
    public class RollbackSimulationBridge : ISimulationBridge
    {
        private readonly RollbackSimulator _core;
        private readonly NetworkClient _client;
        private readonly ulong _selfId;
        private uint _tick;

        public RollbackSimulationBridge(ArenaDefinition arena, NetworkClient client, ulong selfEntityId, IMatchRule? rule = null)
        {
            _core = new RollbackSimulator(arena, selfEntityId, rule);
            _client = client;
            _selfId = selfEntityId;
        }

        /// <summary>Debug overlay data (Task 10) — not part of ISimulationBridge.</summary>
        public int CorrectionCount => _core.CorrectionCount;
        public uint LastFrontierTicks => _core.LastFrontierTicks;

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => _core.RegisterEntity(id, def, initialState, baked);

        public void Tick(Dictionary<ulong, InputState> inputs)
        {
            if (inputs.TryGetValue(_selfId, out var input))
                _client.SendInput(input, _tick);
            _tick++;

            _core.Tick(inputs);

            var packets = _client.ReceiveEntityPackets();
            if (packets.Count == 0) return;

            var opponentBatch = new List<ServerEntityPacket>(packets.Count);
            foreach (var packet in packets)
            {
                if (packet.EntityId == _selfId)
                    _core.ReconcileSelf(packet);
                else
                    opponentBatch.Add(packet);
            }
            if (opponentBatch.Count > 0)
                _core.IngestOpponentBatch(opponentBatch);
        }

        public CharacterState GetState(ulong id) => _core.GetState(id);
        public Dictionary<ulong, CharacterState> GetAllStates() => _core.GetAllStates();
        public SpellResolver? Resolver => _core.Resolver;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/Simulation/RollbackSimulationBridge.cs
git commit -m "feat(netcode): add RollbackSimulationBridge — Unity adapter for RollbackSimulator (ADR-0011)"
```

(Compile verification happens in Task 9, once `PvPMatch` actually constructs this class — Unity won't compile an added file in isolation meaningfully until it has a caller.)

---

### Task 8: Delay/loss harness (dev-only)

**Files:**
- Create: `client/Unity/Assets/Scripts/Runtime/Network/NetworkConditionHarness.cs`

**Interfaces:**
- Consumes: nothing new — wraps calls the developer makes manually around `NetworkClient.SendInput`/`.ReceiveEntityPackets()` in a debug build; does not change `NetworkClient` itself, so Task 6/7 don't depend on this task and it can be built any time after Task 6.
- Produces: `NetworkConditionHarness` — a plain (non-`MonoBehaviour`) class taking a drop chance, duplicate chance, reorder window, and extra-RTT-in-ticks, exposing `bool ShouldDrop()`, `IEnumerable<ServerEntityPacket> ApplyReorderAndDuplicate(List<ServerEntityPacket> incoming)`, and `void QueueDelayed(ServerEntityPacket packet, uint availableAtTick, uint currentTick)` / `List<ServerEntityPacket> DrainDue(uint currentTick)`. Used manually by wiring it into `RollbackSimulationBridge.Tick` behind a `#if UNITY_EDITOR || DEVELOPMENT_BUILD` conditional — that wiring is a manual dev step per the Test Plan in `docs/plans/2026-08-02-rollback-netcode.md`, not part of this task's automated deliverable (the harness's job is to exist and be unit-testable in isolation; wiring it in is a playtest-prep action, not a shippable code change).

**Files:**
- Test: `tests/Shared.Tests/NetworkConditionHarnessTests.cs` — wait, this class lives in `client/Unity`, not `src/Shared`, so it cannot be exercised by `tests/Shared.Tests` (that project only references `src/Shared`). Keep the harness's actual logic (RNG-free, deterministic given a seeded `System.Random`) in a plain class so it's at least readable/reviewable without Unity, but its only real verification is manual: use it during the Unity playtest checklist (`docs/plans/2026-08-02-rollback-netcode.md` Test Plan step 2).

- [ ] **Step 1: Implement**

```csharp
using System;
using System.Collections.Generic;
using SlopArena.Shared;

namespace SlopArena.Client.Network
{
    /// <summary>
    /// Dev-only seam for exercising rollback under simulated bad network conditions before
    /// the friends playtest (docs/plans/2026-08-02-rollback-netcode.md, Delay/loss harness).
    /// Not wired into NetworkClient by default — a developer wraps ReceiveEntityPackets()
    /// output through this manually, behind a DEVELOPMENT_BUILD/editor-only toggle, when
    /// testing. Deterministic given a seed, so behavior is reproducible across runs.
    /// </summary>
    public sealed class NetworkConditionHarness
    {
        private readonly Random _random;
        private readonly float _dropChance;
        private readonly float _duplicateChance;
        private readonly uint _extraDelayTicks;
        private readonly List<(uint AvailableAtTick, ServerEntityPacket Packet)> _delayed = new();

        public NetworkConditionHarness(float dropChance = 0f, float duplicateChance = 0f, uint extraDelayTicks = 0, int seed = 0)
        {
            _dropChance = dropChance;
            _duplicateChance = duplicateChance;
            _extraDelayTicks = extraDelayTicks;
            _random = new Random(seed);
        }

        /// <summary>Feed freshly-received packets in; get back what the client should actually
        /// "receive" this tick, after simulated drop, duplication, and injected RTT delay.</summary>
        public List<ServerEntityPacket> Process(List<ServerEntityPacket> incoming, uint currentTick)
        {
            foreach (var packet in incoming)
            {
                if (_random.NextDouble() < _dropChance) continue;
                _delayed.Add((currentTick + _extraDelayTicks, packet));
                if (_random.NextDouble() < _duplicateChance)
                    _delayed.Add((currentTick + _extraDelayTicks, packet));
            }

            var due = new List<ServerEntityPacket>();
            for (int i = _delayed.Count - 1; i >= 0; i--)
            {
                if (_delayed[i].AvailableAtTick > currentTick) continue;
                due.Add(_delayed[i].Packet);
                _delayed.RemoveAt(i);
            }
            return due;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/Network/NetworkConditionHarness.cs
git commit -m "feat(netcode): add NetworkConditionHarness — dev-only drop/duplicate/delay seam"
```

---

### Task 9: Wire `PvPMatch` to `RollbackSimulationBridge`; delete `NetworkSimulationBridge` (clean cutover)

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs`
- Delete: `client/Unity/Assets/Scripts/Runtime/Simulation/NetworkSimulationBridge.cs`

**Interfaces:**
- Consumes: `RollbackSimulationBridge` (Task 7).
- Produces: `PvPMatch._bridge` is now `RollbackSimulationBridge`. No other file references `NetworkSimulationBridge` or `NetworkClient.ReceiveStates` (verified by grep before this task was written — only caller was `PvPMatch`).

- [ ] **Step 1: Update `PvPMatch.cs`**

Change the field declaration (currently lines 35–37):

```csharp
        private MatchState _lastMatchState = MatchState.Waiting;
        private RollbackSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;
```

Change the bridge construction inside `OnMatchStart()` (currently lines 62–65):

```csharp
            // Bridge
            _networkClient.EntityId = PlayerEntityId;
            _bridge = new RollbackSimulationBridge(arena, _networkClient, PlayerEntityId);
            _networkClient.Connect(MatchConfig.ServerIP, MatchConfig.ServerPort);
```

(`arena` is already in scope at this point in `OnMatchStart` — it's the `ArenaDefinition` loaded a few lines above at `var arenaOpt = ArenaBinaryFormat.LoadFromFile(arenaPath); ... if (arenaOpt is not ArenaDefinition arena) { ...; return; }`.)

Update the class doc comment (currently lines 16–19):

```csharp
    /// <summary>
    /// PvP match backed by a remote server. Uses RollbackSimulationBridge (ADR-0011):
    /// the local player predicts continuously; opponents predict while in a movement
    /// state and render raw from the server otherwise.
    /// </summary>
```

- [ ] **Step 2: Delete `NetworkSimulationBridge.cs`**

Run: `rm client/Unity/Assets/Scripts/Runtime/Simulation/NetworkSimulationBridge.cs client/Unity/Assets/Scripts/Runtime/Simulation/NetworkSimulationBridge.cs.meta`

(Delete the `.meta` file alongside it — Unity regenerates GUIDs on missing `.meta` otherwise, which can break scene references. Check whether a `.meta` file exists first with `ls client/Unity/Assets/Scripts/Runtime/Simulation/ | grep NetworkSimulationBridge` — if the `.meta` isn't there, skip it.)

- [ ] **Step 3: Verify no dangling references**

Run: `grep -rn "NetworkSimulationBridge" client/Unity/Assets/`
Expected: no output.

- [ ] **Step 4: Unity Editor compile gate**

Run: `"$UNITY_EDITOR" -batchmode -quit -projectPath client/Unity -logFile -` (per `.omp/AGENTS.md` — `$UNITY_EDITOR` is the Unity 6000.0.78f1 install path; first run builds `Library/` from scratch and is slow, subsequent runs are fast).
Expected: exits 0, no `CS####` compile errors in the log for `PvPMatch.cs`, `RollbackSimulationBridge.cs`, `RollbackSimulator.cs`/`LocalTrack.cs`/`PredictedTrack.cs`/`ActionStateClassifier.cs` (via the copied Shared DLL), or `NetworkClient.cs`.

If this is a worktree (not the main checkout), first run `scripts/setup-worktree-unity-packages.sh` — gitignored paid packages (Animancer) don't travel to worktrees and the gate fails on Animancer type errors otherwise.

- [ ] **Step 5: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs
git rm client/Unity/Assets/Scripts/Runtime/Simulation/NetworkSimulationBridge.cs
git commit -m "feat(netcode): switch PvPMatch to RollbackSimulationBridge, remove NetworkSimulationBridge"
```

- [ ] **Step 6: Unity playtest — two local clients**

Per `docs/plans/2026-08-02-rollback-netcode.md` Test Plan step 2: run host-and-play with two local clients (0 RTT baseline first — confirms no regression), then wire in `NetworkConditionHarness` (Task 8) behind a dev toggle and re-test with injected delay. Check: movement feels responsive for the local player; opponent movement is smooth, not choppy; opponent attacks/hitstun still display exactly as before (RawTrack — no change expected there); no exceptions in the Unity console (this is where the Task 3 target-lock mirror fix gets its real-world proof — soft-lock onto a moving opponent and confirm no `KeyNotFoundException`).

If this surfaces a problem, stop and report it rather than proceeding to Task 10 — this is the step that actually proves the feature works, per the Delivery Contract's verification requirement.

---

### Task 10: F3 debug overlay — correction counter + frontier window

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs`

**Interfaces:**
- Consumes: `RollbackSimulationBridge.CorrectionCount` / `.LastFrontierTicks` (Task 7).
- Produces: an `OnGUI` override in `PvPMatch` that draws the two counters when F3 is held, following `MatchBase`'s existing `protected virtual void OnGUI()` pattern (crosshair rendering) — this task adds a sibling override, not a new overlay framework.

- [ ] **Step 1: Add the override**

Add to `PvPMatch.cs` (a new method — place it near the other `protected override` methods, e.g. after `OnMatchFixedUpdate`):

```csharp
        protected override void OnGUI()
        {
            base.OnGUI();
            if (!UnityEngine.Input.GetKey(KeyCode.F3)) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            GUI.Label(new Rect(10, 10, 400, 20), $"Corrections: {_bridge.CorrectionCount}", style);
            GUI.Label(new Rect(10, 30, 400, 20), $"Frontier window: {_bridge.LastFrontierTicks} ticks", style);
        }
```

- [ ] **Step 2: Commit**

```bash
git add client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs
git commit -m "feat(netcode): add F3 debug overlay for rollback correction count + frontier window"
```

---

### Task 11: Sync `docs/systems/netcode-architecture.md`

**Files:**
- Modify: `docs/systems/netcode-architecture.md`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Update §4b (Server → Client packet layout)**

Update the CharacterStatePacket byte-offset table (currently ending at offset 51-62 for cooldowns) to append the 14 D10 fields at offsets 63-94, and update every "63 bytes"/"75 bytes"/"95 bytes"/"76 bytes" size reference in that section to the new 95/107/127/108 values (mirroring the exact numbers already locked into the Task 1 tests).

- [ ] **Step 2: Rewrite §6 ("Prediction & Rollback — Not Implemented")**

Replace the section content with a short summary pointing at the real design docs rather than re-deriving them: state that self prediction (LocalTrack) and movement-state opponent prediction (PredictedTrack) are implemented per `docs/plans/2026-08-02-rollback-netcode.md` and `docs/adr/0011-rollback-scope-track-model.md`, opponents mid-attack/hitstun/warp still render raw (RawTrack, same as this section previously described for everyone), and link both docs instead of duplicating their content. Rename the section from "Not Implemented" to "Prediction & Rollback".

- [ ] **Step 3: Commit**

```bash
git add docs/systems/netcode-architecture.md
git commit -m "docs(netcode): sync architecture doc with the shipped rollback design (ADR-0011)"
```

---

## Self-Review

**1. Spec coverage** (against `docs/plans/2026-08-02-rollback-netcode.md` + `docs/adr/0011-rollback-scope-track-model.md`):
- D10 (wire widening) → Task 1.
- D9 (Predictable/Complex partition) → Task 2.
- LocalTrack + correction (D3/D4 for self) → Task 3.
- PredictedTrack + hold-last frontier (D3/D4/D5 for opponents) → Task 4.
- RawTrack + track switching on ActionState transition (D9) → Task 5 (`RollbackSimulator.IngestOpponentBatch`).
- `RollbackSimulationBridge`/third `ISimulationBridge` impl (D7) → Task 7.
- Delay/loss harness (D8) → Task 8.
- `PvPMatch` cutover + `NetworkSimulationBridge` removal → Task 9.
- F3 debug overlay (Test Plan step 3 in the design doc) → Task 10.
- Doc sync → Task 11.
- Golden-tick determinism (D8) → covered across Tasks 1, 3, 4, 5's test suites (not one monolithic "golden-tick" file, but the same claims: LocalTrack matches a reference sim exactly absent injected drift; PredictedTrack's replay matches a reference sim fed the same held inputs; the no-input-marker path is asserted separately from the held-last path per D2's own distinction).
- Gap: the design doc's "elimination tail" and "gap tick" golden cases aren't separately named as tests above. They're implicitly covered by existing mechanisms (`_rule.IsEliminated` and the `if (inputs.Count > 0)` gate are server-side, untouched by this plan) but not explicitly re-verified client-side. **Not adding a task for this** — both paths are exercised by the existing `ServerSimulationTests`/`SimulationInvariantTests` suites already in `tests/Shared.Tests`, and Task 5's `RollbackSimulator` doesn't introduce new elimination/gap-tick logic of its own (it delegates entirely to `ServerSimulation`, which those suites already cover).

**2. Placeholder scan:** no `TBD`/`TODO`/"add error handling"/"similar to Task N" found — re-read each task's Step 3 above; every one is real, compiling-shape C#.

**3. Type consistency:** `RollbackSimulator`'s public surface (`RegisterEntity`, `Tick`, `GetState`, `GetAllStates`, `Resolver`) matches `ISimulationBridge` exactly, verified against `ISimulationBridge.cs`'s actual signatures read from source, not assumed. `LocalTrack`/`PredictedTrack`'s constructor/method names used in Task 5's `RollbackSimulator` match Task 3/4's actual declared signatures (`RegisterEntity(def,state,baked)`, `Tick(input)`, `SyncOpponentMirror(id,def,state)`, `ReconcileWithServer(packet)`, `IsTracking(id)`, `ApplyBatch(packets,tick,defs,baked)`, `StopTracking(id)`, `GetState(id)`) — cross-checked field-by-field while writing Task 5, not copied from memory.
