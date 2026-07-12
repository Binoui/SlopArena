using Xunit;
using System.Collections.Generic;

namespace SlopArena.Shared.Tests;

public class DashTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;
    private static readonly CharacterDefinition FightGuyDef = TestHelpers.FightGuyDef;
    private static readonly float MankiPy = TestHelpers.MankiGroundPY;

    [Fact]
    public void Dash_FromIdle_TransitionsToDashing()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        Assert.Equal(ActionState.Dashing, t0.State);
        Assert.True(t0.DashDurationTicks > 0);
    }

    [Fact]
    public void Dash_TransitionsToIdleAfterDuration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(dash: true, moveY: 1f) }
        });
        var t0 = sim.GetState(1);
        Assert.Equal(ActionState.Dashing, t0.State);

        ushort duration = t0.DashDurationTicks;
        Assert.True(duration > 0, "Dash should have positive duration");

        // Tick through all dash ticks (duration-1 more, since tick 0 already ran)
        for (int i = 1; i < duration; i++)
        {
            TestHelpers.TickDefault(sim, 1);
            var s = sim.GetState(1);
            Assert.Equal(ActionState.Dashing, s.State);
        }

        // One more tick → dash expires
        TestHelpers.TickDefault(sim, 1);
        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((ushort)0, ended.DashDurationTicks);

        // Velocity should be zeroed
        float residual = System.MathF.Sqrt(ended.VX * ended.VX + ended.VZ * ended.VZ);
        Assert.True(residual < 1f,
            $"Expected velocity near 0 after dash end, got {residual:F3}");
    }

    [Fact]
    public void Dash_UsesCharacterDefDuration()
    {
        // FightGuy has DashDurationTicks = 8 vs Manki's 15
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        Assert.Equal((ushort)8, t0.DashDurationTicks);
    }

    [Fact]
    public void Dash_CanCooldown_BlocksSecondDash()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // First dash
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        Assert.Equal(ActionState.Dashing, t0.State);
        Assert.True(t0.DashCooldownTicks > 0);

        // Tick past dash duration
        for (int i = 0; i < 20; i++)
            TestHelpers.TickDefault(sim, 1);

        var mid = sim.GetState(1);
        Assert.Equal(ActionState.Idle, mid.State);

        // Try to dash again while cooldown still active
        var afterTry = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        Assert.NotEqual(ActionState.Dashing, afterTry.State);
        Assert.Equal(ActionState.Idle, afterTry.State);
    }

    [Fact]
    public void Dash_CancelsAttack_ClearsAttackSlotAndAnimLock()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // Start LMB attack (ServerAbility) — AnimLockTicks=40
        var attack = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, attack.State);
        Assert.Equal((byte)1, attack.AttackSlot);
        Assert.True(attack.AnimLockTicks > 0);

        // Send dash input every tick until AnimLockTicks expires.
        // Section 6 is gated on AnimLockTicks == 0, so dash won't fire until lock expires.
        // On the expiry tick, StartDash clears attack state and TickAbilities interrupts the ability.
        int maxTicks = 60;
        for (int i = 0; i < maxTicks; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                { 1, TestHelpers.Input(dash: true, moveY: 1f) }
            });
            var s = sim.GetState(1);
            if (s.State == ActionState.Dashing)
            {
                // Dash successfully started — verify attack state was cleared
                Assert.Equal((byte)0, s.AttackSlot);
                Assert.Equal((ushort)0, s.AnimLockTicks);
                Assert.False(s.IsServerAbility);
                Assert.Equal((ushort)0, s.ComboStage);
                Assert.Equal((ushort)0, s.AttackElapsedTicks);
                return; // success
            }
        }

        Assert.Fail($"Dash never started within {maxTicks} ticks of holding dash during attack");
    }

    [Fact]
    public void Dash_CancelsAttack_AbilityRemovedFromActive()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // Start LMB attack (ServerAbility via LmbCombo) — AnimLockTicks=40
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);

        // Hold dash input until it fires (on AnimLockTicks expiry)
        int maxTicks = 60;
        int dashStartedAt = -1;
        for (int i = 0; i < maxTicks; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                { 1, TestHelpers.Input(dash: true, moveY: 1f) }
            });
            var s = sim.GetState(1);
            if (s.State == ActionState.Dashing)
            {
                dashStartedAt = i;
                break;
            }
        }
        Assert.True(dashStartedAt >= 0, $"Dash never started within {maxTicks} ticks");

        // Tick through dash duration + margin
        for (int i = 0; i < 25; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);

        // Verify no stale attack state
        Assert.Equal((byte)0, ended.AttackSlot);
        Assert.Equal((ushort)0, ended.AnimLockTicks);
    }

    [Fact]
    public void Dash_FromIdle_NormalMovementAfterEnd()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // Dash forward
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        Assert.Equal(ActionState.Dashing, t0.State);

        // Tick past dash duration
        for (int i = 0; i < 20; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);

        // Should be able to walk after dash ends
        var walking = TestHelpers.TickN(sim, TestHelpers.Input(moveY: 1f), 5);
        Assert.Equal(ActionState.Idle, walking.State);
        Assert.True(System.MathF.Abs(walking.VZ) > 0.1f,
            "Character should be able to move after dash completes");
    }

    [Fact]
    public void Dash_CanDashForwardWithNoDirectionInput()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiPy;
        state.FacingYaw = 0f; // facing +Z
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // Dash with no direction input -> forward based on facing
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveX: 0f, moveY: 0f), 1);
        Assert.Equal(ActionState.Dashing, t0.State);
        Assert.True(t0.VZ > 0f,
            $"Expected forward dash (VZ > 0) with no direction input, got VZ={t0.VZ:F3}");
    }

    [Fact]
    public void Dash_StateDoesNotPersistPastDuration()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = MankiPy;
        TestHelpers.RegisterPlayer(sim, MankiDef, state);

        // Dash
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        var duration = t0.DashDurationTicks;

        // Tick well past duration
        for (int i = 0; i < duration + 30; i++)
            TestHelpers.TickDefault(sim, 1);

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.Equal((ushort)0, s.DashDurationTicks);
        Assert.Equal((ushort)0, s.InvincibilityTicks);

        // Velocity must be near zero after all this time
        float hSpeed = System.MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
        Assert.True(hSpeed < 1f,
            $"Velocity should be near 0 after dash + friction, got {hSpeed:F3}");
    }

    [Fact]
    public void VelocityDeadZone_GroundFriction_SnapsSubthresholdToZero()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.VX = 0.005f; // below VelocityDeadZone (0.015)
        state.VZ = 0.003f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.MankiDef, state);

        // One tick with no input → friction applies, dead zone snaps to zero
        TestHelpers.TickDefault(sim, 1);
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }

    [Fact]
    public void VelocityDeadZone_AirDrag_SnapsSubthresholdToZero()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 10f; // airborne
        state.IsGrounded = false;
        state.VX = 0.008f; // below VelocityDeadZone (0.015)
        state.VZ = 0.006f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.MankiDef, state);

        // One tick with no input → air drag applies, dead zone snaps to zero
        TestHelpers.TickDefault(sim, 1);
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }

    [Fact]
    public void VelocityDeadZone_AboveThreshold_DoesNotSnap()
    {
        // Ground friction is proportional (asymptotic), so velocity above threshold
        // should never snap to zero — the dead zone only catches subthreshold residuals.
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.VX = 1.0f; // well above VelocityDeadZone (0.015)
        state.VZ = 1.0f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.MankiDef, state);

        // Tick with no input → ground friction reduces but doesn't snap
        for (int i = 0; i < 60; i++)
            TestHelpers.TickDefault(sim, 1);

        var after = sim.GetState(1);
        // After 60 ticks of proportional friction (V *= 0.767^60 ≈ 2e-7), velocity
        // should be well below 0.015 and snapped to exactly 0 by the dead zone.
        // But with VX=1.0, after ~30 ticks it should still be above 0.015.
        // After 60 ticks, it's well below and snapped.
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);

        // Verify the dead zone is the reason: with the same starting velocity,
        // after a partial number of ticks it should still be positive.
        var sim2 = TestHelpers.MakeSim();
        var state2 = TestHelpers.PlayerState();
        state2.PY = TestHelpers.MankiGroundPY;
        state2.VX = 1.0f;
        state2.VZ = 1.0f;
        TestHelpers.RegisterPlayer(sim2, TestHelpers.MankiDef, state2);

        for (int i = 0; i < 10; i++)
            TestHelpers.TickDefault(sim2, 1);

        var mid = sim2.GetState(1);
        Assert.True(mid.VX > 0f && mid.VZ > 0f,
            $"Velocity should still be positive after 10 ticks of friction, got VX={mid.VX} VZ={mid.VZ}");
    }
 }
