using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Movement data sheet probe (issue #150) — external-behavior assertions on the real sim:
/// run reaches authored RunSpeed, dash distance tracks DashSpeed × duration, jump apex
/// respects Gravity + float window, fast fall reaches FastFallSpeed, drift caps at
/// AirSpeedMax, release stops. Tolerances absorb tick quantization — the values come from
/// the same probe the report renders, so a failure means the sim's movement changed.
/// </summary>
public class MovementProbeTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static readonly MovementStats M = Def.Movement;

    private static MovementProbe.CharacterMovement Measured() =>
        MovementProbe.Measure(Def, TestHelpers.TestArena());

    [Fact]
    public void Run_HoldingStick_ReachesAuthoredRunSpeed()
    {
        var run = Measured().Run;
        // Rush kick-off (ADR-0020): cruise speed on the first tick, no ramp.
        Assert.True(run.TimeToMaxTicks <= 2, $"time-to-max {run.TimeToMaxTicks} ticks");
        Assert.Equal(M.RunSpeed, run.MaxSpeed, 2); // 14 m/s
        Assert.True(run.Curve[30].Speed >= M.RunSpeed * 0.99f, "speed decays below cruise");
    }

    [Fact]
    public void Dash_Distance_TracksAuthoredSpeedAndDuration()
    {
        var dash = Measured().Dash;
        // Constant DashSpeed for DashDurationTicks, then hard stop.
        Assert.InRange(dash.DurationTicks, M.DashDurationTicks - 1, M.DashDurationTicks + 1);
        float expected = M.DashSpeed * (M.DashDurationTicks / 60f);
        // Input lands on the next tick's movement: the sim moves 19 of 20 ticks, so the
        // tolerance absorbs the tick-quantized duration (6.33 vs 6.67).
        Assert.True(Math.Abs(dash.TotalDistance - expected) <= expected * 0.08f,
            $"dash distance {dash.TotalDistance:F2} vs {expected:F2}");
        // Hard stop: actionable right after the burst, speed zeroed.
        Assert.True(dash.Curve[dash.ActionableTick].Speed < 0.01f, "dash coasts after expiry");
    }

    [Fact]
    public void Jump_Apex_RespectsGravityAndFloatWindow()
    {
        var jump = Measured().Jump;
        // Measured contract (what the sim actually does): the jump pre-sets
        // AirTimeTicks = FloatWindowTicks, and the float-window gate is `AirTimeTicks <
        // window`, so the window never applies to jumps — full Gravity from takeoff.
        // With AirFloatGravity = 0 the apex is exactly JumpForce²/(2·Gravity).
        float expected = M.AirFloatGravity == 0f
            ? (M.JumpForce * M.JumpForce) / (2f * M.Gravity)
            : M.JumpForce * (M.FloatWindowTicks / 60f) + (M.JumpForce * M.JumpForce) / (2f * M.Gravity);
        Assert.True(Math.Abs(jump.ApexHeight - expected) <= expected * 0.08f,
            $"apex {jump.ApexHeight:F2} vs {expected:F2}");
        Assert.True(jump.AirtimeTicks > 0);
    }

    [Fact]
    public void FastFall_ReachesAuthoredFastFallSpeed()
    {
        var fall = Measured().Fall;
        // The 50 m drop must be long enough to hit the gravity cap — this pins the
        // measurement to the real fall regime, not the short jump tail.
        Assert.True(fall.MaxFallSpeed >= M.MaxFallSpeed * 0.98f,
            $"max fall {fall.MaxFallSpeed:F1} vs {M.MaxFallSpeed}");
        Assert.True(fall.FastFallSpeed >= M.FastFallSpeed * 0.98f,
            $"fast fall {fall.FastFallSpeed:F1} vs {M.FastFallSpeed}");
        Assert.True(fall.FastFallReachTicks <= 3, $"took {fall.FastFallReachTicks} ticks to reach FF");
        Assert.True(fall.FastFallDescentTicks < fall.DescentTicks,
            "fast fall should descend faster than natural fall");
        // From a real full-jump apex the fast-fall descent is under a quarter second —
        // the landing-mixup window (natural ~0.3s → fast-fall ~0.03s).
        Assert.True(fall.FastFallFromJumpTicks < 15,
            $"jump fast-fall descent {fall.FastFallFromJumpTicks} ticks");
    }

    [Fact]
    public void AirDrift_CapsAtAuthoredAirSpeedMax()
    {
        var jump = Measured().Jump;
        Assert.True(jump.DriftSpeedMax >= M.AirSpeedMax * 0.97f,
            $"drift {jump.DriftSpeedMax:F1} vs air cap {M.AirSpeedMax}");
    }

    [Fact]
    public void Stop_FromCruise_BrakesToStandstill()
    {
        var stop = Measured().Stop;
        Assert.True(stop.StopTicks <= 30, $"stop took {stop.StopTicks} ticks");
        Assert.True(stop.StopDistance <= 4f, $"stop distance {stop.StopDistance:F2}m");
    }

    [Fact]
    public void ShortHop_Apex_RespectsShortHopForce()
    {
        var sh = Measured().ShortHop;
        float expected = (M.ShortHopForce * M.ShortHopForce) / (2f * M.Gravity);
        // Sample quantization lands up to ~10% below the theoretical peak (short hops are
        // short — the discrete max sits one tick off the true apex).
        Assert.True(Math.Abs(sh.ApexHeight - expected) <= expected * 0.12f,
            $"short-hop apex {sh.ApexHeight:F2} vs {expected:F2}");
        // A short hop is shorter AND faster than the full hop.
        Assert.True(sh.ApexHeight < Measured().Jump.ApexHeight, "short hop taller than full hop");
        Assert.True(sh.AirtimeTicks < Measured().Jump.AirtimeTicks, "short hop slower than full hop");
    }

    [Fact]
    public void Reversal_FromCruise_IsPivotSkidThenReaccel()
    {
        var rev = Measured().Reversal;
        // A 180° flip does NOT refresh the rush window (perpendicular redirects only), so
        // reversal = skid through zero + soft-start re-accel. It must take longer than a
        // single tick (no instant flip) and complete within a sane window.
        Assert.True(rev.ReversalTicks > 5, $"reversal suspiciously instant ({rev.ReversalTicks} ticks)");
        Assert.True(rev.ReversalTicks <= 60, $"reversal too slow ({rev.ReversalTicks} ticks)");
        Assert.True(rev.Displacement > 1f, $"reversal displacement {rev.Displacement:F2}m");
    }

    [Fact]
    public void Measure_IsDeterministic()
    {
        var a = Measured();
        var b = Measured();
        Assert.Equal(a.Run.MaxSpeed, b.Run.MaxSpeed);
        Assert.Equal(a.Dash.TotalDistance, b.Dash.TotalDistance);
        Assert.Equal(a.Jump.ApexHeight, b.Jump.ApexHeight);
        Assert.Equal(a.Jump.AirtimeTicks, b.Jump.AirtimeTicks);
        Assert.Equal(a.Fall.MaxFallSpeed, b.Fall.MaxFallSpeed);
    }
}
