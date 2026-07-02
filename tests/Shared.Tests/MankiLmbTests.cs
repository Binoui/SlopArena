using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for Manki's LMB combo (LmbCombo via StageChainAbility).
/// 3-hit melee chain with forward lunge, bone hitboxes, and input buffering.
/// Chain input at ANY point during a stage is buffered; chain fires on stage end.
/// </summary>
public class MankiLmbTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPy = TestHelpers.MankiGroundPY;

    private static readonly AttackStage Stage1 = Def.LMB!.Stages[0];
    private static readonly AttackStage Stage2 = Def.LMB!.Stages[1];
    private static readonly AttackStage Stage3 = Def.LMB!.Stages[2];

    // ── Activation ──

    [Fact]
    public void Activate_SetsAttackingState()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);

        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal((byte)1, after.AttackSlot);
    }

    [Fact]
    public void Activate_SetsAnimLock_ToStage1Duration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);

        // AnimLockTicks is decremented by the sim after ability Tick runs
        // Stage1.DurationTicks = 14
        Assert.Equal(Stage1.DurationTicks - 1, after.AnimLockTicks);
    }

    // ── Lunge ──

    [Fact]
    public void Activate_AppliesForwardLunge()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);

        // Stage 1 LungeForce = 8f, applied in facing direction (Z+ by default)
        Assert.True(after.VZ > 0f, "Expected forward lunge velocity");
    }

    [Fact]
    public void Lunge_DecaysAfterLungeDuration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        const int lungeTicks = 6; // lunge_duration param
        const int extraTicks = 20;
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), lungeTicks + extraTicks);

        // After lunge duration + sim friction, VZ should be near 0
        Assert.True(after.VZ < 1f, $"Expected lunge to decay, got VZ={after.VZ}");
    }

    // ── Stage 1 duration expiry ──

    [Fact]
    public void Stage1_ExpiresToIdle_WithoutChainInput()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), Stage1.DurationTicks + 5);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ── Stage 1 → 2 chain ──

    [Fact]
    public void Stage1_ChainsToStage2_WhenInputBuffered()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tick 0: LMB to start stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Tick 1: LMB again — gets buffered (any point during stage works)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Run to stage 1 end (12 more ticks, total 14)
        for (int i = 2; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });

        var after = sim.GetState(1);

        // Should be attacking still (stage 2), not idle
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal((byte)1, after.AttackSlot);
        Assert.Equal((byte)1, after.ComboStage);
    }

    [Fact]
    public void Stage1_ChainsToStage2_AndLastsStage2Duration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tick 0: LMB to start stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Tick 1: buffer chain input
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Run to stage 1 end — chains to stage 2
        for (int i = 2; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });

        // Let stage 2 expire + margin
        for (int i = 0; i < Stage2.DurationTicks + 10; i++)
            sim.Tick(new() { { 1, default } });

        var final = sim.GetState(1);
        Assert.Equal(ActionState.Idle, final.State);
    }

    // ── Stage 2 → 3 chain ──

    [Fact]
    public void Stage2_ChainsToStage3_WhenInputBuffered()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Chain through stage 1 → 2
        ChainToStage2(sim);

        // Buffer chain input on tick 1 of stage 2
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Run to stage 2 end (15 more ticks, total 16) — chains to stage 3
        for (int i = 1; i < Stage2.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });

        var afterChain = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, afterChain.State);
        Assert.Equal((byte)2, afterChain.ComboStage);
    }

    // ── Stage 3 is final ──

    [Fact]
    public void Stage3_DoesNotChain_AndExpiresToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Chain through all 3 stages
        ChainToStage3(sim);

        // Feed DEFAULT input during stage 3 (no LMB) — stage should expire naturally
        for (int i = 0; i < Stage3.DurationTicks + 10; i++)
            sim.Tick(new() { { 1, default } });

        var final = sim.GetState(1);
        Assert.Equal(ActionState.Idle, final.State);
        Assert.Equal((byte)0, final.AttackSlot);
    }

    // ── Hitbox spawning ──

    [Fact]
    public void Stage1_SpawnsHitbox_AtTriggerTick()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press LMB on tick 0 — ability OnStart + Tick run, _stageTicks becomes 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        int triggerTick = Stage1.HitboxEvents[0].TriggerTick; // 5

        // Run ticks up to triggerTick-1: no hitbox
        for (int i = 1; i < triggerTick; i++)
        {
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            sim.Tick(new() { { 1, default } });
        }

        // At trigger tick, hitbox should spawn
        sim.Tick(new() { { 1, default } });
        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
    }

    [Fact]
    public void Stage2_SpawnsHitbox_WhenChained()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        int stage2Trigger = Stage2.HitboxEvents[0].TriggerTick; // 6

        // Start stage 1 and chain to stage 2
        ChainToStage2(sim);

        // Run ticks up to triggerTick-1: no hitbox
        for (int i = 1; i < stage2Trigger; i++)
        {
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            sim.Tick(new() { { 1, default } });
        }

        // At trigger tick, hitbox should spawn
        sim.Tick(new() { { 1, default } });
        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
    }

    // ── Early input buffering ──

    [Fact]
    public void Stage1_DoesNotChain_BeforeStageEnd()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press LMB to start stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Feed LMB early (tick 3) — should buffer but NOT chain immediately
        for (int i = 0; i < 3; i++)
            sim.Tick(new() { { 1, default } });
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // At tick 5, should still be stage 1 (chain hasn't fired yet)
        var mid = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, mid.State);
        Assert.Equal((byte)0, mid.ComboStage);

        // Run to stage 1 end — chain should fire
        for (int i = 5; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });

        var afterChain = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, afterChain.State);
        Assert.Equal((byte)1, afterChain.ComboStage);
    }

    [Fact]
    public void LMB_EarlyInput_IsBuffered_NotImmediate()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tick 0: LMB to activate
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Ticks 1-3: spam LMB (should all be buffered, not immediate chains)
        for (int i = 1; i <= 3; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Tick 4: should still be stage 0 (no immediate chain despite early input)
        var mid = sim.GetState(1);
        Assert.Equal((byte)0, mid.ComboStage);

        // Run to stage 1 end
        for (int i = 4; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });

        // Should now be stage 2 (chain fired on stage end)
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal((byte)1, after.ComboStage);
    }

    // ── Input consumption ──

    [Fact]
    public void ChainInput_IsConsumed_AfterStageTransition()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Start stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Buffer chain input
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Run to stage 1 end — chain fires
        for (int i = 2; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });

        // After chain, should still be in stage 2 (not idle)
        // If input wasn't consumed, ActivateAbility would start a new combo
        var afterChain = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, afterChain.State);
        Assert.Equal((byte)1, afterChain.AttackSlot);
    }


    [Fact]
    public void ComboStage_AdvancesThroughFullChain()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Stage 1 starts: ComboStage = 0
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        var s1 = sim.GetState(1);
        Assert.Equal(0, s1.ComboStage);

        // Buffer chain for stage 2
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Run to stage 1 end — chains to stage 2: ComboStage = 1
        for (int i = 2; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });
        var s2 = sim.GetState(1);
        Assert.Equal(1, s2.ComboStage);

        // Buffer chain for stage 3
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Run to stage 2 end — chains to stage 3: ComboStage = 2
        for (int i = 1; i < Stage2.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });
        var s3 = sim.GetState(1);
        Assert.Equal(2, s3.ComboStage);

        // Stage 3 finishes naturally: ComboStage resets to 0
        for (int i = 0; i < Stage3.DurationTicks + 10; i++)
            sim.Tick(new() { { 1, default } });
        var done = sim.GetState(1);
        Assert.Equal(ActionState.Idle, done.State);
        Assert.Equal(0, done.ComboStage);
    }
    // ── Helpers ──

    /// <summary>
    /// Chain from stage 1 → 2. Leaves sim at the start of stage 2.
    /// </summary>
    private static void ChainToStage2(ServerSimulation sim)
    {
        // Stage 1 start
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Buffer chain input early in stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Run to stage 1 end (12 more ticks, total 14) — chains to stage 2
        for (int i = 2; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });
    }

    /// <summary>
    /// Chain from stage 1 → 2 → 3. Leaves sim at the start of stage 3.
    /// </summary>
    private static void ChainToStage3(ServerSimulation sim)
    {
        ChainToStage2(sim);

        // Buffer chain input early in stage 2
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Run to stage 2 end (15 more ticks, total 16) — chains to stage 3
        for (int i = 1; i < Stage2.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });
    }
}
