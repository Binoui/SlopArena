using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for Manki's LMB single move (LmbCombo via StageChainAbility, issue #115).
/// One press = one move: lunge, hitbox trigger, duration, then Idle. No chains —
/// repeat presses during the move do nothing until it ends.
/// Momentum is preserved: the lunge coasts through the attack (no ground friction
/// while Attacking, nothing zeroes it) and decays only in Idle.
/// </summary>
public class MankiLmbTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPy = TestHelpers.MankiGroundPY;

    private static readonly AttackStage Stage = Def.LMB!.Stages[0];

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
    public void Activate_SetsAnimLock_ToStageDuration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);

        // AnimLockTicks is decremented by the sim after ability Tick runs
        Assert.Equal(Stage.DurationTicks - 1, after.AnimLockTicks);
    }

    [Fact]
    public void Activate_AppliesForwardLunge()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);

        // Stage LungeForce applied in facing direction (Z+ by default)
        Assert.True(after.VZ > 0f, "Expected forward lunge velocity");
    }

    // ── Momentum (issue #115) ──

    [Fact]
    public void Lunge_PersistsThroughAttack_NoFrictionWhileAttacking()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        const int lungeTicks = 10;   // lunge_duration param
        const int extraTicks = 20;   // still well inside the 40-tick move
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), lungeTicks + extraTicks);

        // Attacking applies no ground friction and nothing zeroes the lunge velocity —
        // it coasts through the whole move (ADR-0015 momentum-preserve).
        Assert.True(after.VZ > 6f, $"Expected lunge to persist mid-attack, got VZ={after.VZ}");
    }

    [Fact]
    public void Lunge_DecaysAfterMoveEnds_WhenIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), Stage.DurationTicks + 60);

        // Move ended; Idle ground friction has decayed the lunge drift.
        Assert.True(after.VZ < 2f, $"Expected drift to decay after move, got VZ={after.VZ}");
    }

    // ── Duration ──

    [Fact]
    public void Move_ExpiresToIdle_AfterDuration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), Stage.DurationTicks + 5);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
        Assert.Equal((byte)0, after.ComboStage);
    }

    // ── No chains (issue #115) ──

    [Fact]
    public void RepeatPress_DuringMove_DoesNotChain()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tick 0: LMB starts the move
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        // Repeated presses mid-move — no chain, no stage advance
        for (int i = 1; i <= 5; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        var mid = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, mid.State);
        Assert.Equal((byte)0, mid.ComboStage); // still the single move

        // Run out the full duration — ends Idle, no second move queued
        for (int i = 6; i < Stage.DurationTicks + 10; i++)
            sim.Tick(new() { { 1, default } });
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    // ── Hitbox spawning ──

    [Fact]
    public void Move_SpawnsHitbox_AtTriggerTick()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });

        int triggerTick = Stage.HitboxEvents[0].TriggerTick;

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
}
