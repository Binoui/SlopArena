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
    // Packet size increased to accommodate the new byte
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CharacterStatePacket_Size_IncludesHitstunLevel()
    {
        // Size should be 110 for the current packet layout (Deaths #37, damage + cooldowns #38,
        // D10 movement-resource fields for PredictedTrack — ADR-0011, hitstop — ADR-0012,
        // burst cooldown/recovery — ADR-0014, cooldowns 6-10 + JumpHeldTicks — ADR-0016,
        // LockOn — ADR-0018; DirHoldTicks/IsSprinting dropped — ADR-0020)
        Assert.Equal(110, CharacterStatePacket.Size);
    }

    // ═══════════════════════════════════════════════════════════════════
    // End-to-end: combat pipeline produces correct HitstunLevel
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void LMB_Stage1_BandedStun_SetsHitstunLevel0()
    {
        // Manki LMB stage 1: StunTicks = 20 (ADR-0015 band) → HitstunLevel = 0 (light)
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
        // Hitstop (ADR-0012): the hit resolves at tick 11, freezing the victim for
        // 1 + 1.5·4 = 7 ticks before the launch. Damage + tier apply at connect; the
        // Hitstun STATE begins at freeze expiry (tick 18).
        Assert.True(afterHit.DamagePercent > 0, "NPC should have taken damage");
        Assert.Equal(0, (int)afterHit.HitstunLevel);
        Assert.Equal((ushort)7, afterHit.HitstopTicks);
        Assert.Equal(ActionState.Idle, afterHit.State); // frozen, not yet launched

        for (int i = 0; i < 7; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var afterLaunch = sim.GetState(100);
        Assert.Equal(ActionState.Hitstun, afterLaunch.State);
        Assert.Equal((ushort)1, afterLaunch.HitstunTicks);
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
