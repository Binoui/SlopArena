using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Downlink per-entity envelope: entityId(8) + tick(4) + CharacterStatePacket(63)
/// + hasInput(1) + InputState(19) when the server consumed input that tick.
/// Input relay for client rollback prediction (issue #80, ADR-0010).
/// </summary>
public class ServerEntityPacketTests
{
    private static CharacterStatePacket SampleState()
    {
        var state = new CharacterState
        {
            PX = 12.5f,
            PY = 3.25f,
            PZ = -7.1f,
            VX = 1.5f,
            VY = -2.5f,
            VZ = 0.75f,
            State = ActionState.Attacking,
            StateTicks = 42,
            IsGrounded = true,
            AttackSlot = 3,
            ComboStage = 2,
            AnimIndex = 5,
            FacingYaw = 1.234f,
            MatchState = MatchState.Playing,
            BuffRemainingTicks = 60,
            BuffActiveFlags = 0b_0011,
            HitstunLevel = 2,
            AimPitch = -0.5f,
            Deaths = 2,
            DamagePercent = 87,
            LockOn = true,
            Cooldown0 = 1,
            Cooldown1 = 12,
            Cooldown2 = 33,
            Cooldown3 = 44,
            Cooldown4 = 55,
            Cooldown5 = 66,
        };
        return CharacterStatePacket.FromState(state, tick: 999);
    }

    private static InputState SampleInput() => new InputState
    {
        MoveX = 0.75f,
        MoveY = -0.25f,
        Up = true,
        Down = true,
        Left = true,
        Right = true,
        Jump = true,
        Dash = true,
        Burst = true,
        IsAiming = true,
        ActiveSlot = 3,
        FacingYaw = 420,
        AimYaw = -18000,
        AimPitch = 9000,
        AimDistance = 6500,
        TargetEntityId = 7,
    };

    [Fact]
    public void RoundTrip_WithRelay_PreservesAllFields()
    {
        // Arrange
        var statePacket = SampleState();
        var input = SampleInput();
        var packet = new ServerEntityPacket
        {
            EntityId = 2,
            Tick = 999,
            State = statePacket,
            HasInput = true,
            Input = input,
        };

        // Act: Serialize → Deserialize
        var buffer = new byte[ServerEntityPacket.MaxSize];
        packet.Serialize(buffer);
        var restored = ServerEntityPacket.Deserialize(buffer);

        // Assert: envelope + tick echo + state + relayed input all survive
        Assert.Equal(2UL, restored.EntityId);
        Assert.Equal(999u, restored.Tick);
        Assert.Equal(999u, restored.State.TickNumber); // tick echo unchanged — reconciliation anchor
        Assert.True(restored.HasInput);

        Assert.Equal(statePacket.PositionX, restored.State.PositionX);
        Assert.Equal(statePacket.PositionY, restored.State.PositionY);
        Assert.Equal(statePacket.PositionZ, restored.State.PositionZ);
        Assert.Equal(statePacket.VelocityX, restored.State.VelocityX);
        Assert.Equal(statePacket.CurrentActionState, restored.State.CurrentActionState);
        Assert.Equal(statePacket.IsGrounded, restored.State.IsGrounded);
        Assert.Equal(statePacket.StateDurationFrames, restored.State.StateDurationFrames);
        Assert.Equal(statePacket.AttackSlot, restored.State.AttackSlot);
        Assert.Equal(statePacket.ComboStage, restored.State.ComboStage);
        Assert.Equal(statePacket.AnimIndex, restored.State.AnimIndex);
        Assert.Equal(statePacket.FacingYaw, restored.State.FacingYaw);
        Assert.Equal(statePacket.MatchState, restored.State.MatchState);
        Assert.Equal(statePacket.DamagePercent, restored.State.DamagePercent);
        Assert.True(restored.State.LockOn);

        Assert.Equal(input.MoveX, restored.Input.MoveX);
        Assert.Equal(input.MoveY, restored.Input.MoveY);
        Assert.Equal(input.Up, restored.Input.Up);
        Assert.Equal(input.Down, restored.Input.Down);
        Assert.Equal(input.Left, restored.Input.Left);
        Assert.Equal(input.Right, restored.Input.Right);
        Assert.Equal(input.Jump, restored.Input.Jump);
        Assert.Equal(input.Dash, restored.Input.Dash);
        Assert.Equal(input.Burst, restored.Input.Burst);
        Assert.Equal(input.IsAiming, restored.Input.IsAiming);
        Assert.Equal(input.ActiveSlot, restored.Input.ActiveSlot);
        Assert.Equal(input.FacingYaw, restored.Input.FacingYaw);
        Assert.Equal(input.AimYaw, restored.Input.AimYaw);
        Assert.Equal(input.AimPitch, restored.Input.AimPitch);
        Assert.Equal(input.AimDistance, restored.Input.AimDistance);
        Assert.Equal(input.TargetEntityId, restored.Input.TargetEntityId);
    }

    [Fact]
    public void RoundTrip_NoInputMarker_EncodesFlagWithoutRelay()
    {
        // Arrange: empty queue / eliminated entity path — explicit no-input marker
        var packet = new ServerEntityPacket
        {
            EntityId = 1,
            Tick = 99,
            State = SampleState(),
            HasInput = false,
        };

        // Act
        var buffer = new byte[ServerEntityPacket.MaxSize];
        packet.Serialize(buffer);
        Assert.Equal(ServerEntityPacket.NoInputSize, packet.WireSize);
        var restored = ServerEntityPacket.Deserialize(buffer);

        // Assert: flag reads 0, no stale input is ever carried
        Assert.Equal(1UL, restored.EntityId);
        Assert.Equal(99u, restored.Tick);
        Assert.False(restored.HasInput);
        Assert.Equal(0, restored.Input.ActiveSlot);
        Assert.False(restored.Input.Up);
        Assert.False(restored.Input.Jump);
    }

    [Fact]
    public void TruncatedRelay_DecodesAsNoInputMarker()
    {
        // A relay flag=1 whose 19 input bytes never arrived is a protocol violation;
        // decode leniently to the no-input marker (mirrors InputState.Deserialize guards).
        var packet = new ServerEntityPacket
        {
            EntityId = 1,
            Tick = 5,
            State = SampleState(),
            HasInput = true,
            Input = SampleInput(),
        };
        var buffer = new byte[ServerEntityPacket.MaxSize];
        packet.Serialize(buffer);
        var truncated = buffer.AsSpan(0, ServerEntityPacket.NoInputSize).ToArray();

        var restored = ServerEntityPacket.Deserialize(truncated);

        Assert.Equal(1UL, restored.EntityId);
        Assert.Equal(5u, restored.Tick);
        Assert.False(restored.HasInput);
    }

    [Fact]
    public void SizeConstants_AssertWireLayout()
    {
        // Downlink max packet size is a wire contract (issue #80, widened per ADR-0011/D10
        // + hitstop/ADR-0012 + burst/ADR-0014 + slots 6-10/JumpHeldTicks/ADR-0016
        // + LockOn/ADR-0018): 125B base (8 entityId + 4 tick + 113 CharacterStatePacket)
        // + 1B flag + 20B input.
        Assert.Equal(8 + 4 + CharacterStatePacket.Size, ServerEntityPacket.BaseSize);
        Assert.Equal(125, ServerEntityPacket.BaseSize);
        Assert.Equal(1 + InputState.Size, ServerEntityPacket.RelaySize);
        Assert.Equal(21, ServerEntityPacket.RelaySize);
        Assert.Equal(146, ServerEntityPacket.MaxSize);
        Assert.Equal(126, ServerEntityPacket.NoInputSize);
        // Uplink format: 20B InputState (32B full uplink packet with entityId+tick) — the
        // ADR-0016 short-hop bit is the only addition; slot count still fits the byte.
        Assert.Equal(20, InputState.Size);
    }

    [Fact]
    public void InputState_Roundtrips_JumpHeldBit()
    {
        var input = new InputState
        {
            MoveX = 0.5f, MoveY = -0.5f,
            Up = true, Down = true, Left = false, Right = true,
            Jump = true, JumpHeld = true, Dash = true, Burst = true, IsAiming = true,
            ActiveSlot = AbilitySlots.A,
        };
        Span<byte> buf = stackalloc byte[InputState.Size];
        input.Write(buf);
        var restored = InputState.Deserialize(buf);

        Assert.True(restored.JumpHeld);
        Assert.True(restored.Jump);
        Assert.True(restored.Down);
        Assert.Equal(AbilitySlots.A, restored.ActiveSlot);
    }

    [Fact]
    public void InputState_Roundtrips_FaceToCamera_And_ToggleLock_Bits()
    {
        // flags2 bits 2 (LMB facing snap, ADR-0017) and 4 (RMB lock toggle, ADR-0018)
        // must survive the wire — they drive sim-authoritative facing/lock state that
        // rollback replay depends on. Bit 1 (JumpHeld) must coexist.
        var input = new InputState
        {
            FaceToCamera = true,
            ToggleLock = true,
            JumpHeld = true,
        };
        Span<byte> buf = stackalloc byte[InputState.Size];
        input.Write(buf);
        var restored = InputState.Deserialize(buf);

        Assert.True(restored.FaceToCamera);
        Assert.True(restored.ToggleLock);
        Assert.True(restored.JumpHeld);

        // Defaults decode as off
        Span<byte> cleanBuf = stackalloc byte[InputState.Size];
        default(InputState).Write(cleanBuf);
        var restoredClean = InputState.Deserialize(cleanBuf);
        Assert.False(restoredClean.FaceToCamera);
        Assert.False(restoredClean.ToggleLock);
        Assert.False(restoredClean.JumpHeld);
    }
}
