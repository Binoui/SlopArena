using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MankiKitTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef!;
    private static readonly CharacterDefinition CombatDef = TestHelpers.CombatDef;
    private static readonly float Gpy = TestHelpers.MankiGroundPY;

    // ══════════════════════════════════════════════════════════════════
    //  BAZOOKA (R, slot 4, activeSlot=5)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bazooka_FiresProjectile_AfterTriggerTick()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = Gpy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Activate R with aim held
        var aimInput = new InputState { ActiveSlot = 5, IsAiming = true };
        sim.Tick(new() { { 1, aimInput } });

        // Hold aim for a few ticks
        var holdInput = new InputState { IsAiming = true };
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, holdInput } });

        // Release → transitions to Firing, projectile spawns at trigger_tick=6
        var releaseInput = new InputState { IsAiming = false };
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, releaseInput } });

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var rocket = hitboxes[0];
        Assert.True(rocket.Gravity > 0, "Rocket should have gravity");
        Assert.True(rocket.Explosion.HasValue, "Rocket should have explosion config");
        Assert.Equal((ulong)1, rocket.OwnerId);
    }

    [Fact]
    public void Bazooka_AimContinuesAfterAirLanding()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = Gpy + 0.4f;
        state.IsGrounded = false;
        state.VY = -5f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var holdInput = new InputState { ActiveSlot = 5, IsAiming = true };
        sim.Tick(new() { { 1, holdInput } });
        holdInput.ActiveSlot = 0;
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, holdInput } });

        var aimed = sim.GetState(1);
        Assert.True(aimed.IsGrounded);
        Assert.Equal(ActionState.Aiming, aimed.State);
        Assert.Equal((byte)5, aimed.AttackSlot);
        Assert.True(aimed.IsAiming);

        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, new InputState { IsAiming = false } } });

        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void Bazooka_RocketJump_SelfDamageCapped()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState();
        state.PY = Gpy;
        TestHelpers.RegisterPlayer(sim, CombatDef, state);

        // Activate R with aim held, steep downward pitch
        var aimInput = new InputState { ActiveSlot = 5, IsAiming = true, AimPitch = (short)(-8500) };
        sim.Tick(new() { { 1, aimInput } });

        // Hold aim
        var holdInput = new InputState { IsAiming = true, AimPitch = (short)(-8500) };
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, holdInput } });
        var releaseInput = new InputState { IsAiming = false, AimPitch = (short)(-8500) };
        for (int i = 0; i < 30; i++)
            sim.Tick(new() { { 1, releaseInput } });

        var after = sim.GetState(1);
        Assert.True(after.DamagePercent > 0,
            $"Expected self-damage from rocket jump, got {after.DamagePercent}");
    }

    // ══════════════════════════════════════════════════════════════════
    //  JETPACK BOOST (E, slot 3, activeSlot=4)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Jetpack_IgnitionReplacesDownwardVelocityWithVerticalLaunch()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with
        {
            PY = Gpy + 2f,
            IsGrounded = false,
            VY = -8f,
            AirTimeTicks = Def.Movement.FloatWindowTicks,
        };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 0f, moveY: 0f);

        var after = sim.GetState(1);
        Assert.Equal(15f, after.VY);
        Assert.False(after.IsGrounded);
        Assert.Equal(Def.Movement.FloatWindowTicks, after.AirTimeTicks);
    }

    [Fact]
    public void Jetpack_DiagonalInputNormalizesToHorizontalSpeedCap()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 1f, moveY: 1f);

        var after = sim.GetState(1);
        var expected = 3.5f / MathF.Sqrt(2f);
        Assert.Equal(expected, after.VX, 3);
        Assert.Equal(expected, after.VZ, 3);
        Assert.Equal(3.5f, MathF.Sqrt(after.VX * after.VX + after.VZ * after.VZ), 3);
    }

    [Fact]
    public void Jetpack_NeutralInputLaunchesVertically()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 0f, moveY: 0f);

        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
        Assert.Equal(15f, after.VY);
    }

    [Fact]
    public void Jetpack_AscentIgnoresOppositeStickAndResumesAirDriftAfterApex()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 1f, moveY: 0f);
        var launch = sim.GetState(1);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, new InputState { MoveX = -1f } } });
        Assert.Equal(launch.VX, sim.GetState(1).VX);

        for (int i = 0; i < 100 && sim.GetActiveAbility(1) != null; i++)
            sim.Tick(new() { { 1, default } });

        var apex = sim.GetState(1);
        Assert.Equal(ActionState.Idle, apex.State);
        Assert.Equal((byte)0, apex.AttackSlot);
        float beforeDrift = apex.VX;
        sim.Tick(new() { { 1, new InputState { MoveX = -1f } } });
        Assert.NotEqual(beforeDrift, sim.GetState(1).VX);
    }

    [Fact]
    public void Jetpack_IgnitionHitboxDamagesAndLaunchesNearbyNpc()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = TestHelpers.CombatGroundPY };
        var npc = TestHelpers.NpcState(z: 0.75f) with { PY = TestHelpers.CombatGroundPY };
        sim.RegisterEntity(1, CombatDef, player);
        sim.RegisterEntity(100, CombatDef, npc);

        TickJetpack(sim, 3, moveX: 0f, moveY: 0f);
        for (int i = 0; i < 8; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.Equal((ushort)4, npcAfter.DamagePercent);
        Assert.True(npcAfter.KVY > 0f, $"NPC should launch upward, got {npcAfter.KVY}");
    }

    [Fact]
    public void Jetpack_ApexCompletionStartsCooldownAndRejectsUntilReady()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 0f, moveY: 0f);
        for (int i = 0; i < 100 && sim.GetActiveAbility(1) != null; i++)
            sim.Tick(new() { { 1, default } });

        var completed = sim.GetState(1);
        Assert.Equal((ushort)210, completed.Cooldown3);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.E) } });
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Equal((byte)0, sim.GetState(1).AttackSlot);

        for (int i = 0; i < 210; i++)
            sim.Tick(new() { { 1, default } });
        Assert.Equal((ushort)0, sim.GetState(1).Cooldown3);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.E) } });
        Assert.Equal((byte)AbilitySlots.E, sim.GetState(1).AttackSlot);
    }

    private static void TickJetpack(ServerSimulation sim, int ticks, float moveX, float moveY)
    {
        for (int i = 0; i < ticks; i++)
        {
            var input = new InputState { MoveX = moveX, MoveY = moveY };
            if (i == 0) input.ActiveSlot = AbilitySlots.E;
            sim.Tick(new() { { 1, input } });
        }
    }
    // ══════════════════════════════════════════════════════════════════
    //  AIR LMB (slot 0 airborne, activeSlot=1)
    // ══════════════════════════════════════════════════════════════════


    // ══════════════════════════════════════════════════════════════════
    //  RETIRED RMB (activeSlot=2) — target-lock toggle, no attack
    //  ADR-0021/0018: AbilityFactory never dispatches slot 1 (RMB is the
    //  lock toggle). An RMB attack press must be a no-op, not an attack —
    //  pins the contract so retired RMB tests don't silently resurrect.
    //  ══════════════════════════════════════════════════════════════════

    [Fact]
    public void RMB_AttackSlot_IsRetired_NeverDispatches()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = Gpy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // RMB attack press + follow-up ticks: nothing may start an attack.
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 2 } } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default } });

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State); // never entered an attack
        Assert.Equal((byte)0, s.AttackSlot);
        Assert.Equal((ushort)0, s.DamagePercent);
    }
}
