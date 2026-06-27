using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for Manki's LMB combo (MankiLmbCombo ServerAbility).
/// 3-hit melee chain with forward lunge, hitbox spawning, and chain windows.
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

        const int lungeTicks = 10; // lunge_duration param
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
    public void Stage1_ChainsToStage2_WhenInputDuringChainWindow()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        int chainTick = Stage1.DurationTicks - Stage1.ChainWindowTicks;

        for (int i = 0; i <= chainTick; i++)
        {
            var input = i == 0
                ? TestHelpers.Input(activeSlot: 1)
                : i == chainTick
                    ? TestHelpers.Input(activeSlot: 1)
                    : default;
            sim.Tick(new() { { 1, input } });
        }

        var after = sim.GetState(1);

        // Should be attacking still (stage 2), not idle
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal((byte)1, after.AttackSlot);
    }

    [Fact]
    public void Stage1_ChainsToStage2_AndLastsStage2Duration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        int chainTick = Stage1.DurationTicks - Stage1.ChainWindowTicks;

        // Tick 0: LMB
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Ticks 1..chainTick-1: default input
        for (int i = 1; i < chainTick; i++)
            sim.Tick(new() { { 1, default } });

        // Tick chainTick: LMB → chain to stage 2
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Let stage 2 expire + margin
        for (int i = 0; i < Stage2.DurationTicks + 10; i++)
            sim.Tick(new() { { 1, default } });

        var final = sim.GetState(1);
        Assert.Equal(ActionState.Idle, final.State);
    }

    // ── Stage 2 → 3 chain ──

    [Fact]
    public void Stage2_ChainsToStage3_WhenInputDuringChainWindow()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        int stage1ChainTick = Stage1.DurationTicks - Stage1.ChainWindowTicks;
        int stage2ChainTick = Stage2.DurationTicks - Stage2.ChainWindowTicks;

        // Tick 0: LMB → stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Ticks 1..stage1ChainTick-1: idle
        for (int i = 1; i < stage1ChainTick; i++)
            sim.Tick(new() { { 1, default } });

        // Tick stage1ChainTick: LMB → stage 2
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Ticks after chain, up to stage 2 chain window: idle
        for (int i = 0; i < stage2ChainTick - 1; i++)
            sim.Tick(new() { { 1, default } });

        // Tick at stage 2 chain window: LMB → stage 3
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        var afterChain = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, afterChain.State);
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

        int triggerTick = Stage1.HitboxEvents[0].TriggerTick; // 6

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

        int chainTick = Stage1.DurationTicks - Stage1.ChainWindowTicks;
        int stage2Trigger = Stage2.HitboxEvents[0].TriggerTick; // 8

        // Start stage 1 and chain to stage 2
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        for (int i = 1; i < chainTick; i++)
            sim.Tick(new() { { 1, default } });
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

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

    // ── Not chaining before chain window ──

    [Fact]
    public void Stage1_DoesNotChain_BeforeChainWindow()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press LMB to start stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Feed LMB early (before chain window opens at tick 42) — should NOT chain
        for (int i = 0; i < 10; i++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
            var mid = sim.GetState(1);
            Assert.Equal(ActionState.Attacking, mid.State);
        }

        // Let stage 1 fully expire without chain input in window
        for (int i = 0; i < Stage1.DurationTicks; i++)
            sim.Tick(new() { { 1, default } });

        var final = sim.GetState(1);
        Assert.Equal(ActionState.Idle, final.State);
    }

    // ── Input consumption ──

    [Fact]
    public void ChainInput_IsConsumed_AfterStageTransition()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        int chainTick = Stage1.DurationTicks - Stage1.ChainWindowTicks;

        // Start stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Default input until chain window
        for (int i = 1; i < chainTick; i++)
            sim.Tick(new() { { 1, default } });

        // Feed LMB at chain window
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // After chain, should still be in stage 2 (not idle)
        // If input wasn't consumed, ActivateAbility would start a new combo
        var afterChain = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, afterChain.State);
        Assert.Equal((byte)1, afterChain.AttackSlot);
    }

    // ── Helpers ──

    /// <summary>
    /// Chain from stage 1 → 2 → 3 by feeding LMB input at each chain window.
    /// Leaves sim at the start of stage 3.
    /// </summary>
    private static void ChainToStage3(ServerSimulation sim)
    {
        int stage1ChainTick = Stage1.DurationTicks - Stage1.ChainWindowTicks;
        int stage2ChainTick = Stage2.DurationTicks - Stage2.ChainWindowTicks;

        // Stage 1
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Chain to stage 2
        for (int i = 1; i < stage1ChainTick; i++)
            sim.Tick(new() { { 1, default } });
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        // Chain to stage 3
        for (int i = 0; i < stage2ChainTick - 1; i++)
            sim.Tick(new() { { 1, default } });
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
    }
}
