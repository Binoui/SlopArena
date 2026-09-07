using System;
using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Behaviour tests for Kistu's kit. Normal tier (keys 1-4, ground + air) activation and
/// damage, plus the specials: E rising recovery, R directional dash, and F skyfall.
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
[InlineData((byte)4)] // E — Rising Slash
[InlineData((byte)5)] // R — Dash Slash
[InlineData((byte)6)] // F — Skyfall
    public void GroundSlot_Activates(byte slot)
    {
        var sim = SimWithPlayer(out _);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot, aiming: true), 1);
        // R (slot 5) is a hold-to-aim ability: it enters the Aiming state instead of Attacking.
        ActionState expected = slot == 5 ? ActionState.Aiming : ActionState.Attacking;
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

    // ── Normal tier: each ground normal damages a nearby enemy ──

    [Theory]
    [InlineData((byte)3)] // key "1"
    [InlineData((byte)7)] // key "2"
    [InlineData((byte)8)] // key "3"
    [InlineData((byte)9)] // key "4"
    public void GroundNormal_DamagesNearbyEnemy(byte slot)
    {
        var sim = SimWithPlayer(out _);
        var baked = TestHelpers.LoadBakedData(Def);

        var npc = TestHelpers.NpcState(0f, 1f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, Def, npc, baked);

        sim.Tick(new() { { 1, new InputState { ActiveSlot = slot } }, { 100, default } });
        for (int i = 0; i < 40; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0, $"slot {slot} should hit the enemy in reach");
    }

    // ── R: directional dash — tap and hold both travel the same set distance ──

    [Fact]
    public void R_TapAndHold_TravelSameSetDistance()
    {
        var tapSim = SimWithPlayer(out _);
        tapSim.Tick(new() { { 1, new InputState { ActiveSlot = 5, IsAiming = true, AimYaw = 0 } } });
        tapSim.Tick(new() { { 1, new InputState { IsAiming = false, AimYaw = 0 } } });
        for (int i = 0; i < 60; i++) tapSim.Tick(new() { { 1, default } });
        float tapPZ = tapSim.GetState(1).PZ;

        var holdSim = SimWithPlayer(out _);
        holdSim.Tick(new() { { 1, new InputState { ActiveSlot = 5, IsAiming = true, AimYaw = 0 } } });
        var hold = new InputState { IsAiming = true, AimYaw = 0 };
        for (int i = 0; i < 80; i++) holdSim.Tick(new() { { 1, hold } });
        float holdPZ = holdSim.GetState(1).PZ;

        Assert.True(MathF.Abs(tapPZ - 5f) < 0.1f, $"tap dash should cover 5 m, got PZ={tapPZ:F2}");
        Assert.True(MathF.Abs(holdPZ - 5f) < 0.1f, $"hold dash should cover 5 m, got PZ={holdPZ:F2}");
    }

    // ── E: rising slash lifts Kistu off ground as recovery ──

    [Fact]
    public void E_RisesOffGround()
    {
        var sim = SimWithPlayer(out _);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 10; i++) sim.Tick(new() { { 1, default } });
        var s = sim.GetState(1);
        Assert.False(s.IsGrounded);
        Assert.True(s.PY > GroundPY + 1f, $"expected Kistu to rise above {GroundPY + 1f:F2}, got {s.PY:F2}");
    }

    // ── E: launches a grounded enemy ──

    [Fact]
    public void E_LaunchesGroundedEnemy()
    {
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.0f); npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 12; i++) sim.Tick(new() { { 1, default }, { 100, default } });
        Assert.True(sim.GetState(100).DamagePercent > 0, "E should hit and damage the grounded enemy");
    }


    [Fact]
    public void E_RefundsChargeOnHit()
    {
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.0f); npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 24; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0, "E should have connected");
        Assert.Equal((byte)0, sim.GetState(1).ChargeStockSpent);
    }

    [Fact]
    public void E_RefundToEmpty_ClearsRegenTimer()
    {
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.0f); npc.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 24; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        var s = sim.GetState(1);
        Assert.Equal((byte)0, s.ChargeStockSpent);
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
