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
    //  GRAPPLE GUN (E, slot 3, activeSlot=4)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Grapple_FiresTether_AfterTriggerTick()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = Gpy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Activate E with aim held
        var aimInput = new InputState { ActiveSlot = 4, IsAiming = true };
        sim.Tick(new() { { 1, aimInput } });

        // Hold aim
        var holdInput = new InputState { IsAiming = true };
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, holdInput } });

        // Release → Firing, tether spawns at trigger_tick=8
        var releaseInput = new InputState { IsAiming = false };
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, releaseInput } });

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var tether = hitboxes[0];
        Assert.Equal(0f, tether.Damage);
        Assert.Equal((ulong)1, tether.OwnerId);
    }

    [Fact]
    public void Grapple_EntityHit_ReelsTowardTarget()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = Gpy;
        player.AimYaw = 0f;
        sim.RegisterEntity(1, CombatDef, player);

        var npc = TestHelpers.NpcState(z: 5f);
        npc.PY = Gpy;
        sim.RegisterEntity(100, CombatDef, npc);

        // Activate E with aim held
        var aimInput = new InputState { ActiveSlot = 4, IsAiming = true };
        sim.Tick(new() { { 1, aimInput }, { 100, default } });

        // Hold aim
        var holdInput = new InputState { IsAiming = true };
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, holdInput }, { 100, default } });

        // Release → Firing, tether spawns then flies toward NPC
        var releaseInput = new InputState { IsAiming = false };
        for (int i = 0; i < 25; i++)
            sim.Tick(new() { { 1, releaseInput }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should have taken grapple damage, got {npcAfter.DamagePercent}");
    }

    // ══════════════════════════════════════════════════════════════════
    //  AIR LMB (slot 0 airborne, activeSlot=1)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void AirLMB_TwoHitCombo_ChainsToSecondStage()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 3f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, CombatDef, state);

        var input = TestHelpers.Input(activeSlot: 1);
        sim.Tick(new() { { 1, input } });
        Assert.Equal(0, sim.GetState(1).ComboStage);

        sim.Tick(new() { { 1, input } });

        for (int i = 2; i < 20; i++)
            sim.Tick(new() { { 1, default } });

        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ComboStage);
    }

    // ══════════════════════════════════════════════════════════════════
    //  AIR RMB (slot 1 airborne, activeSlot=2)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void AirRMB_Activation_SetsAttacking()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 3f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 5);

        Assert.Equal(ActionState.Attacking, after.State);
        Assert.True(after.AttackSlot > 0, "AttackSlot should be set");
    }
}
