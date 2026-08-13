using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Fast-fall tests (issue #116 / #107, ADR-0016 → ADR-0020 set-velocity model): holding
/// Down in the air sets VY to <see cref="MovementStats.FastFallSpeed"/> directly (no gravity
/// that tick), only while already falling. Applies in every airborne state except hitstun;
/// release cancels back to natural gravity; Down on the ground does nothing.
/// The client maps the Down bit to a dedicated key (X by default, issue #116) — NOT to
/// the backward-movement key, so drifting backward never fast-falls.
/// </summary>
public class FastFallTests
{
    // Classic def: no FloatWindow → full gravity from the first air tick.
    private static readonly CharacterDefinition Def = CreateClassicDef();
    private static readonly MovementStats Move = Def.Movement;

    private static CharacterDefinition CreateClassicDef()
    {
        var mov = TestHelpers.MankiDef.Movement;
        mov.FloatWindowTicks = 0;
        return TestHelpers.CloneDef(TestHelpers.MankiDef, mov);
    }

    /// <summary>Falling from a high spawn with zero initial VY — must not land during the test.</summary>
    private static ServerSimulation SimFalling(float py = 15f)
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = py;
        state.IsGrounded = false;
        state.AirTimeTicks = 60; // past any float window — full gravity region
        TestHelpers.RegisterPlayer(sim, Def, state);
        return sim;
    }

    [Fact]
    public void DownHeld_Air_SetsFastFallVelocity()
    {
        var fast = SimFalling();
        TestHelpers.TickHold(fast, TestHelpers.Input(down: true), 5);
        TestHelpers.AssertNear(-Move.FastFallSpeed, fast.GetState(1).VY, 0.001f);
    }

    [Fact]
    public void ReleaseDown_CancelsFastFall()
    {
        var released = SimFalling(py: 25f);
        var keptFalling = SimFalling(py: 25f);

        // Both fast-fall for 10 ticks, then one releases.
        TestHelpers.TickHold(released, TestHelpers.Input(down: true), 10);
        TestHelpers.TickHold(keptFalling, TestHelpers.Input(down: true), 10);
        TestHelpers.TickDefault(released, 15);
        TestHelpers.TickHold(keptFalling, TestHelpers.Input(down: true), 15);

        var a = released.GetState(1);
        var b = keptFalling.GetState(1);
        Assert.True(a.VY > b.VY,
            $"released fall must slow relative to continuing fast fall: released={a.VY:F3} kept={b.VY:F3}");
        // Released sim returns to natural gravity and clamps at MaxFallSpeed.
        TestHelpers.AssertNear(-Move.MaxFallSpeed, a.VY, 0.01f);
    }

    [Fact]
    public void DownWhileGrounded_NoEffect()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        TestHelpers.TickHold(sim, TestHelpers.Input(down: true), 10);
        var s = sim.GetState(1);
        Assert.True(s.IsGrounded);
        TestHelpers.AssertNear(0f, s.VY, 0.001f);
    }

    [Fact]
    public void DownDuringAirAttack_FastFalls()
    {
        // "Works in all airborne states except hitstun" — an active air attack is the
        // commitment case: fast-falling through an aerial is the point of the mechanic.
        var control = SimFalling();
        var fast = SimFalling();

        // Start an air LMB (slot 1 airborne) on both, then only one holds Down.
        TestHelpers.TickN(control, TestHelpers.Input(activeSlot: 1), 1);
        TestHelpers.TickN(fast, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, control.GetState(1).State);

        TestHelpers.TickDefault(control, 15);
        TestHelpers.TickHold(fast, TestHelpers.Input(down: true), 15);

        var a = control.GetState(1);
        var b = fast.GetState(1);
        Assert.True(b.VY < a.VY,
            $"fast fall must work through an air attack: control={a.VY:F3} fast={b.VY:F3}");
    }

    [Fact]
    public void DownDuringHitstun_NoFastFall()
    {
        var control = SimFalling();
        var hit = SimFalling();
        foreach (var sim in new[] { control, hit })
        {
            var st = sim.GetState(1);
            st.State = ActionState.Hitstun;
            st.HitstunTicks = 30;
            st.VY = -5f;
            sim.SetState(1, st);
        }

        TestHelpers.TickDefault(control, 10);
        TestHelpers.TickHold(hit, TestHelpers.Input(down: true), 10);

        // Hitstun owns the trajectory (ProcessHitstun / knockback); Down must not add
        // fast-fall acceleration on top of it.
        TestHelpers.AssertNear(control.GetState(1).VY, hit.GetState(1).VY, 0.001f);
    }
}
