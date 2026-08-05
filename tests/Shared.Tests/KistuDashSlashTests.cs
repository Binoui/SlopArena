using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Kistu E — Directional Dash Slash: hold to aim on the ground (Aiming state: movement
/// unlocked, jump/dash blocked, no aiming anim), release → exact-distance dash toward the
/// cached aim yaw with a per-tick capsule sweep along the path.
/// </summary>
public class KistuDashSlashTests
{
    private static readonly CharacterDefinition Def = TestHelpers.KistuDef;
    private static float Gpy => TestHelpers.GroundPY(Def);

    private static ServerSimulation MakeSim()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        return sim;
    }

    /// <summary>Hold E with an aim direction for the given number of ticks (no slot press).</summary>
    private static void HoldAim(ServerSimulation sim, short aimYaw, int ticks, float moveY = 0f)
    {
        for (int i = 0; i < ticks; i++)
            sim.Tick(new() { { 1, new InputState { IsAiming = true, AimYaw = aimYaw, MoveY = moveY } } });
    }

    [Fact]
    public void E_Press_EntersAiming()
    {
        var sim = MakeSim();
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true } } });
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.Equal((byte)4, s.AttackSlot);
        Assert.True(s.IsAiming);
    }

    [Fact]
    public void E_AimPhase_MovementUnlocked()
    {
        var sim = MakeSim();
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, MoveY = 1f } } });
        HoldAim(sim, 0, 30, moveY: 1f);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.True(s.PZ > 2f, $"expected walk while aiming, got PZ={s.PZ:F3}");
    }

    [Fact]
    public void E_AimPhase_BlocksJumpAndDash()
    {
        var sim = MakeSim();
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true } } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, new InputState { IsAiming = true, Jump = true, Dash = true } } });
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, s.State);    // not JumpSquat / Dashing
        Assert.Equal((byte)2, s.JumpsLeft);           // jump never consumed
        Assert.Equal((ushort)0, s.DashDurationTicks); // dash never started
    }

    [Fact]
    public void E_AimPhase_FacesChosenDirection()
    {
        var sim = MakeSim();
        // Aim 90° (+X) — she should turn to face it while aiming, before the dash starts.
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 9000 } } });
        HoldAim(sim, 9000, 5);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.True(MathF.Abs(s.FacingYaw - MathF.PI / 2f) < 0.01f,
            $"expected facing toward aim (+X, PI/2), got FacingYaw={s.FacingYaw:F3}");
    }

    [Fact]
    public void E_Release_DashesExactDistance_TowardAimYaw()
    {
        var sim = MakeSim();
        // Aim 90° → dash along +X (AimYaw is degrees × 100).
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 9000 } } });
        HoldAim(sim, 9000, 10);
        TestHelpers.TickN(sim, new InputState { IsAiming = false, AimYaw = 9000 }, 40);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.True(MathF.Abs(s.PX - 5f) < 0.05f, $"expected 5 m dash along +X, got PX={s.PX:F3}");
        Assert.True(MathF.Abs(s.PZ) < 0.05f, $"expected no Z movement, got PZ={s.PZ:F3}");
        Assert.True(s.Cooldown3 > 0, "E cooldown should be applied after the dash");
    }

    [Fact]
    public void E_Release_UsesCachedAim_NotReleaseFrameYaw()
    {
        // The client sends camera yaw (not the mouse aim) on the release frame; the server
        // must dash toward the last aimed direction, not the release input's AimYaw.
        var sim = MakeSim();
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 9000 } } });
        HoldAim(sim, 9000, 10);
        TestHelpers.TickN(sim, new InputState { IsAiming = false, AimYaw = 0 }, 40); // release with camera yaw 0
        var s = sim.GetState(1);
        Assert.True(MathF.Abs(s.PX - 5f) < 0.05f, $"dash must follow cached aim (+X), got PX={s.PX:F3} PZ={s.PZ:F3}");
        Assert.True(MathF.Abs(s.PZ) < 0.05f, $"dash must follow cached aim (+X), got PZ={s.PZ:F3}");
    }

    [Fact]
    public void E_TapAndHold_SameDistance()
    {
        // No charge dimension: a tap and a long hold cover the same set distance.
        var tap = MakeSim();
        tap.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 0 } } });
        tap.Tick(new() { { 1, new InputState { IsAiming = false, AimYaw = 0 } } }); // release next tick
        TestHelpers.TickN(tap, new InputState { IsAiming = false }, 40);

        var hold = MakeSim();
        hold.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 0 } } });
        HoldAim(hold, 0, 100);
        hold.Tick(new() { { 1, new InputState { IsAiming = false, AimYaw = 0 } } });
        TestHelpers.TickN(hold, new InputState { IsAiming = false }, 40);

        var tapState = tap.GetState(1);
        var holdState = hold.GetState(1);
        Assert.True(MathF.Abs(tapState.PZ - 5f) < 0.05f, $"tap must dash 5 m, got PZ={tapState.PZ:F3}");
        Assert.True(MathF.Abs(tapState.PZ - holdState.PZ) < 0.01f,
            $"tap and hold must cover the same distance: tap PZ={tapState.PZ:F3}, hold PZ={holdState.PZ:F3}");
    }

    [Fact]
    public void E_AutoRelease_AtMaxAimTicks()
    {
        var sim = MakeSim();
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 0 } } });
        // Hold past max_aim_ticks (180) — the dash fires automatically.
        HoldAim(sim, 0, 200);
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State); // aim capped → dash → done
        Assert.True(MathF.Abs(s.PZ - 5f) < 0.05f, $"auto-release should dash 5 m, got PZ={s.PZ:F3}");
    }
}
