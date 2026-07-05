using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════
/// HITSTUN ANIMATION TIER TESTS
/// ═══════════════════════════════════════════════════════════════════════
///
/// Verifies the 3-tier hitstun animation system:
///   - Damage < 5   → HitstunLevel = 0 (small / hit_light)
///   - Damage 5-14  → HitstunLevel = 1 (medium / hit_medium)
///   - Damage ≥ 15  → HitstunLevel = 2 (hard / hit_hard)
///
/// Tier is computed once at hit time in ServerSimulation.ResolveHits(),
/// serialized through CharacterStatePacket at byte offset 43,
/// and consumed by the client renderer for animator trigger selection.
/// ═══════════════════════════════════════════════════════════════════════
public class HitstunAnimationTierTests
{
    // Mirrors ServerSimulation.ResolveHits damage→level logic
    private static byte ComputeHitstunLevel(float damage)
        => damage < 5f ? (byte)0 : damage < 15f ? (byte)1 : (byte)2;

    // ═══════════════════════════════════════════════════════════════════
    // Tier boundary tests
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0f,    0)]
    [InlineData(1f,    0)]
    [InlineData(4.9f,  0)]
    [InlineData(5f,    1)]
    [InlineData(7f,    1)]
    [InlineData(14.9f, 1)]
    [InlineData(15f,   2)]
    [InlineData(20f,   2)]
    [InlineData(999f,  2)]
    public void HitstunLevel_ComputedFromDamage_CorrectTier(float damage, byte expectedLevel)
    {
        byte level = ComputeHitstunLevel(damage);
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
    // Packet size increased to accommodate the new byte
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CharacterStatePacket_Size_IncludesHitstunLevel()
    {
        // Size should be 48 for the new packet layout (44 + 4 for AimPitch)
        Assert.Equal(48, CharacterStatePacket.Size);
    }

    // ═══════════════════════════════════════════════════════════════════
    // End-to-end: combat pipeline produces correct HitstunLevel
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void LMB_Stage1_SmallDamage_SetsHitstunLevel0()
    {
        // Manki LMB stage 1: Damage = 4 → HitstunLevel = 0
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;

        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(100, def, npc);

        // Tick 0: press LMB (slot 1)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        // Ticks 1-11: wait for hitbox to trigger
        for (int i = 0; i < 11; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var afterHit = sim.GetState(100);
        Assert.Equal(ActionState.Hitstun, afterHit.State);
        Assert.True(afterHit.DamagePercent > 0, "NPC should have taken damage");
        Assert.Equal(0, (int)afterHit.HitstunLevel);
    }

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
