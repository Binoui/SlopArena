using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Aerial landing termination (drift fix): landing while an AIR-STARTED ability is still
/// active must END the move (back to Idle) so ground friction can stop it — even when the
/// stage declares no LandingLagTicks. Previously a no-landing-lag aerial (Cyclone R,
/// Rising Dragon E) kept the character in Attacking on the floor: ProcessNormalMovement is
/// skipped for Attacking, so zero friction applied, and the move kept writing its lunge
/// velocity every tick — the character slid across the stage at full speed with only dash
/// able to stop it. Now the landing frame terminates the aerial unconditionally
/// (LandingLagTicks just controls the lock length; 0 = end with no lock).
/// </summary>
public class LandingAerialDriftTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static readonly float GroundPy = TestHelpers.GroundPY(Def);

    private static InputState Fwd(float moveY = 1f, bool jump = false, byte slot = 0) => new()
    {
        MoveY = moveY,
        Jump = jump,
        ActiveSlot = slot,
    };

    /// <summary>
    /// Short hop, whiff an air move mid-flight, then release all input after landing.
    /// Returns (pz, vz, states, grounded) sampled each tick.
    /// </summary>
    private static (List<float> pz, List<float> vz, List<ActionState> states, List<bool> grounded)
        LandMidAerial(byte slot, int aerialStartTick, int releaseTick, int totalTicks)
    {
        var sim = TestHelpers.MakeSim();
        var p = TestHelpers.PlayerState() with { PY = GroundPy, PZ = 0f };
        TestHelpers.RegisterPlayer(sim, Def, p);

        var inputs = new Dictionary<ulong, InputState>();
        var pz = new List<float>();
        var vz = new List<float>();
        var states = new List<ActionState>();
        var grounded = new List<bool>();
        for (int t = 0; t < totalTicks; t++)
        {
            if (t < 20) inputs[1] = Fwd();
            else if (t == 20) inputs[1] = Fwd(jump: true);               // short hop
            else if (t < aerialStartTick) inputs[1] = Fwd();
            else if (t < releaseTick) inputs[1] = Fwd(slot: slot);       // whiff aerial
            else inputs[1] = default;                                    // release all
            sim.Tick(inputs);
            var s = sim.GetState(1);
            pz.Add(s.PZ); vz.Add(s.VZ); states.Add(s.State); grounded.Add(s.IsGrounded);
        }
        return (pz, vz, states, grounded);
    }

    private static int FirstTakeoff(List<bool> grounded)
    {
        for (int t = 21; t < grounded.Count; t++) if (!grounded[t]) return t;
        return -1;
    }

    private static int FirstLanding(List<bool> grounded, int afterTick)
    {
        for (int t = afterTick; t < grounded.Count; t++) if (grounded[t]) return t;
        return -1;
    }

    [Fact]
    public void Cyclone_LandMidMove_EndsOnLanding_NoSlide()
    {
        var (pz, vz, states, grounded) = LandMidAerial(AbilitySlots.R, aerialStartTick: 28, releaseTick: 48, totalTicks: 120);

        int takeoff = FirstTakeoff(grounded);
        int landing = FirstLanding(grounded, takeoff);
        Assert.True(landing > 0, "should land");

        // On the landing frame the aerial ends: state returns to Idle immediately.
        Assert.Equal(ActionState.Idle, states[landing]);

        // Momentum is preserved (ADR-0015) but friction resumes: velocity decays to zero
        // within a bounded window — it must NOT persist at the lunge speed (previously 17).
        float vzAtLanding = vz[landing];
        Assert.True(vzAtLanding > 0f, "should retain lunge momentum on landing");

        // 30 ticks after landing the character must be substantially stopped (friction),
        // not still sliding at full lunge speed.
        int stopCheck = Math.Min(landing + 30, states.Count - 1);
        Assert.True(vz[stopCheck] < 2f,
            $"landing must brake the lunge drift: vz[{stopCheck}]={vz[stopCheck]:F2}");
    }

    [Fact]
    public void AirNormal_LandMidMove_StillEndsOnLanding()
    {
        // Double Punch (air key 1) declares LandingLagTicks=9 — must still terminate on
        // landing (pre-existing behavior preserved).
        var (pz, vz, states, grounded) = LandMidAerial(AbilitySlots.Slot1, aerialStartTick: 40, releaseTick: 60, totalTicks: 100);

        int takeoff = FirstTakeoff(grounded);
        int landing = FirstLanding(grounded, takeoff);
        Assert.True(landing > 0, "should land");
        Assert.Equal(ActionState.Idle, states[landing]);
        Assert.Equal(0f, vz[landing]); // landing lag plants the aerial
    }

    /// <summary>All four Kistu air normals (keys 1-4): none may drift after landing.</summary>
    [Fact]
    public void Kistu_AllAirNormals_Land_NoDrift()
    {
        var def = TestHelpers.KistuDef;
        foreach (byte slot in new[] { AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4 })
        {
            var sim = TestHelpers.MakeSim();
            var p = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def), PZ = 0f };
            TestHelpers.RegisterPlayer(sim, def, p);

            var inputs = new Dictionary<ulong, InputState>();
            var vz = new List<float>();
            var grounded = new List<bool>();
            // Short hop, tap the air normal mid-flight, release before landing.
            for (int t = 0; t < 140; t++)
            {
                if (t < 20) inputs[1] = Fwd();
                else if (t == 20) inputs[1] = Fwd(jump: true);
                else if (t < 30) inputs[1] = Fwd();
                else if (t < 35) inputs[1] = Fwd(slot: slot);   // tap air normal
                else inputs[1] = default;
                sim.Tick(inputs);
                var s = sim.GetState(1);
                vz.Add(s.VZ); grounded.Add(s.IsGrounded);
            }

            int takeoff = -1;
            for (int t = 21; t < grounded.Count; t++) if (!grounded[t]) { takeoff = t; break; }
            int landing = -1;
            for (int t = takeoff; t < grounded.Count; t++) if (grounded[t]) { landing = t; break; }
            Assert.True(landing > 0, $"slot {slot}: should land");

            // 40 ticks after landing, drift must be fully braked to zero.
            int check = Math.Min(landing + 40, vz.Count - 1);
            Assert.Equal(0f, vz[check]);
        }
    }

    /// <summary>
    /// Faithful reproduction of the user report: Kistu runs toward the enemy, jumps, whiffs
    /// an air normal past them, crosses up (turn around), lands, and must NOT keep drifting
    /// — with the stick RELEASED after the cross-up, friction must stop it.
    /// </summary>
    [Fact]
    public void Kistu_CrossUpWhiffAirNormal_NoPersistentDrift()
    {
        var def = TestHelpers.KistuDef;
        var sim = TestHelpers.MakeSim();
        var p = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def), PZ = 0f };
        TestHelpers.RegisterPlayer(sim, def, p);
        TestHelpers.RegisterNpc(sim, def, TestHelpers.NpcState(0, 5f) with { PY = TestHelpers.GroundPY(def) });

        var inputs = new Dictionary<ulong, InputState>();
        var pz = new List<float>();
        var vz = new List<float>();
        var grounded = new List<bool>();
        for (int t = 0; t < 200; t++)
        {
            if (t < 20) inputs[1] = Fwd();                             // run toward enemy
            else if (t == 20) inputs[1] = Fwd(jump: true);             // jump
            else if (t < 30) inputs[1] = Fwd();
            else if (t < 34) inputs[1] = Fwd(slot: AbilitySlots.Slot1); // tap air normal (whiff)
            else if (t < 60) inputs[1] = Fwd();                        // keep flying past, cross up
            else inputs[1] = default;                                  // release — must stop
            sim.Tick(inputs);
            var s = sim.GetState(1);
            pz.Add(s.PZ); vz.Add(s.VZ); grounded.Add(s.IsGrounded);
        }

        int takeoff = -1;
        for (int t = 21; t < grounded.Count; t++) if (!grounded[t]) { takeoff = t; break; }
        int landing = -1;
        for (int t = takeoff; t < grounded.Count; t++) if (grounded[t]) { landing = t; break; }
        Assert.True(landing > 0, "should land");
        Assert.True(pz[landing] > 5f, $"should land PAST the enemy (cross-up): pz={pz[landing]:F2}");

        // 40 ticks after landing, fully stopped — no persistent drift.
        int check = Math.Min(landing + 40, vz.Count - 1);
        Assert.Equal(0f, vz[check]);
    }
}
