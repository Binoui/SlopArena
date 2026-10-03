using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Real baked-pose normal contacts and attack completion, independent of tuning snapshots.
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
    public void G2_ForwardPunch_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "FightGuy G2 Forward Punch Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, 7),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot2, false), (float)npc.DamagePercent),
            TotalTicks = 80,
        });
    }

    [Fact]
    public void G4_DoubleKick_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "FightGuy G4 Double Kick Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, 9),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => TestHelpers.NpcState(0f, 0.8f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot4, false), (float)npc.DamagePercent),
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
    public void A3_HighKick_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "FightGuy A3 High Kick Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, 8),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => AirborneNpc(0.8f),
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot3, true), (float)npc.DamagePercent),
            TotalTicks = 90,
        });
    }

    [Fact]
    public void A4_AirSmash_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "FightGuy A4 Air Smash Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, 9),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => AirborneNpc(0.8f),
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot4, true), (float)npc.DamagePercent),
            TotalTicks = 100,
        });
    }
    private static float NormalDamage(byte slot, bool airborne)
    {
        var damage = Def.GetCookedSlotAbility(slot, airborne)!.Timeline.Stages
            .SelectMany(stage => stage.Operations).OfType<CookedSpawnHitboxOperation>()
            .Sum(operation => operation.Hitbox.Damage);
        Assert.True(damage > 0f, "Hit-confirm scenarios require a damaging authored normal.");
        return damage;
    }
}
