using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Facing model (ADR-0017 / issue #126) — the unlocked-mode rules the persistent
/// target lock (ADR-0018) overrides: air facing is sticky (no velocity re-facing),
/// ground facing follows movement, and LMB snaps facing to the camera azimuth at the
/// input gate. Implemented as the prerequisite for #127's LMB-exits-lock; the lock
/// tests live in <see cref="TargetLockTests"/>.
/// </summary>
public class FacingSnapTests
{
    private static readonly CharacterDefinition Def = TestHelpers.CombatDef;
    private static float Gpy => TestHelpers.CombatGroundPY;

    [Fact]
    public void AirDrift_DoesNotReface()
    {
        // Sticky air facing: drift must never re-face the fighter mid-air. Assert within
        // the airborne window (the player falls to the ground in ~4 ticks; on the ground
        // facing legitimately follows movement again).
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = 2f, IsGrounded = false, JumpsLeft = 0, FacingYaw = MathF.PI / 4f };
        sim.RegisterEntity(1, Def, player);

        for (int i = 0; i < 3; i++)
            sim.Tick(new() { { 1, new InputState { MoveX = 1f } } }); // drifting +X

        var state = sim.GetState(1);
        Assert.False(state.IsGrounded, "still airborne");
        Assert.True(state.VX > 0.1f, "drift happened");
        TestHelpers.AssertNear(MathF.PI / 4f, state.FacingYaw, 1e-3f);
    }

    [Fact]
    public void GroundMovement_StillFacesVelocity()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);

        sim.Tick(new() { { 1, new InputState { MoveY = 1f } } }); // walk +Z

        var state = sim.GetState(1);
        TestHelpers.AssertNear(0f, state.FacingYaw, 1e-3f); // +Z = yaw 0
    }

    [Fact]
    public void AirSnap_FacesCameraAzimuth()
    {
        // LMB snap works mid-air against sticky facing: facing = camera azimuth instantly.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = 2f, IsGrounded = false, JumpsLeft = 0, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);

        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } });

        var state = sim.GetState(1);
        TestHelpers.AssertNear(MathF.PI, state.FacingYaw, 1e-4f);
    }

    [Fact]
    public void SnapThenNormal_FiresAlongSnappedFacing()
    {
        // The #126 playtest: jump, rotate camera, LMB, press a slot — the normal fires
        // behind the drift. FightGuy's E has an air variant (AirE); Manki's Slot1 has
        // none, so use FightGuy to prove the air attack fires along the snapped facing.
        var fg = TestHelpers.FightGuyDef;
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with
        {
            PY = 2f, IsGrounded = false, JumpsLeft = 0, FacingYaw = 0f,
        };
        sim.RegisterEntity(1, fg, player);

        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } }); // snap → PI (-Z)
        var snapped = sim.GetState(1);
        TestHelpers.AssertNear(MathF.PI, snapped.FacingYaw, 1e-3f);

        sim.Tick(new() { { 1, new InputState { ActiveSlot = AbilitySlots.E } } });          // air normal

        var state = sim.GetState(1);
        Assert.True(state.State is ActionState.Attacking, "attack started");
        Assert.Equal(AbilitySlots.E, state.AttackSlot);
        TestHelpers.AssertNear(MathF.PI, state.FacingYaw, 1e-3f); // snapped facing held through the attack start
    }

    [Fact]
    public void Snap_Rejected_MidAttack()
    {
        // Attack animation lock rejects the snap (the "not attacking" gate); facing stays
        // on the attack's tracking target, not the camera.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ActiveSlot = AbilitySlots.Slot1 } } });
        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } }); // LMB mid-attack

        var state = sim.GetState(1);
        Assert.True(state.AnimLockTicks > 0);
        Assert.True(MathF.Abs(state.FacingYaw) < 1f, $"snap must be rejected mid-attack, facing was {state.FacingYaw}");
    }

    [Fact]
    public void Snap_Rejected_InHitstun()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with
        {
            PY = Gpy, FacingYaw = 0f,
            State = ActionState.Hitstun, HitstunTicks = 10, KVY = 3f,
        };
        sim.RegisterEntity(1, Def, player);

        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } });

        var state = sim.GetState(1);
        Assert.True(MathF.Abs(state.FacingYaw) < 1e-3f, $"snap must be rejected in hitstun, facing was {state.FacingYaw}");
    }

    [Fact]
    public void GroundSnap_IsOneTickTurnaround()
    {
        // Ground snap wins the tick it is pressed (instant turnaround for poke spacing),
        // then the next tick's movement re-faces the walk direction.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = MathF.PI }; // facing -Z
        sim.RegisterEntity(1, Def, player);

        // Walking -Z (facing -Z already) + snap to +Z on the same tick
        sim.Tick(new() { { 1, new InputState { MoveY = -1f, FaceToCamera = true, AimYaw = 0 } } });
        var snapped = sim.GetState(1);
        TestHelpers.AssertNear(0f, snapped.FacingYaw, 1e-4f); // snap wins this tick

        // Next tick, still walking: ground movement re-faces -Z
        sim.Tick(new() { { 1, new InputState { MoveY = -1f } } });
        TestHelpers.AssertNear(MathF.PI, sim.GetState(1).FacingYaw, 1e-3f);
    }
}
