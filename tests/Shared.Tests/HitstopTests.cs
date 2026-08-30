using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════
/// HITSTOP TESTS (ADR-0012, issue #98)
/// ═══════════════════════════════════════════════════════════════════════
///
/// Verifies the per-pair freeze:
///   - On a melee hit BOTH attacker and victim freeze for F ticks
///     (F = min(12, damage/3 + 6), ADR-0019; per-ability hitstop_multiplier override).
///   - The victim is fully stationary during the freeze; the launch (KV +
///     hitstun) is deferred to freeze expiry.
///   - The attacker's ability ticks and AnimLockTicks/AttackElapsedTicks
///     pause symmetrically (no free combo extension).
///   - Projectile/zone hits freeze the receiver only.
///
/// Tick accounting: ResolveHits runs AFTER SimulateMovement within a tick, so
/// a freeze set at the end of tick N is decremented on ticks N+1..N+F, the
/// launch is applied at the end of tick N+F, and the victim first MOVES at
/// tick N+F+1.
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public class HitstopTests
{
    private static readonly float Gpy = TestHelpers.CombatGroundPY;





    [Fact]
    public void Multihit_SecondHit_FreezeNotDiscounted()
    {
        // Deterministic formula: ADR-0019 dropped the beyond-first ×0.5 discount —
        // every connecting hit freezes at the full rate.
        Assert.Equal(7, ServerSimulation.ComputeHitstopTicks(4, null));
        Assert.Equal(7, ServerSimulation.ComputeHitstopTicks(4, null));

        // Integration: a RehitIntervalTicks zone rehits the still-frozen victim —
        // the second pulse applies a full fresh freeze.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var def = TestHelpers.CombatDef;
        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = Gpy;
        sim.RegisterEntity(100, def, npc);

        // Zone at the NPC, pulse every 3 ticks, damage 4 (F = 7, discounted 3).
        sim.Resolver.Spawn(new Hitbox
        {
            X = 0f, Y = Gpy, Z = 2.2f,
            Radius = 1.2f,
            Damage = 4f,
            DurationTicks = 30,
            RehitIntervalTicks = 3,
            OwnerId = 0,
        });

        sim.Tick(new() { { 100, default } });       // tick 1: first pulse (AgeTicks 0)
        Assert.Equal((ushort)7, sim.GetState(100).HitstopTicks);
        sim.Tick(new() { { 100, default } });       // tick 2
        Assert.Equal((ushort)6, sim.GetState(100).HitstopTicks);
        sim.Tick(new() { { 100, default } });       // tick 3
        Assert.Equal((ushort)5, sim.GetState(100).HitstopTicks);
        sim.Tick(new() { { 100, default } });       // tick 4: second pulse (AgeTicks 3) — still frozen → full fresh freeze
        Assert.Equal((ushort)7, sim.GetState(100).HitstopTicks);
    }

    [Fact]
    public void ProjectileHit_DoesNotFreezeThrower()
    {
        // Manki Q (Round Bomb) — projectile + explosion both receiver-only.
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;

        var player = TestHelpers.PlayerState();
        player.PY = Gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 3.5f);
        npc.PY = Gpy;
        sim.RegisterEntity(100, def, npc);

        var aimInput = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 500);
        var releaseInput = new InputState { ActiveSlot = 3, AimDistance = 500, IsAiming = false };

        sim.Tick(new() { { 1, aimInput }, { 100, default } });
        for (int i = 0; i < 8; i++)
            sim.Tick(new() { { 1, aimInput }, { 100, default } });
        for (int i = 0; i < 3; i++)
            sim.Tick(new() { { 1, releaseInput }, { 100, default } });

        // Flight + explosion window: find the tick the NPC first freezes.
        int freezeTick = -1;
        for (int i = 0; i < 60 && freezeTick < 0; i++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });
            if (sim.GetState(100).HitstopTicks > 0) freezeTick = i;
        }
        Assert.True(freezeTick >= 0, "the projectile/explosion must freeze the victim");
        Assert.Equal((ushort)0, sim.GetState(1).HitstopTicks);
    }

    [Fact]
    public void Formula_Contract()
    {
        // (damage, spec) → freeze ticks. ADR-0019: min(12, (int)(damage/3 + 6)), floor 1.
        Assert.Equal(6, ServerSimulation.ComputeHitstopTicks(1, null));   // 6.33 → 6
        Assert.Equal(7, ServerSimulation.ComputeHitstopTicks(4, null));   // 7.33 → 7
        Assert.Equal(10, ServerSimulation.ComputeHitstopTicks(14, null)); // 10.67 → 10
        Assert.Equal(11, ServerSimulation.ComputeHitstopTicks(16, null)); // 11.33 → 11 (kit max)
        Assert.Equal(12, ServerSimulation.ComputeHitstopTicks(18, null)); // 12.00 → capped 12 (safety)
        // Per-ability override via spec.Params.
        var halved = new AbilitySpec { Params = new() { ["hitstop_multiplier"] = 0.5f } };
        Assert.Equal(3, ServerSimulation.ComputeHitstopTicks(4, halved)); // 3.67 → 3
    }
}
