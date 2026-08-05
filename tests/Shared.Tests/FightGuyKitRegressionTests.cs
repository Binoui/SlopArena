using System;
using Xunit;

namespace SlopArena.Shared.Tests;

public class FightGuyKitRegressionTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static float Gpy => FightGuyGpy;

    [Fact]
    public void LMB_FullCombo_ChainsThroughAllStages()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy LMB Full Combo",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence()
                .Press(0, 1).Press(10, 1).Press(55, 1).Press(100, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 10,   // stage 1 hitbox active (trigger=7, dur=6)
            TotalTicks = 250,
        });
    }

    [Fact]
    public void LMB_Stage1_HitsNpcFor4Damage()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy LMB Hit Confirm",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 12,   // after hit connects
            TotalTicks = 80,
        });
    }

    [Fact]
    public void AirLMB_RisingKickThenSpike_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy Air LMB Combo",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 1).Press(1, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 12,   // stage 1 active (trigger=6, window at 18)
            TotalTicks = 80,
        });
    }

    [Fact]
    public void RMB_UnchargedUppercut_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy RMB Uncharged",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence()
                .Set(0, new InputState { ActiveSlot = 2, IsAiming = true })
                .Set(10, default),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.2f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 50,   // attack phase, hitboxes at 5/10/15
            TotalTicks = 80,
        });
    }

    [Fact]
    public void AirRMB_Helicopter_SpikesNpcDownward()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy Air RMB Helicopter",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 1.5f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 2),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.2f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 16,   // tap hitbox active (release ~tick5, trigger=6, dur=16 → 11-26)
            TotalTicks = 60,
        });
    }
}
