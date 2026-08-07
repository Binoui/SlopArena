using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════
/// COMBO INFLUENCE TESTS (ADR-0013, issue #100)
/// ═══════════════════════════════════════════════════════════════════════
///
/// Verifies launch-scaled vectoring (replaces the flat +3.5 m/s DI):
///   - A committed held direction adds 0.30 × original launch magnitude
///     to horizontal velocity, applied once at hitstun expiry.
///   - Capture is commit + latest-wins: a nonzero input commits the full
///     vector; releasing to neutral preserves the committed direction;
///     a later nonzero hold overwrites.
///   - Vertical (KVY) is untouched — the drift is horizontal-only.
///
/// Tick accounting: a zone hitbox spawned pre-tick resolves at the END of
/// tick 1 (ResolveHits runs after SimulateMovement), freezing the victim
/// F = 2 + 2·damage(4) = 10 ticks, decremented ticks 2..11. The launch
/// applies at the end of tick 11: KVY = 14·sin45° ≈ 9.899, KVX = KVZ = 0,
/// LaunchMagnitude = 14. HitstunTicks = 12 — the freeze-expiry gate forces
/// it to QueuedKBStun (= the hitbox StunTicks, see Simulation.cs line 186),
/// so the zone's StunTicks must BE 12 for the 12-tick hitstun the scenario
/// intends (the ADR-0013 design doc's 60 was the pre-override cap).
/// Hitstun decrements ticks 12..23, so the drift lands at the END of tick
/// 23 — the first tick whose VX carries the influence (design-doc tick 26
/// was an off-by-three slip; the doc's own formula KVY 9.899 → 6.91 over
/// 12 ticks and "post-expiry VX ≈ 4.42" describe tick 23 exactly).
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public class ComboInfluenceTests
{
    /// <summary>Freeze = 2 + 2·damage(4) = 10 ticks, decremented ticks 2..11.</summary>
    private const int LaunchTick = 11;
    /// <summary>Hitstun = 12 ticks (hitbox StunTicks), decremented ticks 12..23.</summary>
    private const int ExpiryTick = 23;
    /// <summary>Launch magnitude = base 14 + growth 0·(damage%·0.01) = 14.</summary>
    private const float LaunchMag = 14f;
    /// <summary>Drift band: 0.25·launch to 0.35·launch around 0.30·launch = 4.2.</summary>
    private static readonly (float Lo, float Hi) DriftBand = (0.25f * LaunchMag, 0.35f * LaunchMag);

    /// <summary>
    /// Zone-launch scenario: NPC at (0, ground, 2.2), zone hitbox spawned pre-tick
    /// CENTERED on the NPC. The resolver computes direction hitbox→entity center,
    /// so a centered zone yields direction (0,0) → pure vertical launch — the drift
    /// axis is deliberately orthogonal to the launch direction (ADR's core property).
    /// </summary>
    private static ServerSimulation ZoneScenario()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var def = TestHelpers.CombatDef;
        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(100, def, npc);
        sim.Resolver.Spawn(new Hitbox
        {
            X = 0f, Y = TestHelpers.CombatGroundPY, Z = 2.2f,
            Radius = 1.2f, Damage = 4f, DurationTicks = 1,
            OwnerId = 0, KnockbackAngle = 45, BaseKnockback = LaunchMag,
            KnockbackGrowth = 0f, StunTicks = 12,
        });
        return sim;
    }

    [Fact]
    public void HeldDirection_AddsAboutThirtyPercentOfLaunch()
    {
        var simA = ZoneScenario();
        var simB = ZoneScenario();
        // Tick 1: the zone resolves at the END of this tick — freeze queued, no input yet
        // (holding on tick 1 would add a pre-hit walk tick to the physics).
        simA.Tick(new() { { 100, default } });
        simB.Tick(new() { { 100, default } });

        var held = TestHelpers.Input(moveX: 1f);
        // Freeze ticks 2..11: commit the direction inside the decision window.
        for (int t = 2; t <= LaunchTick; t++)
        {
            simA.Tick(new() { { 100, held } });
            simB.Tick(new() { { 100, default } });
        }

        // Precondition: launch applied at end of tick 11 with the raw magnitude captured.
        var atLaunch = simA.GetState(100);
        Assert.Equal(LaunchMag, atLaunch.LaunchMagnitude);
        Assert.Equal(0f, atLaunch.KVX);                       // centered zone → pure vertical
        Assert.Equal((ushort)12, atLaunch.HitstunTicks);

        // Hitstun ticks 12..23: keep holding; drift lands at end of tick 23.
        for (int t = LaunchTick + 1; t <= ExpiryTick; t++)
        {
            simA.Tick(new() { { 100, held } });
            simB.Tick(new() { { 100, default } });
        }

        var a = simA.GetState(100);
        var b = simB.GetState(100);
        float dVX = a.VX - b.VX;
        Assert.InRange(dVX, DriftBand.Lo, DriftBand.Hi);
        Assert.Equal(0f, a.VY - b.VY);                        // vertical untouched
        Assert.Equal(0f, a.VZ - b.VZ);                        // no cross-axis drift
    }

    [Fact]
    public void ZeroInput_ChangesNothing()
    {
        var sim = ZoneScenario();
        sim.Tick(new() { { 100, default } });
        for (int t = 2; t <= ExpiryTick; t++)
            sim.Tick(new() { { 100, default } });

        var s = sim.GetState(100);
        Assert.Equal(0f, s.VX);                               // no drift into a zero-remnant axis
        Assert.True(s.VY > 0f, "vertical launch intact");     // KVY ≈ 6.9 after 12 hitstun ticks
    }

    [Fact]
    public void FreezeOnlyInput_StillCounts()
    {
        var simC = ZoneScenario();
        var simB = ZoneScenario();
        simC.Tick(new() { { 100, default } });
        simB.Tick(new() { { 100, default } });

        var held = TestHelpers.Input(moveX: 1f);
        // Freeze is the decision window: commit, then release for the whole hitstun.
        for (int t = 2; t <= LaunchTick; t++)
        {
            simC.Tick(new() { { 100, held } });
            simB.Tick(new() { { 100, default } });
        }
        for (int t = LaunchTick + 1; t <= ExpiryTick; t++)
        {
            simC.Tick(new() { { 100, default } });            // neutral — must NOT cancel the pick
            simB.Tick(new() { { 100, default } });
        }

        var c = simC.GetState(100);
        var b = simB.GetState(100);
        Assert.InRange(c.VX - b.VX, DriftBand.Lo, DriftBand.Hi);
    }

    [Fact]
    public void LatestNonzeroInput_Wins()
    {
        var simD = ZoneScenario();
        var simB = ZoneScenario();
        simD.Tick(new() { { 100, default } });
        simB.Tick(new() { { 100, default } });

        var held = TestHelpers.Input(moveX: 1f);
        for (int t = 2; t <= LaunchTick; t++)
        {
            simD.Tick(new() { { 100, held } });
            simB.Tick(new() { { 100, default } });
        }
        var flipped = TestHelpers.Input(moveX: -1f);
        // Latest-wins: the hold flips during hitstun and overwrites the freeze commit.
        for (int t = LaunchTick + 1; t <= ExpiryTick; t++)
        {
            simD.Tick(new() { { 100, flipped } });
            simB.Tick(new() { { 100, default } });
        }

        var d = simD.GetState(100);
        var b = simB.GetState(100);
        Assert.InRange(d.VX - b.VX, -DriftBand.Hi, -DriftBand.Lo);
    }

    [Fact]
    public void OverrideLaunch_CapturesSnapshotMagnitude()
    {
        // Synthetic NetherGrasp-style path: OnHitEntity rewrote the launch at connect,
        // so the freeze-expiry gate restores the QueuedKVX/Y/Z snapshot (magnitude
        // sqrt(3²+4²) = 5) instead of recomputing from raw hitbox params.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var def = TestHelpers.CombatDef;
        var player = TestHelpers.PlayerState();
        player.HitstopTicks = 1;
        player.QueuedKVOverride = true;
        player.QueuedKVX = 3f; player.QueuedKVY = 4f; player.QueuedKVZ = 0f;
        player.QueuedKBStun = 10;
        sim.RegisterEntity(1, def, player);

        sim.Tick(new() { { 1, default } });

        var s = sim.GetState(1);
        Assert.Equal(5f, s.LaunchMagnitude);                  // snapshot magnitude captured
        Assert.Equal(3f, s.KVX);
        Assert.Equal(4f, s.KVY);
        Assert.Equal((ushort)10, s.HitstunTicks);
    }
}
