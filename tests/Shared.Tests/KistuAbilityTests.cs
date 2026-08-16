using System;
using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Behaviour tests for Kistu's kit. Normal tier (keys 1-4, ground + air) activation and
/// damage, plus the specials: E dash self-movement and R rising launcher + charge-stock
/// (spend / block-when-empty / refund-on-hit).
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
        // Baked data required: Kistu's normals are blade-anchored (RightHand→_weapon_tip),
        // which resolves against the bake like the real game (MatchInstance.LoadBakedData).
        sim.RegisterEntity(1, Def, player, TestHelpers.LoadBakedData(Def));
        return sim;
    }

    // ── Activation of every slot ──

    [Theory]
    [InlineData((byte)3)] // key "1" — Quick Slash (normal)
    [InlineData((byte)7)] // key "2" — Double Slash (normal)
    [InlineData((byte)8)] // key "3" — Up Slash (normal)
    [InlineData((byte)9)] // key "4" — Heavy Down Slash (normal)
    [InlineData((byte)4)] // E — Dash Slash
    [InlineData((byte)5)] // R — Rising Slash
    [InlineData((byte)6)] // F — Blade Flurry
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
    [InlineData((byte)3)] // key "1" air — Air Slash
    [InlineData((byte)7)] // key "2" air — Reverse Slash
    [InlineData((byte)8)] // key "3" air — Air Up Slash
    [InlineData((byte)9)] // key "4" air — Air Heavy Down Slash
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

    // ── Normal tier: each ground normal damages an enemy in reach ──

    [Theory]
    [InlineData((byte)3)] // g_1 Quick Slash — active 9-13
    [InlineData((byte)7)] // g_2 Double Slash — active 6-11 / 22-27
    [InlineData((byte)8)] // g_3 Up Slash — active 6-12
    [InlineData((byte)9)] // g_4 Heavy Down Slash — active 21-26
    public void GroundNormal_DamagesEnemyInReach(byte slot)
    {
        var sim = SimWithPlayer(out _);
        // PZ 0.6, not 1.5: the blade-anchored normals sweep at chest/head height with a
        // short forward reach (g_1's clip ~0.6 m at the apex; the old entity capsule
        // reached 1.8 m). 0.6 is inside every normal's blade reach.
        var npc = TestHelpers.NpcState(0f, 0.6f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, Def, npc, TestHelpers.LoadBakedData(Def));

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: slot) }, { 100, default } });
        // Enough ticks for the slowest normal (g_4 hitbox at trigger 21) to land.
        for (int i = 0; i < 40; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0, $"slot {slot} should hit the enemy in reach");
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
