using Xunit;
using System.Collections.Generic;
using System.Text;

namespace SlopArena.Shared.Tests;

public class AirDashAnalysisTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

    /// <summary>
    /// Record every tick of an aerial dash (holding forward).
    /// Proves bug: AirTimeTicks reset by StartDash causes FloatWindow to restart,
    /// leaving the character hovering with VY=0 for ~250ms after the dash ends.
    /// </summary>
    [Fact]
    public void Record_AerialDash_FrameByFrame()
    {
        // ── Setup: airborne 3m, AirTimeTicks=10 (been falling a bit) ──
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        float groundY = TestHelpers.GroundPY(Def);
        state.PY = groundY + 3f;
        state.IsGrounded = false;
        state.VY = -2f; // was falling before dash
        state.JumpsLeft = 1;
        state.AirTimeTicks = 10; // already airborne for 10 ticks
        TestHelpers.RegisterPlayer(sim, Def, state);

        var sb = new StringBuilder();
        sb.AppendLine($"Air dash frame-by-frame (Manki, holding forward)");
        sb.AppendLine($"Pre-dash: airborne 3m, VY=-2, AirTimeTicks=10");
        sb.AppendLine($"FloatWindowTicks={Def.Movement.FloatWindowTicks}, FallRampDuration={Def.Movement.FallRampDuration}");
        sb.AppendLine($"AirFloatGravity={Def.Movement.AirFloatGravity}, Gravity={Def.Movement.Gravity}");
        sb.AppendLine($"DashDurationTicks={Def.Movement.DashDurationTicks}, DashSpeed={Def.Movement.DashSpeed}");
        sb.AppendLine($"");
        sb.AppendLine($"{"Tick",4} {"State",-10} {"Dur",4} {"AirT",4} {"VX",8} {"VZ",8} {"VY",8} {"PY",8} {"PZ",8} {"Gnd",3} {"Note"}");
        sb.AppendLine(new string('-', 100));

        // Tick 0: dash + forward
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(dash: true, moveY: 1f) }
        });

        // Record ticks 0-65
        for (int tick = 0; tick < 66; tick++)
        {
            var s = sim.GetState(1);
            string note = "";

            if (tick == 0) note = "<-- DASH START: AirTimeTicks RESET to 0!";
            else if (s.DashDurationTicks == 0 && s.State == ActionState.Idle 
                     && tick > 0 && System.MathF.Abs(s.VY) < 0.001f)
                note = "<-- HOVER (dash ended, VY=0, FloatWindow still active)";
            else if (s.VY < -0.01f && s.VY > -0.5f)
                note = "<-- SLOW DESCENT (early FallRamp)";
            else if (s.VY < -4f)
                note = "<-- FULL GRAVITY";

            sb.AppendLine($"{tick,4} {s.State,-10} {s.DashDurationTicks,4} {s.AirTimeTicks,4} {s.VX,8:F3} {s.VZ,8:F3} {s.VY,8:F3} {s.PY,8:F3} {s.PZ,8:F3} {s.IsGrounded,3} {note}");

            // Tick with forward held, no dash
            sim.Tick(new Dictionary<ulong, InputState>
            {
                { 1, TestHelpers.Input(moveY: 1f) }
            });
        }

        string output = sb.ToString();
        System.IO.File.WriteAllText("/tmp/aerial_dash_trace.txt", output);

        // ── KEY ASSERTIONS (the bug) ──
        // After dash expires (tick 15), VX=VZ=0, VY should be 0
        // AirTimeTicks was reset to 0 by StartDash, so FloatWindow still active
        
        // BUG PROOF: VY stays 0 for many ticks after dash ends
        // We'll capture a second sim recording to get tick-by-tick states
        var recordedStates = new List<CharacterState>();
        var sim2 = TestHelpers.MakeSim();
        var s2 = TestHelpers.PlayerState();
        s2.PY = groundY + 3f;
        s2.IsGrounded = false;
        s2.VY = -2f;
        s2.JumpsLeft = 1;
        s2.AirTimeTicks = 10;
        TestHelpers.RegisterPlayer(sim2, Def, s2);

        for (int t = 0; t < 50; t++)
        {
            sim2.Tick(new Dictionary<ulong, InputState>
            {
                { 1, t == 0 ? TestHelpers.Input(dash: true, moveY: 1f) : TestHelpers.Input(moveY: 1f) }
            });
            recordedStates.Add(sim2.GetState(1));
        }

        // Tick 15 = dash end (tick 0 was dash start, 15 ticks of dash)
        Assert.True(recordedStates.Count > 15);
        var afterDash = recordedStates[15];
        
        // VY should be 0 (bug: hover)
        System.Console.Error.WriteLine($"Tick 15 (dash end): VY={afterDash.VY:F6}, AirTimeTicks={afterDash.AirTimeTicks}");

        // Tick 20 (5 ticks after dash end) — VY should still be ~0 due to FloatWindow
        var at20 = recordedStates[20];
        System.Console.Error.WriteLine($"Tick 20 (5 after dash end): VY={at20.VY:F6}, AirTimeTicks={at20.AirTimeTicks}");

        // Tick 30 (15 ticks after dash end) — should barely be falling
        var at30 = recordedStates[30];
        System.Console.Error.WriteLine($"Tick 30 (15 after dash end): VY={at30.VY:F6}, AirTimeTicks={at30.AirTimeTicks}, PY={at30.PY:F6}");

        // Tick 45 (30 after dash end) — should be near ground
        var at45 = recordedStates[45];
        System.Console.Error.WriteLine($"Tick 45 (30 after dash end): VY={at45.VY:F6}, PY={at45.PY:F6} (ground at {groundY:F6})");
    }

    /// <summary>
    /// Record with forward hold — tests the "slight offset" issue.
    /// </summary>
    [Fact]
    public void Record_AerialDash_ForwardHold_DuringFall()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        float groundY = TestHelpers.GroundPY(Def);
        state.PY = groundY + 5f;
        state.IsGrounded = false;
        state.VY = -1f;
        state.JumpsLeft = 1;
        state.AirTimeTicks = 8;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var sb = new StringBuilder();
        sb.AppendLine($"Air dash + forward hold frame-by-frame");
        sb.AppendLine($"{"Tick",4} {"State",-10} {"Dur",4} {"AirT",4} {"VX",8} {"VZ",8} {"VY",8} {"PY",8} {"PZ",8} {"FaceYaw",8} {"Note"}");
        sb.AppendLine(new string('-', 100));

        for (int tick = 0; tick < 70; tick++)
        {
            var s = sim.GetState(1);
            string note = tick == 0 ? "<-- DASH START" : "";

            sb.AppendLine($"{tick,4} {s.State,-10} {s.DashDurationTicks,4} {s.AirTimeTicks,4} {s.VX,8:F3} {s.VZ,8:F3} {s.VY,8:F3} {s.PY,8:F3} {s.PZ,8:F3} {s.FacingYaw,8:F3} {note}");

            sim.Tick(new Dictionary<ulong, InputState>
            {
                { 1, tick == 0 ? TestHelpers.Input(dash: true, moveY: 1f) : TestHelpers.Input(moveY: 1f) }
            });
        }

        string output = sb.ToString();
        System.IO.File.WriteAllText("/tmp/aerial_dash_forward_trace.txt", output);
    }
}
