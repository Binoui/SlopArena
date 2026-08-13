using System;
using Xunit;

namespace SlopArena.Shared.Tests;

public class FightGuyKitRegressionTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static float Gpy => FightGuyGpy;

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
    public void AirLMB_RisingKick_HitsAirborneNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy Air LMB",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, 1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY + 2f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 12,   // stage 1, second hitbox active (trigger=13, dur=5)
            TotalTicks = 80,
        });
    }

    // ── Normal tier 1-4 + air variants (melee frame-data pass, 2026-08-12) ──

    [Fact]
    public void Slot1_LowKick_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy Slot1 Low Kick",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.0f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 3,    // jab profile: trigger=2, dur=3 → active 2-4
            TotalTicks = 40,
        });
    }

    [Fact]
    public void Slot2_Roundhouse_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy Slot2 Roundhouse",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot2),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.2f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 9,    // ftilt profile: trigger=8, dur=4 → active 8-11
            TotalTicks = 50,
        });
    }

    [Fact]
    public void Slot3_DoubleUppercut_FirstHitConnects()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy Slot3 Double Uppercut",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot3),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 0.8f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 13,   // low starter: trigger=12, dur=4 → active 12-15 (uppercut 20+ reaches jumpers)
            TotalTicks = 60,
        });
    }

    [Fact]
    public void Slot4_TornadoKick_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy Slot4 Tornado Kick",
            Def = Def,
            Setup = () => TestHelpers.PlayerState() with { PY = Gpy },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot4),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = TestHelpers.CombatGroundPY },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 19,   // dsmash profile: trigger=18, dur=10 → ring active 18-27
            TotalTicks = 70,
        });
    }

    [Fact]
    public void AirSlot1_DoublePunch_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy AirSlot1 Double Punch",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.0f, PY = 3f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 5,    // uair profile: hit 1 trigger=4, dur=4 → active 4-7
            TotalTicks = 60,
        });
    }

    [Fact]
    public void AirSlot2_FloatingKick_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy AirSlot2 Floating Kick",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot2),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.0f, PY = 3f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 10,   // nair profile: trigger=7, dur=12 → long window 7-18
            TotalTicks = 70,
        });
    }

    [Fact]
    public void AirSlot3_HighKick_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy AirSlot3 High Kick",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2.5f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot3),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.0f, PY = 3.5f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 16,   // trigger=15, dur=5 → active 15-19, high OffY 1.3
            TotalTicks = 70,
        });
    }

    [Fact]
    public void AirSlot4_AirTornado_HitsNpc()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "FightGuy AirSlot4 Air Tornado",
            Def = Def,
            Setup = () => TestHelpers.PlayerState()
                with { PX = 0, PZ = 0, PY = 2.5f, IsGrounded = false, JumpsLeft = 0 },
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot4),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState()
                with { PX = 0, PZ = 1.5f, PY = 3.5f, IsGrounded = false },
            NpcAssert = _ => { },
            NpcDef = TestHelpers.CombatDef,
            SnapshotTick = 19,   // same profile as ground tornado: trigger=18, dur=10
            TotalTicks = 70,
        });
    }
}
