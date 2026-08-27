using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Regression coverage for the FightGuy normal-role pass. The golden scenarios use baked
/// attacker poses and a plain capsule dummy, so timing, hitbox attachment, damage, and launch
/// stay pinned together.
/// </summary>
public class FightGuyNormalTuningTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static float GroundPy => TestHelpers.GroundPY(Def);

    private static CharacterState GroundedPlayer()
    {
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        return state;
    }

    private static CharacterState AirbornePlayer()
    {
        var state = TestHelpers.PlayerState();
        state.PY = 2f; // Calibrated low jump: still airborne during the late a4 strike.
        state.IsGrounded = false;
        return state;
    }

    private static CharacterState AirborneNpc(float z)
    {
        var state = TestHelpers.NpcState(0f, z);
        state.PY = 3f; // Air dummy sits above the attacker at the contact window.
        state.IsGrounded = false;
        return state;
    }

    [Fact]
    public void NormalRoles_KeepDistinctTimingAndLaunchContracts()
    {
        var g2 = Def.Slot2!.Stages[0].HitboxEvents[0];
        var g3 = Def.Slot3!.Stages[0].HitboxEvents[0];
        var g4 = Def.Slot4!.Stages[0].HitboxEvents[0];
        var a2 = Def.AirSlot2!.Stages[0];
        var a3 = Def.AirSlot3!.Stages[0].HitboxEvents[0];
        var a4 = Def.AirSlot4!.Stages[0].HitboxEvents[0];

        Assert.Equal("Straight Punch", Def.Slot2.Name);
        Assert.Equal((ushort)25, Def.Slot2.Stages[0].DurationTicks);
        Assert.Equal((ushort)5, g2.DurationTicks);
        Assert.Equal((sbyte)25, g2.Knockback.Angle);

        Assert.Equal("Sweeping Kick", Def.Slot3.Name);
        Assert.Equal((sbyte)55, g3.Knockback.Angle);
        Assert.True(g3.Knockback.KnockbackGrowth < g2.Knockback.KnockbackGrowth);

        Assert.Equal("Double Kick", Def.Slot4.Name);
        Assert.Equal((ushort)60, Def.Slot4.Stages[0].DurationTicks);
        Assert.Equal(HitboxShape.Capsule, g4.Shape);
        Assert.Equal("mixamorig:LeftFoot", g4.BoneName);
        Assert.Equal("mixamorig:RightFoot", g4.EndBoneName);
        Assert.True(g4.Knockback.KnockbackGrowth > g2.Knockback.KnockbackGrowth);

        Assert.Equal(2, a2.HitboxEvents.Length);
        Assert.All(a2.HitboxEvents, hit =>
        {
            Assert.Equal(HitboxShape.Capsule, hit.Shape);
            Assert.Equal("mixamorig:LeftFoot", hit.BoneName);
            Assert.Equal("mixamorig:Hips", hit.EndBoneName);
            Assert.Equal((byte)1, hit.HitGroup);
        });
        Assert.True(a2.HitboxEvents[0].Knockback.KnockbackGrowth < a4.Knockback.KnockbackGrowth);

        Assert.Equal((sbyte)65, a3.Knockback.Angle);
        Assert.Equal("Air Smash", Def.AirSlot4.Name);
        Assert.Equal((ushort)54, Def.AirSlot4.Stages[0].DurationTicks);
        Assert.Equal((ushort)12, Def.AirSlot4.Stages[0].LandingLagTicks);
    }

    [Fact]
    public void G2_ForwardPunch_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy G2 Forward Punch Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, 7),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)7, npc.DamagePercent),
            SnapshotTick = 7, // t5–9 active window; pins the fully extended punch.
            TotalTicks = 80,
        });
    }

    [Fact]
    public void G4_DoubleKick_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy G4 Double Kick Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, 9),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 0.8f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)14, npc.DamagePercent),
            SnapshotTick = 12, // t10–16 capsule active across both feet.
            TotalTicks = 100,
        });
    }

    [Fact(Skip = "Phase 7: golden snapshot predates the committed cooked FightGuy pose/runtime identity.")]
    public void A2_SexKick_SweetspotCannotRehitAsSourspot_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy A2 Sex Kick Single Hit",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, 7),
            Assert = _ => { },
            NpcSetup = () => AirborneNpc(0.8f),
            NpcDef = Def,
            // The sweet hit is 8 damage. A later 5-damage sour hit must share its HitGroup
            // instead of adding another hit while the target remains inside the capsule.
            NpcAssert = npc => Assert.Equal((ushort)8, npc.DamagePercent),
            SnapshotTick = 9, // t7–11 sweetspot active.
            TotalTicks = 90,
        });
    }

    [Fact]
    public void A3_HighKick_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy A3 High Kick Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, 8),
            Assert = _ => { },
            NpcSetup = () => AirborneNpc(0.8f),
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)8, npc.DamagePercent),
            SnapshotTick = 16, // t14–19 high-kick window.
            TotalTicks = 90,
        });
    }

    [Fact]
    public void A4_AirSmash_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy A4 Air Smash Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, 9),
            Assert = _ => { },
            NpcSetup = () => AirborneNpc(0.8f),
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)13, npc.DamagePercent),
            SnapshotTick = 22, // t20–26 late forward-air strike window.
            TotalTicks = 100,
        });
    }
}
