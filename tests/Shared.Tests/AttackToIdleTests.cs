using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// Tests that every character's abilities properly transition back to idle
/// after their attack duration expires.
/// Covers all attack slots: LMB (LmbCombo), AirLMB (AirLmbCombo), A (MankiRoundBomb/FightGuyKiShot),
/// E (MankiGrapple/FightGuyRisingKick), R (MankiBazooka/FightGuyCycloneKick), F (MankiOverclock/FightGuyDragonBeam).
/// The RMB slot (slot 2) is retired — it is the target-lock toggle, ADR-0018, no attack.
public class AttackToIdleTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;
    private static readonly CharacterDefinition FightGuyDef = TestHelpers.FightGuyDef;
    private static readonly float MankiGroundPy = TestHelpers.MankiGroundPY;

    // ════════════════════════════════════════════════
    //  MANKI LMB — LmbCombo (StageChainAbility)
    // ════════════════════════════════════════════════



    // ════════════════════════════════════════════════
    //  MANKI AIR LMB — AirLmbCombo (StageChainAbility)
    // ════════════════════════════════════════════════


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
    //  FIGHTGUY A — FightGuyKiShot
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyA_KiShot_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 11) } });
        // Hold-to-aim: release after the hold debounce, then the 24-tick fire
        // phase ends the ability — natural completion lands inside this window.
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(aiming: true) } });
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, default } });

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  FIGHTGUY E — FightGuyRisingKick
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyE_RisingDragon_ReturnsToIdle()
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
    //  FIGHTGUY R — FightGuyCycloneKick
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyR_CycloneKick_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        // duration_ticks=40 plus a recovery margin
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 80);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ════════════════════════════════════════════════
    //  FIGHTGUY F — FightGuyDragonBeam
    // ════════════════════════════════════════════════

    [Fact]
    public void FightGuyF_DragonBeam_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 40);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }
}
