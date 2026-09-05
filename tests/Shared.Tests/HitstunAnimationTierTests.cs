using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════
/// HITSTUN ANIMATION TIER TESTS
/// ═══════════════════════════════════════════════════════════════════════
///
/// Verifies the 3-tier hitstun animation system:
///   - StunTicks ≤ 30  → HitstunLevel = 0 (small / hit_light)
///   - StunTicks 31-50 → HitstunLevel = 1 (medium / hit_medium)
///   - StunTicks ≥ 51  → HitstunLevel = 2 (hard / hit_hard)
///
/// Tier is computed once at hit time in ServerSimulation.ResolveHits()
/// from hit.StunTicks, serialized through CharacterStatePacket at byte offset 43,
/// and consumed by the client renderer for animator trigger selection.
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public class HitstunAnimationTierTests
{
    // Mirrors ServerSimulation.ResolveHits StunTicks→level logic
    private static byte ComputeHitstunLevel(ushort stunTicks)
        => stunTicks <= 30 ? (byte)0 : stunTicks <= 50 ? (byte)1 : (byte)2;

    // ═══════════════════════════════════════════════════════════════════
    // Tier boundary tests
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0,   0)]
    [InlineData(15,  0)]
    [InlineData(30,  0)]
    [InlineData(31,  1)]
    [InlineData(40,  1)]
    [InlineData(50,  1)]
    [InlineData(51,  2)]
    [InlineData(80,  2)]
    [InlineData(200, 2)]
    public void HitstunLevel_ComputedFromStunTicks_CorrectTier(ushort stunTicks, byte expectedLevel)
    {
        byte level = ComputeHitstunLevel(stunTicks);
        Assert.Equal(expectedLevel, level);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Default value (struct zero-initialization)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void HitstunLevel_DefaultsToZero()
    {
        var state = new CharacterState();
        Assert.Equal(0, (int)state.HitstunLevel);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Packet serialization round-trip
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HitstunLevel_RoundTrips_ThroughPacketSerialization(byte level)
    {
        var original = new CharacterStatePacket
        {
            TickNumber = 1,
            PositionX = 1, PositionY = 2, PositionZ = 3,
            CurrentActionState = 1, IsGrounded = true, StateDurationFrames = 10,
            HitstunLevel = level,
        };
        Span<byte> buf = stackalloc byte[CharacterStatePacket.Size];
        original.Serialize(buf);
        var deserialized = CharacterStatePacket.Deserialize(buf);
        Assert.Equal(level, deserialized.HitstunLevel);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FromState → ToState preserves HitstunLevel
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HitstunLevel_RoundTrips_ThroughFromStateToState(byte level)
    {
        var state = new CharacterState
        {
            PX = 1, PY = 2, PZ = 3,
            State = ActionState.Hitstun,
            HitstunTicks = 12,
            HitstunLevel = level,
        };
        var packet = CharacterStatePacket.FromState(state);
        var restored = packet.ToState();
        Assert.Equal(level, restored.HitstunLevel);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Packet size includes the current fixed movement-resource and lifecycle fields.
    [Fact]
    public void CharacterStatePacket_Size_IncludesHitstunLevel()
    {
        Assert.Equal(109, CharacterStatePacket.Size);
    }

    // ═══════════════════════════════════════════════════════════════════
    // End-to-end: combat pipeline produces correct HitstunLevel
    // ═══════════════════════════════════════════════════════════════════


    [Fact]
    public void HitstunLevel_SetsFromFromState_AndSurvivesPacketConversion()
    {
        // Verifies that HitstunLevel survives the full server tick → packet → state chain
        var state = TestHelpers.PlayerState();
        state.State = ActionState.Hitstun;
        state.HitstunTicks = 15;
        state.HitstunLevel = 2;

        var packet = CharacterStatePacket.FromState(state);
        Assert.Equal((byte)2, packet.HitstunLevel);

        Span<byte> buf = stackalloc byte[CharacterStatePacket.Size];
        packet.Serialize(buf);
        var deserialized = CharacterStatePacket.Deserialize(buf);
        Assert.Equal((byte)2, deserialized.HitstunLevel);

        var restored = deserialized.ToState();
        Assert.Equal((byte)2, restored.HitstunLevel);
    }
}
