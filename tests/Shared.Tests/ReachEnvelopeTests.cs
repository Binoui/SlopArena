using System;
using Xunit;

namespace SlopArena.Shared.Tests;
using SlopArena.Shared.AI;

/// <summary>
/// Issue #148 — the deterministic threat-zone math both the bot policy and the whiff-spot
/// normalization build on: <see cref="HeuristicBotPolicy.ForwardReach"/> (hitbox reach from the
/// spec — the engage distance, NOT the auto-dash AttackRange) and <see cref="FacingMath"/>
/// (world delta → facing frame).
/// </summary>
public class ReachEnvelopeTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;

    [Fact]
    public void ForwardReach_G1LowKick_MatchesHitboxExtent()
    {
        // g1 Low Kick: single hitbox OffZ=0.21, Radius=0.35, no lunge → reach = 0.56.
        float reach = HeuristicBotPolicy.ForwardReach(Def, AbilitySlots.Slot1, airborne: false);
        TestHelpers.AssertNear(0.56f, reach, 0.001f);
    }

    [Fact]
    public void ForwardReach_G2Roundhouse_MatchesHitboxExtent()
    {
        // g2 Roundhouse: OffZ=0.21, Radius=0.4 → reach = 0.61.
        float reach = HeuristicBotPolicy.ForwardReach(Def, AbilitySlots.Slot2, airborne: false);
        TestHelpers.AssertNear(0.61f, reach, 0.001f);
    }

    [Fact]
    public void ForwardReach_A3HighKick_UsesForwardAxisNotSideways()
    {
        // a3 High Kick is a SIDE kick: OffX=0.43, OffZ=-0.16, Radius=0.4. The forward reach is
        // OffZ+Radius = -0.16+0.4 = 0.24 — NOT hypot(0.43,0.16)+0.4 = 0.859 (sideways extent).
        // This regression guards the over-pick that made the bot whiff a3 97% of the time.
        float reach = HeuristicBotPolicy.ForwardReach(Def, AbilitySlots.Slot3, airborne: true);
        TestHelpers.AssertNear(0.24f, reach, 0.001f);
    }

    [Fact]
    public void ForwardReach_A4AirTornado_IsNotTheSideHitbox()
    {
        // a4 has one sideways/behind hitbox (OffX=-0.17, OffZ=-0.09, r=0.4) that must NOT inflate
        // the reach: the forward extent from the OffZ=0 hitboxes is 0.4, not hypot+0.4 = 0.592.
        float reach = HeuristicBotPolicy.ForwardReach(Def, AbilitySlots.Slot4, airborne: true);
        TestHelpers.AssertNear(0.40f, reach, 0.001f);
    }

    [Fact]
    public void ForwardReach_IsNotTheAuthoredAttackRange()
    {
        // The policy's reach is the ACTUAL hitbox extent, far smaller than the authored
        // AttackRange (the auto-dash engage distance). If this ever inverts, the bot attacks
        // from beyond where its hitbox connects and whiffs everything.
        float reach = HeuristicBotPolicy.ForwardReach(Def, AbilitySlots.Slot1, airborne: false);
        float attackRange = Def.GetSlotAbility(AbilitySlots.Slot1 - 1, false)!.Stages[0].AttackRange;
        Assert.True(reach < attackRange,
            $"ForwardReach {reach:F2} must be below the auto-dash AttackRange {attackRange:F2}");
    }

    [Fact]
    public void ForwardReach_DataLessSlot_ReturnsZero()
    {
        // Slot5 (byte 10) has no kit data → zero reach (the bot must skip it).
        float reach = HeuristicBotPolicy.ForwardReach(Def, AbilitySlots.Slot5, airborne: false);
        Assert.Equal(0f, reach);
    }

    [Fact]
    public void FacingFrame_ForwardDeltaIsPureForward()
    {
        // Attacker facing +Z (yaw 0): a world delta straight ahead maps to forward-only.
        var (side, fwd) = FacingMath.ToFacingFrame(0f, 3f, facingYaw: 0f);
        TestHelpers.AssertNear(0f, side, 0.0001f);
        TestHelpers.AssertNear(3f, fwd, 0.0001f);
    }

    [Fact]
    public void FacingFrame_RotatedByQuarterTurn_SwapsAxes()
    {
        // Facing +X (yaw 90°): a delta along world +Z becomes the character's right side.
        float yaw = MathF.PI / 2f;
        var (side, fwd) = FacingMath.ToFacingFrame(0f, 3f, yaw);
        TestHelpers.AssertNear(3f, side, 0.0001f);
        TestHelpers.AssertNear(0f, fwd, 0.0001f);
    }

    [Fact]
    public void FacingFrame_RoundTrip_RecoversWorldDelta()
    {
        // Inverse of the sim's hitbox offset rotation: the local +X (side) basis is world
        // (cos, sin) and local +Z (forward) is world (−sin, cos), per the sim's rotate-offsets
        // hx = OffX·cos − OffZ·sin, hz = OffX·sin + OffZ·cos. Reconstructing the world delta
        // from (side, fwd) must reproduce the original.
        float yaw = 0.7f;
        float dx = 2f, dz = -1.5f;
        var (side, fwd) = FacingMath.ToFacingFrame(dx, dz, yaw);
        float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
        float wx = side * cos - fwd * sin;
        float wz = side * sin + fwd * cos;
        TestHelpers.AssertNear(dx, wx, 0.0001f);
        TestHelpers.AssertNear(dz, wz, 0.0001f);
    }
}
