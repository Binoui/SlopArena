using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Aerial landing lag + auto-cancel windows (issue #125): an air stage can declare
/// LandingLagTicks — landing while the aerial is active applies that no-input/no-movement
/// lock unless the landing frame falls inside an auto-cancel window (stage-elapsed
/// &lt;= AutoCancelBeforeTicks or &gt;= AutoCancelAfterTicks → no lag, act immediately).
/// All-zero fields preserve the pre-issue behavior (landing never locks).
///
/// Physics used by the scenarios: the test aerial runs 40 ticks and (unless the test sets a
/// lunge) declares LungeForce 0 — StageChainAbility's lunge write would zero VY on
/// activation, so a lunging aerial hovers during the zero-gravity float window instead of
/// falling. With lunge 0 the character falls straight down at a constant -10 m/s (float
/// gravity 0 for the first 35 air ticks): a start height of 1.4 m lands at stage-elapsed 8
/// (mid active window → lag), 4.7 m lands at stage-elapsed 28 (inside the 6..32 mid window →
/// lag; with the late window at 26 the same landing auto-cancels). The lock outlives the
/// aerial (18 ticks from a t27 landing expires at t45, 6 ticks after the move ends at t39),
/// which is what makes the post-move input blocks below discriminate the lag from the move's
/// own anim lock.
/// </summary>
public class LandingLagTests : KitScenarioTests
{
    private const ushort AirLmbDuration = 40;
    private const ushort AirLmbLag = 18;
    private const ushort AirLmbBefore = 6;
    private const ushort AirLmbAfter = 32;
    /// <summary>Fall speed for the scenarios: -10 m/s at 60 Hz = 1/6 m per tick.</summary>
    private const float FallSpeed = -10f;

    private static CharacterDefinition MakeDef(ushort lag, ushort before, ushort after,
        float lunge = 0f, float airFloatGravity = 0f)
    {
        var def = TestHelpers.CloneDef(TestHelpers.FightGuyDef);
        if (airFloatGravity != 0f)
            def.Movement = def.Movement with { AirFloatGravity = airFloatGravity };
        def.AirLMB = new AbilitySpec
        {
            Name = "Test Aerial",
            CooldownTicks = 0,
            AnimationNames = new[] { "test_aerial" },
            Params = new() { ["lunge_duration"] = 10f },
            Stages = new[]
            {
                new AttackStage
                {
                    DurationTicks = AirLmbDuration,
                    LandingLagTicks = lag,
                    AutoCancelBeforeTicks = before,
                    AutoCancelAfterTicks = after,
                    LungeForce = lunge,
                    HitboxEvents = Array.Empty<HitboxEvent>(),
                },
            },
        };
        // GetParam reads the GROUND spec's params (airborne:false), so the lunge window
        // lives on LMB — the spec the air ability actually reads for "lunge_duration".
        def.LMB = new AbilitySpec
        {
            Name = "Test Ground",
            CooldownTicks = 0,
            AnimationNames = new[] { "test_ground" },
            Params = new() { ["lunge_duration"] = 6f },
            Stages = new[] { new AttackStage { DurationTicks = 1 } },
        };
        return def;
    }

    /// <summary>Mid-window landing: lag 18, auto-cancel 6..32, no lunge (clean vertical fall).</summary>
    private static readonly CharacterDefinition LagDef = MakeDef(AirLmbLag, AirLmbBefore, AirLmbAfter);
    /// <summary>Late-window landing: auto-cancel from 26, so a stage-elapsed-28 landing is clean.</summary>
    private static readonly CharacterDefinition CleanLateDef = MakeDef(AirLmbLag, AirLmbBefore, 26);
    /// <summary>Default-off control: every field zero — current behavior, landing never locks.</summary>
    private static readonly CharacterDefinition ZeroDef = MakeDef(0, 0, 0);
    /// <summary>
    /// Freeze control: lunging aerial (4 m/s — StageChainAbility re-applies it each tick of
    /// its lunge window and its lunge write zeroes VY on top of that, so full float gravity
    /// is needed for the fall to resume after activation) starting 0.4 m up. The lunge
    /// window (6 ticks — GetParam reads the ground spec's lunge_duration) keeps the fall
    /// crawling at 0.01 m/tick, so touchdown lands at stage-elapsed 13 with the residual
    /// VZ = 4 drift still live — the lock must zero it the instant it applies.
    /// </summary>
    private static readonly CharacterDefinition FreezeDef =
        MakeDef(AirLmbLag, AirLmbBefore, AirLmbAfter, lunge: 4f, airFloatGravity: 36f);

    private static float Gpy => TestHelpers.GroundPY(TestHelpers.FightGuyDef); // 0.85

    /// <summary>Airborne falling start: heightAbove above ground, straight down, no horizontal input.</summary>
    private static CharacterState FallingStart(float heightAbove)
        => TestHelpers.PlayerState() with { PY = Gpy + heightAbove, VY = FallSpeed, IsGrounded = false };

    /// <summary>Run a scenario manually, returning the state after each tick.</summary>
    private static List<CharacterState> Run(CharacterDefinition def, float heightAbove,
        Func<int, InputState> inputFor)
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, FallingStart(heightAbove), TestHelpers.LoadBakedData(def));
        var states = new List<CharacterState>();
        for (int tick = 0; tick < 60; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, inputFor(tick) } });
            states.Add(sim.GetState(1));
        }
        return states;
    }

    private static InputState LmbAt(int tick) => tick == 0
        ? new InputState { ActiveSlot = AbilitySlots.Lmb }
        : default;

    // ── Goldens: clean-land vs lag-land on the same aerial ──

    /// <summary>
    /// FightGuy's air normals (AirSlot1-4) instantiate as AirLmbCombo (AbilityFactory), which
    /// the landing-lag startedAirborne check already covers — so a mid-window landing must
    /// apply the air spec's LandingLagTicks. This guards the factory mapping + the check.
    /// </summary>
    [Fact]
    public void AirSlot1_LandingMidWindow_AppliesLandingLag()
    {
        var def = TestHelpers.CloneDef(TestHelpers.FightGuyDef);
        def.AirSlot1 = new AbilitySpec
        {
            Name = "Test Aerial Slot1",
            CooldownTicks = 0,
            AnimationNames = new[] { "test_aerial" },
            Stages = new[]
            {
                new AttackStage
                {
                    DurationTicks = AirLmbDuration,       // 40
                    LandingLagTicks = AirLmbLag,          // 18
                    AutoCancelBeforeTicks = AirLmbBefore, // 6
                    AutoCancelAfterTicks = AirLmbAfter,   // 32
                    LungeForce = 0f,
                    HitboxEvents = Array.Empty<HitboxEvent>(),
                },
            },
        };
        def.Slot1 = new AbilitySpec
        {
            Name = "Test Ground Slot1",
            CooldownTicks = 0,
            AnimationNames = new[] { "test_ground" },
            Stages = new[] { new AttackStage { DurationTicks = 1 } },
        };

        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, FallingStart(4.7f), TestHelpers.LoadBakedData(def));
        var states = new List<CharacterState>();
        for (int tick = 0; tick < 60; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
                { { 1, tick == 0 ? new InputState { ActiveSlot = AbilitySlots.Slot1 } : default } });
            states.Add(sim.GetState(1));
        }

        // 4.7 m start lands at stage-elapsed ~28 (mid window 6..32) → landing lag must apply.
        bool appliedLag = false;
        foreach (var s in states)
            if (s.IsGrounded && s.LandingLagTicks > 0) { appliedLag = true; break; }
        Assert.True(appliedLag,
            "AirSlot1 (AirLmbCombo) should apply its air spec's landing lag on a mid-window landing");
    }

    /// <summary>
    /// Both goldens share physics and inputs (aerial at t0, jump press at t30 — 3 ticks
    /// after the t27 landing, snapshot at t33) — the only difference is the stage's declared
    /// auto-cancel window. Mid-window (6..32): the stage-elapsed-28 landing locks for 18
    /// ticks and the aerial keeps running, so the jump press is dropped — still Attacking,
    /// grounded, JumpsLeft 2 at the snapshot.
    /// </summary>
    [Fact]
    public void Golden_LagLand_MidWindow_BlocksJumpThroughLag()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Landing Lag Mid Window Blocks Jump",
            Def = LagDef,
            Setup = () => FallingStart(4.7f),
            Inputs = new InputSequence()
                .Press(0, AbilitySlots.Lmb)
                .Set(30, new InputState { Jump = true }),
            Assert = _ => { },
            SnapshotTick = 33,   // mid-lag, aerial still active: jump press dropped
            TotalTicks = 120,
        });
    }

    /// <summary>
    /// Late window (26..): the same stage-elapsed-28 landing auto-cancels — the aerial ends
    /// on the landing frame, so the same jump press at t30 goes through (JumpSquat,
    /// JumpsLeft 1 at the snapshot): the "act immediately" half of auto-cancel, pinned.
    /// </summary>
    [Fact]
    public void Golden_CleanLand_LateWindow_AutoCancels()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Landing Lag AutoCancel Late Window",
            Def = CleanLateDef,
            Setup = () => FallingStart(4.7f),
            Inputs = new InputSequence()
                .Press(0, AbilitySlots.Lmb)
                .Set(30, new InputState { Jump = true }),
            Assert = _ => { },
            SnapshotTick = 33,   // jumped at t30 → JumpSquat
            TotalTicks = 120,
        });
    }

    // ── Mechanics: the lock itself ──

    /// <summary>
    /// Landing at stage-elapsed 8 (1.4 m start) with no lunge: the lag applies (full 18
    /// ticks) and the character is planted — VX pinned to 0 and PX constant through the
    /// lock. The lock counts down one per tick and expires at t25.
    /// </summary>
    [Fact]
    public void LandingMidAerial_AppliesLag_FreezesMovement()
    {
        var states = Run(LagDef, 1.4f, LmbAt);

        // Landed at t7 (elapsed 8 — mid window), full 18-tick lock applied.
        Assert.Equal((ushort)18, states[7].LandingLagTicks);
        Assert.True(states[7].IsGrounded);

        // Lock counts down 18 ticks: still live at t24, expired at t25.
        // (No-movement is asserted in LandingMidAerial_LagFreezesResidualLungeVelocity,
        // where a live velocity exists at touchdown — this scenario has none.)
        Assert.Equal((ushort)1, states[24].LandingLagTicks);
        Assert.Equal((ushort)0, states[25].LandingLagTicks);
    }

    /// <summary>
    /// The lock suppresses movement even when velocity is live at touchdown: the lunging
    /// FreezeDef still carries its VZ = 4 lunge drift (facing +Z) when it lands at
    /// stage-elapsed 13 — the freeze zeroes it the instant the lock applies, so the
    /// character does not slide through the lock the way it would with no lag.
    /// </summary>
    [Fact]
    public void LandingMidAerial_LagFreezesResidualLungeVelocity()
    {
        var states = Run(FreezeDef, 0.4f, LmbAt);

        // Landed at t12 (elapsed 13 — mid window), full 18-tick lock applied.
        Assert.True(states[12].IsGrounded);
        Assert.Equal((ushort)18, states[12].LandingLagTicks);

        // The lunge drift was live through the fall; the freeze zeroes it at touchdown and
        // pins the position for the whole lock (no friction runs during Attacking, so an
        // unfrozen VZ = 4 would slide the character across the floor).
        Assert.True(states[11].PZ > states[0].PZ); // drifted while airborne
        Assert.Equal(0f, states[12].VZ);           // frozen at the landing tick
        Assert.Equal(0f, states[20].VZ);           // and through the rest of the lock
        Assert.Equal(states[12].PZ, states[20].PZ);

        // 18 ticks from t12 → expired at the start of t30.
        Assert.Equal((ushort)1, states[29].LandingLagTicks);
        Assert.Equal((ushort)0, states[30].LandingLagTicks);
    }

    /// <summary>
    /// The lock is a hard no-input window once the aerial's own anim lock is gone: the
    /// aerial ends at t39, the lag (applied t27) holds until t45 — so jump/dash presses at
    /// t41/t42 are dropped, and a jump press after the expiry (t46) goes through.
    /// </summary>
    [Fact]
    public void LandingMidAerial_LagBlocksInputsAfterAerialEnds()
    {
        var states = Run(LagDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Lmb },
            41 => new InputState { Jump = true },
            42 => new InputState { Dash = true },
            46 => new InputState { Jump = true },
            _ => default,
        });

        // Aerial ended t39; the lag is still live at t41/t42 and drops both presses.
        Assert.Equal(ActionState.Idle, states[42].State);
        Assert.True(states[42].LandingLagTicks > 0);
        Assert.Equal((ushort)0, states[43].DashDurationTicks);

        // Lag expired at the start of t45: the t46 jump goes through.
        Assert.Equal(ActionState.JumpSquat, states[47].State);
    }

    /// <summary>
    /// All-zero fields = pre-issue behavior: the same landing applies no lock and a jump
    /// press right after the aerial ends goes through immediately.
    /// </summary>
    [Fact]
    public void LandingMidAerial_ZeroFields_NoLag_CurrentBehavior()
    {
        var states = Run(ZeroDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Lmb },
            41 => new InputState { Jump = true },
            _ => default,
        });

        Assert.True(states[27].IsGrounded);
        Assert.Equal((ushort)0, states[27].LandingLagTicks);
        Assert.Equal(ActionState.JumpSquat, states[42].State); // jump at t41 (aerial ended t39)
    }

    /// <summary>
    /// Early auto-cancel window: pressing the aerial 5 ticks before landing (stage-elapsed 5
    /// at touchdown, &lt;= AutoCancelBeforeTicks 6) skips the lock entirely AND ends the move
    /// on the landing frame — the player acts immediately instead of riding the recovery.
    /// </summary>
    [Fact]
    public void LandingInAutoCancelEarlyWindow_NoLag_ActsImmediately()
    {
        var states = Run(LagDef, 4.7f, tick => tick == 23
            ? new InputState { ActiveSlot = AbilitySlots.Lmb }
            : default);

        Assert.True(states[27].IsGrounded);
        Assert.Equal((ushort)0, states[27].LandingLagTicks); // elapsed 5 ≤ 6 → auto-cancel
        Assert.Equal(ActionState.Idle, states[28].State);    // the aerial ended on landing
        Assert.Equal((byte)0, states[28].AttackSlot);
    }

    /// <summary>
    /// Late auto-cancel window: a stage-elapsed-28 landing with AutoCancelAfterTicks 26
    /// skips the lock AND ends the aerial on the landing frame — a jump press three ticks
    /// after landing (t30) goes through immediately, while the mid-window landing would
    /// still be attack-locked at that tick.
    /// </summary>
    [Fact]
    public void LandingInAutoCancelLateWindow_NoLag_ActsImmediately()
    {
        var states = Run(CleanLateDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Lmb },
            30 => new InputState { Jump = true },
            _ => default,
        });

        Assert.True(states[27].IsGrounded);
        Assert.Equal((ushort)0, states[27].LandingLagTicks); // elapsed 28 ≥ 26 → auto-cancel
        Assert.Equal(ActionState.Idle, states[28].State);    // the aerial ended on landing
        Assert.Equal(ActionState.JumpSquat, states[31].State); // act immediately (t30 jump)
    }

    /// <summary>
    /// Ability inputs are hard-blocked inside the lock and never buffered through it: a
    /// Slot1 press at t41 (lag live, anim lock gone) is dropped — no second attack follows,
    /// and no buffered re-trigger fires when the lag expires.
    /// </summary>
    [Fact]
    public void AbilityInputDuringLag_IsDropped_NotBuffered()
    {
        var states = Run(LagDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Lmb },
            41 => new InputState { ActiveSlot = AbilitySlots.Slot1 },
            _ => default,
        });

        Assert.True(states[41].LandingLagTicks > 0);
        Assert.Equal(ActionState.Idle, states[45].State);     // dropped — still Idle mid-lag
        Assert.Equal((byte)0, states[45].AttackSlot);
        Assert.Equal(ActionState.Idle, states[50].State);     // and nothing fired post-lag
        Assert.Equal((byte)0, states[50].AttackSlot);
    }

    /// <summary>
    /// "No movement" holds after the aerial has ended too (the lock outlives the move): the
    /// stick cannot steer while lagged; the instant the lag expires the same stick walks.
    /// </summary>
    [Fact]
    public void MoveInputDuringLag_NoWalk_UntilLagExpires()
    {
        var states = Run(LagDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Lmb },
            >= 40 => new InputState { MoveX = 1f },
            _ => default,
        });

        // Aerial ended t39 → Idle, but the lag (live t28..44) still plants the character.
        Assert.Equal(ActionState.Idle, states[42].State);
        Assert.True(states[42].LandingLagTicks > 0);
        Assert.Equal(states[41].PX, states[44].PX); // stick held through the lock: planted
        Assert.True(states[50].PX > states[44].PX); // lag expired (t45): the stick walks
    }

    /// <summary>
    /// Being hit ends the commitment: ApplyKnockback (the hitstun choke point) clears the
    /// lock so a stale LandingLagTicks cannot re-lock the victim after hitstun resolves.
    /// </summary>
    [Fact]
    public void Hitstun_ClearsLandingLag()
    {
        var s = TestHelpers.PlayerState() with { LandingLagTicks = 18 };
        Simulation.ApplyKnockback(ref s, 1f, 0f, 45, 10f, 5f, 0f, 20, 100f);
        Assert.Equal((ushort)0, s.LandingLagTicks);
        Assert.Equal(ActionState.Hitstun, s.State);
    }
}
