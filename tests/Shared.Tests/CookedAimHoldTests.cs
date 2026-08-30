using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Hold-to-aim, release-to-fire contract for cooked AimedProjectile slots
/// (Manki Q Round Bomb, FightGuy Q Ki Shot): the projectile must NOT spawn
/// while the key is held — the hold is an aim phase — and fires only after
/// release, using the aim captured at release. The stage clock is frozen
/// during the hold so long aims never hit an authored stage timeout.
/// </summary>
public class CookedAimHoldTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef!;
    private static readonly CharacterDefinition FightGuyDef = TestHelpers.FightGuyDef!;

    private static ServerSimulation MakeSim(CharacterDefinition def)
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        TestHelpers.RegisterPlayer(sim, def, state);
        return sim;
    }

    private static InputState HoldInput(bool aiming, short aimYaw = 0, short aimPitch = 0, ushort aimDistance = 0)
        => new() { IsAiming = aiming, AimYaw = aimYaw, AimPitch = aimPitch, AimDistance = aimDistance };

    // ── Manki Q — Round Bomb (groundCursor, fixed aim) ──

    [Fact]
    public void Manki_Q_HoldsWhileAiming_NoProjectileDuringHold()
    {
        var sim = MakeSim(MankiDef);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimDistance = 400 } },
        });

        for (int i = 0; i < 30; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimDistance: 400) } });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }

        // Release → throw phase spawns the bomb at throw_trigger_tick (10) after release.
        for (int i = 0; i < 15; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void Manki_Q_LongHold_DoesNotCancel_ReleaseStillFires()
    {
        var sim = MakeSim(MankiDef);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimDistance = 400 } },
        });

        // Hold past the authored stage duration (600 ticks) — the stage clock must
        // freeze during the aim hold (IAimHoldCapability), not cancel the ability.
        for (int i = 0; i < 700; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimDistance: 400) } });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }

        for (int i = 0; i < 15; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }


    [Fact]
    public void Manki_Q_ThrowsAtCursorDistance_WhenReleaseInputZeroesAim()
    {
        var sim = MakeSim(MankiDef);

        // Press Q with the cursor 4 m ahead (AimDistance = 400 cm), hold, then
        // release with a zeroed aim input — mirrors the client's release context
        // (it forwards the last yaw but no distance). The throw must use the LAST
        // HELD distance, not the zeroed release tick.
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimDistance = 400 } },
        });
        for (int i = 0; i < 20; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimDistance: 400) } });
        for (int i = 0; i < 20; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes());
        // 4 m lob at 30° (hSpeed ≈ 7.4) vs the 0.5 m clamp the bug produced (hSpeed ≈ 1.3).
        float hSpeed = MathF.Sqrt(bomb.VX * bomb.VX + bomb.VZ * bomb.VZ);
        Assert.True(hSpeed > 5f, $"expected a 4 m lob, got hSpeed={hSpeed}");
    }

    [Fact]
    public void Manki_Q_ThrowsAlongCursorYaw_NotPressOrZeroedYaw()
    {
        var sim = MakeSim(MankiDef);

        // Aim 90° to the side while held; the release input carries yaw = 0.
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 9000, AimDistance = 400 } },
        });
        for (int i = 0; i < 20; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimYaw: 9000, aimDistance: 400) } });
        for (int i = 0; i < 20; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.True(bomb.VX > 5f, $"expected a sideways launch, got VX={bomb.VX} VZ={bomb.VZ}");
        Assert.True(MathF.Abs(bomb.VZ) < 1f, $"expected no forward velocity, got VX={bomb.VX} VZ={bomb.VZ}");
    }


    [Fact]
    public void AimedSlots_ExposeAuthoredAimLoop()
    {
        // The cooked adapters surface the authored aim-loop id so the renderer can
        // play it during the hold (and the timeline anim on release).
        Assert.Equal("anim.ki-shot-loop", FightGuyDef.GetSlotAbility(10, airborne: false)!.AimAnimationId);
        Assert.Equal("anim.manki.ga-loop", MankiDef.GetSlotAbility(10, airborne: false)!.AimAnimationId);
    }

    [Fact]
    public void FightGuy_Q_HoldsWhileAiming_NoProjectileDuringHold()
    {
        var sim = MakeSim(FightGuyDef);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimPitch = 0 } },
        });

        for (int i = 0; i < 30; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true) } });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }

        // Release → fire phase spawns at startup_ticks (8) after release.
        for (int i = 0; i < 10; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void FightGuy_Q_LongHold_DoesNotCancel_ReleaseStillFires()
    {
        var sim = MakeSim(FightGuyDef);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimPitch = 0 } },
        });

        // The Ki Shot stage is only 24 ticks — without the hold freeze the ability
        // would complete mid-hold and never fire.
        for (int i = 0; i < 700; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true) } });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }

        for (int i = 0; i < 10; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void FightGuy_Q_FiresAtReleaseAim_NotPressAim()
    {
        var sim = MakeSim(FightGuyDef);

        // Press facing forward (AimYaw = 0°).
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimPitch = 0 } },
        });

        // Hold and re-aim 90° to the side before releasing.
        for (int i = 0; i < 10; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true) } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimYaw: 9000) } });

        for (int i = 0; i < 10; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false, aimYaw: 9000) } });

        var shot = Assert.Single(sim.Resolver.GetActiveHitboxes());
        // 25 speed at 90° yaw → VX ≈ 25, VZ ≈ 0. Press-time aim (0°) would give VX ≈ 0, VZ ≈ 25.
        Assert.True(shot.VX > 10f, $"Expected sideways launch, got VX={shot.VX} VZ={shot.VZ}");
        Assert.True(MathF.Abs(shot.VZ) < 1f, $"Expected no forward velocity, got VX={shot.VX} VZ={shot.VZ}");
    }
}
