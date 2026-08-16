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

        // Grounded dash hard-stops on expiry (wavedash) — velocity is zeroed.
        float residual = System.MathF.Sqrt(ended.VX * ended.VX + ended.VZ * ended.VZ);
        Assert.True(residual < 0.001f,
            $"Expected hard stop after grounded dash end, got {residual:F3}");
    }

    [Fact]
    public void Dash_UsesCharacterDefDuration()
    {
        // FightGuy has DashDurationTicks = 10 vs Manki's 15
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        Assert.Equal((ushort)10, t0.DashDurationTicks);
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
        Assert.Equal(ActionState.Run, afterTry.State);
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

        // Should be able to run after dash ends (the dash coasts into Run)
        var walking = TestHelpers.TickHold(sim, TestHelpers.Input(moveY: 1f), 5);
        Assert.Equal(ActionState.Run, walking.State);
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

        // Grounded dash hard-stops (wavedash) — velocity zeroed, no coast.
        float hSpeed = System.MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
        Assert.True(hSpeed < 0.001f,
            $"Grounded dash should hard-stop after it ends, got {hSpeed:F3}");
    }

    [Fact]
    public void Dash_IframesExpireBeforeDashEnds()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = TestHelpers.GroundPY(FightGuyDef);
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(dash: true, moveY: 1f), 1);
        Assert.True(t0.InvincibilityTicks > 0, "dash should open with i-frames");

        // i-frames (4 ticks) close before the dash (10 ticks) ends: the tail is vulnerable,
        // so dodging through an attack is timing-tight (ADR-0020 v2).
        for (int i = 0; i < 20 && sim.GetState(1).InvincibilityTicks > 0; i++)
            TestHelpers.TickDefault(sim, 1);
        var s = sim.GetState(1);
        Assert.Equal((ushort)0, s.InvincibilityTicks);
        Assert.Equal(ActionState.Dashing, s.State);
    }

    [Fact]
    public void AerialDash_HardStopsAtExpiry()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(50f, 50f);
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, FightGuyDef, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(dash: true, moveX: 1f) } });

        // Tick past the dash duration — still airborne, but the burst hard-stops on the
        // expiry frame (same wavedash contract as grounded): the air dash is a clean
        // 0.25s horizontal dodge, not a momentum boost that sails.
        for (int i = 0; i < 12; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default(InputState) } });
        var s = sim.GetState(1);
        Assert.False(s.IsGrounded, "should still be airborne");
        float residual = System.MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
        Assert.True(residual < 0.001f,
            $"aerial dash should hard-stop at expiry (wavedash), got residual velocity {residual:F3}");
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

        // One tick with no input → linear air friction applies, dead zone snaps to zero
        TestHelpers.TickDefault(sim, 1);
        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }

    [Fact]
    public void VelocityDeadZone_AboveThreshold_DoesNotSnap()
    {
        // Ground friction (release brake) decays a fixed m/s² per tick, so an above-threshold
        // velocity decays toward zero and reaches it exactly (no asymptotic tail, no instant snap).
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.VX = 1.0f; // well above VelocityDeadZone (0.015)
        state.VZ = 1.0f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.MankiDef, state);

        // One tick of friction has not yet zeroed it.
        TestHelpers.TickDefault(sim, 1);
        var mid = sim.GetState(1);
        Assert.True(mid.VX > 0f && mid.VZ > 0f,
            $"Velocity should still be positive after 1 tick of friction, got VX={mid.VX} VZ={mid.VZ}");

        // After enough ticks, linear friction drives it to exactly zero.
        for (int i = 0; i < 57; i++)
            TestHelpers.TickDefault(sim, 1);

        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
    }
 }
