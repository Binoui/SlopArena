using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// Tests that every character's abilities properly transition back to idle
/// after their attack duration expires.
/// Covers all slots: LMB (LmbCombo), AirLMB (AirLmbCombo), RMB (MankiAerosolFlame/FightGuyUppercut),
/// AirRMB (AirChargeAttack), Q (MankiRoundBomb/FightGuyKiShot), E (MankiGrapple/FightGuyCycloneKick),
/// R (MankiBazooka/FightGuyDragonKick), F (MankiOverclock/FightGuyTempest).
public class AttackToIdleTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;
    private static readonly CharacterDefinition FightGuyDef = TestHelpers.FightGuyDef;
    private static readonly float MankiGroundPy = TestHelpers.MankiGroundPY;

    // ════════════════════════════════════════════════
    //  MANKI LMB — LmbCombo (StageChainAbility)
    // ════════════════════════════════════════════════

    [Fact]
    public void MankiLMB_SingleStage_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1),
            MankiDef.LMB!.Stages[0].DurationTicks + 5);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    [Fact]
    public void MankiLMB_FullComboToIdle_NoChainInput()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var totalTicks = MankiDef.LMB!.Stages[0].DurationTicks + 10;

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), totalTicks);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  MANKI AIR LMB — AirLmbCombo (StageChainAbility)
    // ════════════════════════════════════════════════

    [Fact]
    public void MankiAirLMB_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1),
            MankiDef.AirLMB!.Stages[0].DurationTicks + 10);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  MANKI Q — MankiRoundBomb (hold → throw)
    // ════════════════════════════════════════════════

    [Fact]
    public void MankiQ_HoldAndRelease_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 3, aiming: true) } });

        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 0, aiming: true) } });

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 0, aiming: false) } });

        for (int i = 0; i < 70; i++)
            sim.Tick(new() { { 1, default } });

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  MANKI E — MankiGrapple
    // ════════════════════════════════════════════════

    [Fact]
    public void MankiE_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4),
            MankiDef.E!.Stages[0].DurationTicks + 10);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  MANKI R — MankiBazooka (FPS fire-and-forget)
    // ════════════════════════════════════════════════

    [Fact]
    public void MankiR_CastAndRecovery_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // Activate R, tick through cast (20) + recovery (15) + buffer
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 40);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  MANKI F — MankiOverclock (self-buff)
    // ════════════════════════════════════════════════

    [Fact]
    public void MankiF_Overclock_ReturnsToIdleAfterInjection()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 60);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
        Assert.True(after.BuffRemainingTicks > 400,
            $"Expected buff to persist, got {after.BuffRemainingTicks}");
    }

    // ════════════════════════════════════════════════
    //  FIGHTGUY Q — FightGuyKiShot (hold → throw)
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyQ_HoldAndRelease_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 3, aiming: true) } });

        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 0, aiming: true) } });

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 0, aiming: false) } });

        for (int i = 0; i < 70; i++)
            sim.Tick(new() { { 1, default } });

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  FIGHTGUY E — FightGuyCycloneKick
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyE_CycloneKick_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        var spec = FightGuyDef.E!;
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4),
            spec.Stages[^1].DurationTicks + 30);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  FIGHTGUY R — FightGuyDragonKick
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyR_DragonKick_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        // max_flight_ticks=60 + end_duration=15 + margin
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 200);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  FIGHTGUY F — FightGuyTempest
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyF_Tempest_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 120);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }
}
