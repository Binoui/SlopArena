using Xunit;

namespace SlopArena.Shared.Tests;

public class CharacterStatePacketTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        // Arrange: a CharacterState with every PvP-relevant field set to non-default values
        var original = new CharacterState
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
            Cooldown0 = 1,
            Cooldown1 = 12,
            Cooldown2 = 33,
            Cooldown3 = 44,
            Cooldown4 = 55,
            Cooldown5 = 66,
            AirTimeTicks = 37,
            DashDurationTicks = 9,
            DashDirX = 0.6f,
            DashDirZ = -0.8f,
            DashCooldownTicks = 20,
            AirDodgesLeft = 1,
            JumpsLeft = 2,
            InvincibilityTicks = 15,
            RushTicks = 4,
            LastDirX = 1f,
            LastDirZ = 0f,
            WasAirborneDuringKnockback = true,
            HitstopTicks = 17,
            BurstCooldownTicks = 1234,
            BurstRecoveryTicks = 25,
        };

        // Act: FromState → Serialize → Deserialize → ToState
        var packet = CharacterStatePacket.FromState(original, tick: 999);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restoredPacket = CharacterStatePacket.Deserialize(buffer);
        var restored = restoredPacket.ToState();

        // Assert: every PvP-relevant field round-trips
        Assert.Equal(999u, restoredPacket.TickNumber);
        Assert.Equal(original.PX, restored.PX);
        Assert.Equal(original.PY, restored.PY);
        Assert.Equal(original.PZ, restored.PZ);
        Assert.Equal(original.VX, restored.VX);
        Assert.Equal(original.VY, restored.VY);
        Assert.Equal(original.VZ, restored.VZ);
        Assert.Equal(original.State, restored.State);
        Assert.Equal(original.StateTicks, restored.StateTicks);
        Assert.Equal(original.IsGrounded, restored.IsGrounded);
        Assert.Equal(original.AttackSlot, restored.AttackSlot);
        Assert.Equal(original.ComboStage, restored.ComboStage);
        Assert.Equal(original.AnimIndex, restored.AnimIndex);
        Assert.Equal(original.FacingYaw, restored.FacingYaw);
        Assert.Equal(original.MatchState, restored.MatchState);
        Assert.Equal(original.HitstunLevel, restored.HitstunLevel);
        Assert.Equal(original.AimPitch, restored.AimPitch);
        Assert.Equal(original.Deaths, restored.Deaths);
        Assert.Equal(original.DamagePercent, restored.DamagePercent);
        Assert.Equal(original.Cooldown0, restored.Cooldown0);
        Assert.Equal(original.Cooldown1, restored.Cooldown1);
        Assert.Equal(original.Cooldown2, restored.Cooldown2);
        Assert.Equal(original.Cooldown3, restored.Cooldown3);
        Assert.Equal(original.Cooldown4, restored.Cooldown4);
        Assert.Equal(original.Cooldown5, restored.Cooldown5);
        Assert.Equal(original.AirTimeTicks, restored.AirTimeTicks);
        Assert.Equal(original.DashDurationTicks, restored.DashDurationTicks);
        Assert.Equal(original.DashDirX, restored.DashDirX);
        Assert.Equal(original.DashDirZ, restored.DashDirZ);
        Assert.Equal(original.DashCooldownTicks, restored.DashCooldownTicks);
        Assert.Equal(original.AirDodgesLeft, restored.AirDodgesLeft);
        Assert.Equal(original.JumpsLeft, restored.JumpsLeft);
        Assert.Equal(original.InvincibilityTicks, restored.InvincibilityTicks);
        Assert.Equal(original.RushTicks, restored.RushTicks);
        Assert.Equal(original.LastDirX, restored.LastDirX);
        Assert.Equal(original.LastDirZ, restored.LastDirZ);
        Assert.Equal(original.WasAirborneDuringKnockback, restored.WasAirborneDuringKnockback);
        Assert.Equal(original.HitstopTicks, restored.HitstopTicks);
        Assert.Equal(original.BurstCooldownTicks, restored.BurstCooldownTicks);
        Assert.Equal(original.BurstRecoveryTicks, restored.BurstRecoveryTicks);
    }

    [Fact]
    public void Size_MatchesActualSerializedLayout()
    {
        // 109 bytes: the fixed state fields, eleven cooldown slots, and rollback resources.
        Assert.Equal(109, CharacterStatePacket.Size);

        // Prove it: serialize into an exactly-Size buffer must not throw
        var packet = CharacterStatePacket.FromState(new CharacterState { AimPitch = 1f, LastDirX = 2f });
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer); // throws if Size is too small
        var restored = CharacterStatePacket.Deserialize(buffer);
        Assert.Equal(1f, restored.AimPitch);
        Assert.Equal(2f, restored.LastDirX);
    }

    [Fact]
    public void Roundtrip_Cooldown6To10_And_JumpHeldTicks()
    {
        var original = new CharacterState
        {
            Cooldown6 = 111, Cooldown7 = 222, Cooldown8 = 333, Cooldown9 = 444, Cooldown10 = 555,
            JumpHeldTicks = 4,
        };
        var packet = CharacterStatePacket.FromState(original);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restored = CharacterStatePacket.Deserialize(buffer).ToState();

        Assert.Equal((ushort)111, restored.Cooldown6);
        Assert.Equal((ushort)222, restored.Cooldown7);
        Assert.Equal((ushort)333, restored.Cooldown8);
        Assert.Equal((ushort)444, restored.Cooldown9);
        Assert.Equal((ushort)555, restored.Cooldown10);
        Assert.Equal((byte)4, restored.JumpHeldTicks);
    }

    [Fact]
    public void ApplyTo_OverwritesOnlyWireFields_PreservesRest()
    {
        // Arrange: a CharacterState with non-wire fields set (AttackElapsedTicks and the
        // hitstop queued-launch payload are never on the wire — this proves ApplyTo
        // doesn't zero them, unlike ToState()).
        var target = new CharacterState
        {
            PX = 1f,
            AttackElapsedTicks = 500, // NOT carried by CharacterStatePacket — must survive
            AirTimeTicks = 999,       // IS carried — must be overwritten
            QueuedKBDirX = 3.5f,      // NOT carried (queued launch payload, ADR-0012) — must survive
            HitstopTicks = 0,         // IS carried — must be overwritten
        };
        var packet = CharacterStatePacket.FromState(new CharacterState { PX = 42f, AirTimeTicks = 7, HitstopTicks = 9 });

        // Act
        packet.ApplyTo(ref target);

        // Assert
        Assert.Equal(42f, target.PX);       // wire field overwritten
        Assert.Equal((ushort)7, target.AirTimeTicks); // wire field overwritten
        Assert.Equal((ushort)9, target.HitstopTicks); // wire field overwritten (ADR-0012)
        Assert.Equal((ushort)500, target.AttackElapsedTicks); // non-wire field preserved
        Assert.Equal(3.5f, target.QueuedKBDirX);             // non-wire field preserved
    }

    [Fact]
    public void RoundTrip_LockOn_Flag()
    {
        // LockOn (ADR-0018) rides the packet for the client lock indicator.
        var original = new CharacterState { LockOn = true };
        var packet = CharacterStatePacket.FromState(original);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restored = CharacterStatePacket.Deserialize(buffer).ToState();

        Assert.True(restored.LockOn);

        // And it must survive ApplyTo (LocalTrack patch path), not just ToState
        var target = new CharacterState();
        CharacterStatePacket.FromState(original).ApplyTo(ref target);
        Assert.True(target.LockOn);
    }

    [Fact]
    public void RoundTrip_AnimIndex_NonZero()
    {
        // AnimIndex was previously missing from Serialize/Deserialize (the original bug).
        // This test defends against regression: a non-zero AnimIndex must survive the wire.
        var state = new CharacterState { AnimIndex = 7 };
        var packet = CharacterStatePacket.FromState(state);
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);
        var restored = CharacterStatePacket.Deserialize(buffer).ToState();
        Assert.Equal((byte)7, restored.AnimIndex);
    }
}
