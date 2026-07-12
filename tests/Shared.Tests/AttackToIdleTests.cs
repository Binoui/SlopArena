using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests that every character's abilities properly transition back to idle
/// after their attack duration expires.
/// Covers: LmbCombo (ServerAbility), MankiAerosolFlame (RMB),
/// MankiRoundBomb (Q), data-driven (AirLMB), MankiBazooka (R),
/// MankiOverclock (F), and FightGuy's extended abilities.
/// </summary>
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
    //  MANKI AIR LMB — data-driven (no ServerAbility)
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
    //  MANKI RMB — MankiAerosolFlame
    // ════════════════════════════════════════════════

    [Fact]
    public void MankiRMB_Normal_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2),
            5 + MankiDef.RMB!.Stages[1].DurationTicks + 20);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    [Fact]
    public void MankiRMB_Charged_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiGroundPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // Hold RMB with IsAiming=true for 55 ticks (past charge_threshold=45)
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        TestHelpers.TickN(sim, holdInput, 55);

        // Release — charged attack lasts charged_duration (50) ticks
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        var after = TestHelpers.TickN(sim, releaseInput,
            MankiDef.RMB!.ChargedStages![0].DurationTicks + 30);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  MANKI AIR RMB — data-driven (no ServerAbility)
    // ════════════════════════════════════════════════

    [Fact]
    public void MankiAirRMB_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2),
            MankiDef.AirRMB!.Stages[0].DurationTicks + 10);

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
    //  MANKI E — data-driven explosive mine
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
    //  FIGHTGUY LMB — LmbCombo (StageChainAbility)
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyLMB_SingleStage_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        state.FacingYaw = 0;
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1),
            FightGuyDef.LMB!.Stages[0].DurationTicks + 10);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
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

        // max_flight_ticks=180 + post_impact_ticks=10 + margin
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
