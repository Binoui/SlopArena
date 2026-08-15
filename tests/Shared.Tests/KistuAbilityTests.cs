using System;
using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Behaviour tests for Kistu's kit. Standard slots (activation + damage) plus the novel
/// infra: E dash self-movement, R rising launcher + charge-stock
/// (spend / block-when-empty / refund-on-hit), and Q counter (absorb + riposte-launch).
/// </summary>
public class KistuAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.KistuDef);
    private static CharacterDefinition Def => TestHelpers.KistuDef;

    private static ServerSimulation SimWithPlayer(out CharacterState player)
    {
        var sim = TestHelpers.MakeSim();
        player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, Def, player);
        return sim;
    }

    // ── Activation of every slot ──

    [Theory]
    [InlineData((byte)1)] // LMB
    [InlineData((byte)3)] // Q
    [InlineData((byte)4)] // E
    [InlineData((byte)5)] // R
    [InlineData((byte)6)] // F
    public void GroundSlot_Activates(byte slot)
    {
        var sim = SimWithPlayer(out _);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot, aiming: true), 1);
        // E (slot 4) is a hold-to-aim ability: it enters the Aiming state instead of Attacking.
        ActionState expected = slot == 4 ? ActionState.Aiming : ActionState.Attacking;
        Assert.Equal(expected, t0.State);
        Assert.Equal(slot, t0.AttackSlot);
    }

    [Theory]
    [InlineData((byte)1)] // AirLMB
    public void AirSlot_Activates(byte slot)
    {
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 5f;
        s.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, s);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal(slot, t0.AttackSlot);
    }

    // ── LMB: damage + reach ──

    [Fact]
    public void Lmb_DamagesEnemyInReach()
    {
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0);
    }

    // ── E: directional dash — tap and hold both travel the same set distance (no charge) ──

    [Fact]
    public void E_TapAndHold_TravelSameSetDistance()
    {
        // Tap: press and release immediately.
        var tapSim = SimWithPlayer(out _);
        tapSim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 0 } } });
        tapSim.Tick(new() { { 1, new InputState { IsAiming = false, AimYaw = 0 } } });
        for (int i = 0; i < 60; i++) tapSim.Tick(new() { { 1, default } });
        float tapPZ = tapSim.GetState(1).PZ;

        // Hold: aim until the max-aim auto-release (180 ticks), then the dash runs.
        var holdSim = SimWithPlayer(out _);
        holdSim.Tick(new() { { 1, new InputState { ActiveSlot = 4, IsAiming = true, AimYaw = 0 } } });
        var hold = new InputState { IsAiming = true, AimYaw = 0 };
        for (int i = 0; i < 200; i++) holdSim.Tick(new() { { 1, hold } });
        float holdPZ = holdSim.GetState(1).PZ;

        // Set distance: the E dash always covers 5 m, independent of hold time.
        Assert.True(MathF.Abs(tapPZ - 5f) < 0.1f, $"tap dash should cover 5 m, got PZ={tapPZ:F2}");
        Assert.True(MathF.Abs(holdPZ - 5f) < 0.1f, $"hold dash should cover 5 m, got PZ={holdPZ:F2}");
    }

    // ── R: rising slash lifts Kistu off the ground (vertical recovery) ──

    [Fact]
    public void R_RisesOffGround()
    {
        var sim = SimWithPlayer(out _);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });
        for (int i = 0; i < 10; i++) sim.Tick(new() { { 1, default } });
        var s = sim.GetState(1);
        Assert.False(s.IsGrounded);
        Assert.True(s.PY > GroundPY + 1f, $"expected Kistu to rise above {GroundPY + 1f:F2}, got {s.PY:F2}");
    }

    // ── R: launches a grounded enemy ──

    [Fact]
    public void R_LaunchesGroundedEnemy()
    {
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.0f); npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default }, { 100, default } });
        Assert.True(sim.GetState(100).DamagePercent > 0, "R should hit and damage the grounded enemy");
    }

    // ── R charge-stock: two whiffs exhaust the pool; the third cast is blocked ──

    [Fact]
    public void R_ChargePool_ExhaustsThenBlocks()
    {
        var sim = SimWithPlayer(out _);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });
        for (int i = 0; i < 30; i++) sim.Tick(new() { { 1, default } });
        Assert.Equal((byte)1, sim.GetState(1).ChargeStockSpent);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });
        for (int i = 0; i < 30; i++) sim.Tick(new() { { 1, default } });
        Assert.Equal((byte)2, sim.GetState(1).ChargeStockSpent);

        // Pool exhausted (max_charges = 2): third cast must not activate or spend more.
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });
        var s = sim.GetState(1);
        Assert.Equal((byte)2, s.ChargeStockSpent);
        Assert.NotEqual((byte)5, s.AttackSlot);
    }

    // ── R charge-stock: landing a hit refunds the spent charge ──

    [Fact]
    public void R_RefundsChargeOnHit()
    {
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.0f); npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 24; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0, "R should have connected");
        Assert.Equal((byte)0, sim.GetState(1).ChargeStockSpent); // spent 1, refunded 1
    }

    // ── R charge-stock: refund-to-empty clears the regen timer (no stale partial) ──

    [Fact]
    public void R_RefundToEmpty_ClearsRegenTimer()
    {
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.0f); npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        // Spend one charge, then land the hit — refund brings the pool back to full.
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 24; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        var s = sim.GetState(1);
        Assert.Equal((byte)0, s.ChargeStockSpent);
        // Timer must be cleared (not a stale partial countdown) so the next spend gets a full period.
        Assert.Equal((ushort)0, s.ChargeStockRegenTicks);
    }

    // ── Q counter: absorbs a hit in the window and launches the attacker ──

    [Fact]
    public void Q_CounterAbsorbsAndRipostes()
    {
        var sim = TestHelpers.MakeSim();
        var kistu = TestHelpers.PlayerState(0f, 0f);
        kistu.PY = GroundPY;
        kistu.FacingYaw = 0f; // facing +Z toward the attacker
        TestHelpers.RegisterPlayer(sim, Def, kistu);

        var attacker = TestHelpers.NpcState(0f, 1.0f);
        attacker.PY = GroundPY;
        attacker.FacingYaw = MathF.PI; // facing -Z toward Kistu
        TestHelpers.RegisterNpc(sim, Def, attacker);

        // Both act on tick 0: Kistu parries, attacker swings LMB (hitbox lands ~tick 6, inside window).
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 3) }, { 100, TestHelpers.Input(activeSlot: 1) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        var kistuAfter = sim.GetState(1);
        var attackerAfter = sim.GetState(100);
        Assert.Equal((ushort)0, kistuAfter.DamagePercent); // hit absorbed
        Assert.True(attackerAfter.DamagePercent > 0, "attacker should take riposte damage");
        Assert.True(attackerAfter.HitstunTicks > 0, "attacker should be launched into hitstun");
    }

    // ── Q counter negative: without an active parry, the hit lands normally ──

    [Fact]
    public void Q_NoCounterWhenInactive_TakesDamage()
    {
        var sim = TestHelpers.MakeSim();
        var kistu = TestHelpers.PlayerState(0f, 0f);
        kistu.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, Def, kistu);

        var attacker = TestHelpers.NpcState(0f, 1.0f);
        attacker.PY = GroundPY;
        attacker.FacingYaw = MathF.PI;
        TestHelpers.RegisterNpc(sim, Def, attacker);

        // Kistu does nothing; attacker swings.
        sim.Tick(new() { { 1, default }, { 100, TestHelpers.Input(activeSlot: 1) } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(1).DamagePercent > 0, "Kistu should take damage with no active counter");
    }

    // ── F: blade flurry deals damage ──

    [Fact]
    public void F_FlurryDamagesEnemy()
    {
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.3f); npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });
        for (int i = 0; i < 64; i++) sim.Tick(new() { { 1, default }, { 100, default } });
        Assert.True(sim.GetState(100).DamagePercent > 0);
    }
}
