using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Downlink per-entity envelope: entityId(8) + tick(4) + CharacterStatePacket(156)
/// + hasInput(1) + InputState(21) when the server consumed input that tick.
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
            HitstunLevel = 2,
            AimPitch = -0.5f,
            Deaths = 2,
            DamagePercent = 87,
            LockOn = true,
            LandingLagTicks = 18,
            IsFastFalling = true,
            JumpFromSlide = true,
            SlideAttackCarryActive = true,
            CrouchSettled = true,
            QueuedCrouchBrace = true,
            InPostHitstunFlight = true,
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
        DownPressed = true,
        Down = true,
        Left = true,
        Right = true,
        Jump = true,
        Dash = true,
        Burst = true,
        ShieldHeld = true,
        ShieldPressed = true,
        GrabPressed = true,
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
        Assert.Equal(statePacket.MatchState, restored.State.MatchState);
        Assert.Equal(statePacket.DamagePercent, restored.State.DamagePercent);
        Assert.Equal(statePacket.LandingLagTicks, restored.State.LandingLagTicks);
        Assert.True(restored.State.LockOn);
        Assert.True(restored.State.IsFastFalling);
        Assert.True(restored.State.JumpFromSlide);
        Assert.True(restored.State.SlideAttackCarryActive);
        Assert.True(restored.State.CrouchSettled);
        Assert.True(restored.State.QueuedCrouchBrace);
        Assert.True(restored.State.InPostHitstunFlight);
        Assert.Equal(input.MoveX, restored.Input.MoveX);
        Assert.Equal(input.MoveY, restored.Input.MoveY);
        Assert.Equal(input.Up, restored.Input.Up);
        Assert.Equal(input.Down, restored.Input.Down);
        Assert.Equal(input.DownPressed, restored.Input.DownPressed);
        Assert.Equal(input.Left, restored.Input.Left);
        Assert.Equal(input.Right, restored.Input.Right);
        Assert.Equal(input.Jump, restored.Input.Jump);
        Assert.Equal(input.Dash, restored.Input.Dash);
        Assert.Equal(input.Burst, restored.Input.Burst);
        Assert.Equal(input.ShieldHeld, restored.Input.ShieldHeld);
        Assert.Equal(input.ShieldPressed, restored.Input.ShieldPressed);
        Assert.Equal(input.GrabPressed, restored.Input.GrabPressed);
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
        var restored = ServerEntityPacket.Deserialize(buffer.AsSpan(0, packet.WireSize));

        // Assert: flag reads 0, no stale input is ever carried
        Assert.Equal(1UL, restored.EntityId);
        Assert.Equal(99u, restored.Tick);
        Assert.False(restored.HasInput);
        Assert.Equal(0, restored.Input.ActiveSlot);
        Assert.False(restored.Input.Up);
        Assert.False(restored.Input.Jump);
    }

    [Fact]
    public void TruncatedRelay_IsRejected()
    {
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

        Assert.Throws<ArgumentException>(() => ServerEntityPacket.Deserialize(truncated));
    }

    [Fact]
    public void SizeConstants_AssertWireLayout()
    {
        // Downlink max packet: 8 entityId + 4 tick + 156 state + 1 marker + 21 input.
        Assert.Equal(8 + 4 + CharacterStatePacket.Size, ServerEntityPacket.BaseSize);
        Assert.Equal(168, ServerEntityPacket.BaseSize);
        Assert.Equal(1 + InputState.Size, ServerEntityPacket.RelaySize);
        Assert.Equal(22, ServerEntityPacket.RelaySize);
        Assert.Equal(190, ServerEntityPacket.MaxSize);
        Assert.Equal(169, ServerEntityPacket.NoInputSize);
        Assert.Equal(21, InputState.Size);
    }

    [Fact]
    public void InputState_Roundtrips_JumpHeldBit()
    {
        var input = new InputState
        {
            MoveX = 0.5f, MoveY = -0.5f,
            Up = true, Down = true, DownPressed = true, Left = false, Right = true,
            Jump = true, JumpHeld = true, Dash = true, Burst = true, IsAiming = true,
            ActiveSlot = AbilitySlots.A,
        };
        Span<byte> buf = stackalloc byte[InputState.Size];
        input.Write(buf);
        var restored = InputState.Deserialize(buf);

        Assert.True(restored.JumpHeld);
        Assert.True(restored.Jump);
        Assert.True(restored.Down);
        Assert.True(restored.DownPressed);
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
    [Fact]
    public void CodecBoundaries_RejectTruncationVersionAndRelayMismatch()
    {
        var input = new byte[InputState.Size];
        default(InputState).Write(input);
        Assert.Throws<ArgumentException>(() => InputState.Deserialize(input.AsSpan(0, InputState.Size - 1)));
        input[20] = 1;
        Assert.Throws<InvalidDataException>(() => InputState.Deserialize(input));

        var packet = new ServerEntityPacket
        {
            EntityId = 7,
            Tick = 8,
            State = SampleState(),
            HasInput = false,
        };
        var noInput = new byte[ServerEntityPacket.NoInputSize];
        packet.Serialize(noInput);
        noInput[ServerEntityPacket.BaseSize] = 2;
        Assert.Throws<InvalidDataException>(() => ServerEntityPacket.Deserialize(noInput));

        var full = new byte[ServerEntityPacket.MaxSize];
        packet.HasInput = true;
        packet.Input = SampleInput();
        packet.Serialize(full);
        full[ServerEntityPacket.BaseSize] = 0;
        Assert.Throws<InvalidDataException>(() => ServerEntityPacket.Deserialize(full));
        Assert.Throws<ArgumentException>(() => ServerEntityPacket.Deserialize(full.AsSpan(0, full.Length - 1)));
    }
}
