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
            BuffRemainingTicks = 60,
            BuffActiveFlags = 0b_0011,
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
        Assert.Equal(original.BuffRemainingTicks, restored.BuffRemainingTicks);
        Assert.Equal(original.BuffActiveFlags, restored.BuffActiveFlags);
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
    }

    [Fact]
    public void Size_MatchesActualSerializedLayout()
    {
        // AimPitch (float at offset 44) ends at byte 47, Deaths at 48, then
        // DamagePercent (49-50) and six cooldowns (51-62) → 63 bytes.
        // Lock the constant: a silent Size change would break every packet on the wire.
        Assert.Equal(63, CharacterStatePacket.Size);

        // Prove it: serialize into an exactly-Size buffer must not throw
        var packet = CharacterStatePacket.FromState(new CharacterState { AimPitch = 1f });
        byte[] buffer = new byte[CharacterStatePacket.Size];
        packet.Serialize(buffer); // throws if Size is too small
        var restored = CharacterStatePacket.Deserialize(buffer);
        Assert.Equal(1f, restored.AimPitch);
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
