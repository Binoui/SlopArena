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
///     (F = 2 + 2·damage, cap 24; <3 damage ×2; beyond-first ×0.5).
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

    /// <summary>Manki LMB stage 1 connects at the 12th sim tick (Tick 0 = press).</summary>
    private const int ConnectTick = 11;
    /// <summary>Manki LMB stage 1 damage = 4 → F = 2 + 2·4 = 10.</summary>
    private const int LmbFreeze = 10;

    /// <summary>Player LMB vs NPC — the canonical freeze scenario (mirrors CombatPipelineTests.LMB_HitsNpc).</summary>
    private static (ServerSimulation sim, CharacterDefinition def) LmbScenario()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;

        var player = TestHelpers.PlayerState();
        player.PY = Gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = Gpy;
        sim.RegisterEntity(100, def, npc);

        // Tick 0: press LMB (slot 1)
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(activeSlot: 1) },
            { 100, default },
        });
        // Ticks 1-10: no hitbox yet
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });
        return (sim, def);
    }

    [Fact]
    public void Hit_FreezeF_VictimStationaryThenLaunches()
    {
        var (sim, def) = LmbScenario();

        // Tick 11 (connect): the hit resolves at the END of this tick.
        sim.Tick(new() { { 1, default }, { 100, default } });
        var atConnect = sim.GetState(100);
        Assert.Equal((ushort)LmbFreeze, atConnect.HitstopTicks);
        Assert.Equal(0f, atConnect.KVX);
        Assert.Equal(0f, atConnect.KVY);
        Assert.Equal(0f, atConnect.KVZ);
        Assert.Equal((ushort)0, atConnect.HitstunTicks);   // launch deferred
        Assert.Equal(1, (int)atConnect.HitstunLevel);      // tier still set at connect
        Assert.Equal((ushort)4, atConnect.DamagePercent);
        (float px, float py, float pz) frozenPos = (atConnect.PX, atConnect.PY, atConnect.PZ);

        // Ticks 12..21: frozen — position strictly unchanged, freeze countdown runs.
        for (int i = 0; i < LmbFreeze; i++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });
            var s = sim.GetState(100);
            Assert.True(s.PX == frozenPos.px && s.PY == frozenPos.py && s.PZ == frozenPos.pz,
                $"tick {ConnectTick + 1 + i}: victim moved during freeze " +
                $"({frozenPos.px:F4},{frozenPos.py:F4},{frozenPos.pz:F4}) -> ({s.PX:F4},{s.PY:F4},{s.PZ:F4})");
            Assert.Equal((ushort)(LmbFreeze - 1 - i), s.HitstopTicks);
        }

        // Tick 21 (end of freeze): the queued launch applied — KV + hitstun, still unmoved.
        var atLaunch = sim.GetState(100);
        Assert.Equal((ushort)0, atLaunch.HitstopTicks);
        Assert.Equal(ActionState.Hitstun, atLaunch.State);
        Assert.Equal((ushort)32, atLaunch.HitstunTicks);   // forced StunTicks override
        float kbMag = MathF.Sqrt(atLaunch.KVX * atLaunch.KVX
                                 + atLaunch.KVY * atLaunch.KVY
                                 + atLaunch.KVZ * atLaunch.KVZ);
        Assert.True(kbMag > 0.5f, $"launch applied at freeze expiry, magnitude={kbMag:F3}");
        Assert.True(atLaunch.PX == frozenPos.px && atLaunch.PY == frozenPos.py && atLaunch.PZ == frozenPos.pz,
            "launch tick itself must not move the victim (gate returns before physics)");

        // Tick 22: first flight tick — the victim has moved.
        sim.Tick(new() { { 1, default }, { 100, default } });
        var inFlight = sim.GetState(100);
        Assert.True(inFlight.PX != frozenPos.px || inFlight.PY != frozenPos.py || inFlight.PZ != frozenPos.pz,
            "victim must move once hitstun flight begins after the freeze");
    }

    [Fact]
    public void Attacker_Freeze_ExtendsAnimLockByF()
    {
        var (sim, _) = LmbScenario();

        // Tick 11 (connect): attacker frozen too (melee hit, FreezesOwner).
        sim.Tick(new() { { 1, default }, { 100, default } });
        var attackerAtConnect = sim.GetState(1);
        Assert.Equal((ushort)LmbFreeze, attackerAtConnect.HitstopTicks);
        ushort elapsedAtConnect = attackerAtConnect.AttackElapsedTicks;
        ushort lockAtConnect = attackerAtConnect.AnimLockTicks;
        Assert.True(elapsedAtConnect > 0 && lockAtConnect > 0, "attack running at connect");

        // Tick 12: attacker frozen — ability timers paused (no TickTimers, no ability Tick).
        sim.Tick(new() { { 1, default }, { 100, default } });
        var attackerFrozen = sim.GetState(1);
        Assert.Equal((ushort)(LmbFreeze - 1), attackerFrozen.HitstopTicks);
        Assert.Equal(elapsedAtConnect, attackerFrozen.AttackElapsedTicks);
        Assert.Equal(lockAtConnect, attackerFrozen.AnimLockTicks);

        // Run to tick 40: pre-hitstop the stage-1 attack (40 ticks) would have ended here.
        // The 10-tick freeze pauses it, so AttackElapsedTicks is 10 short.
        for (int i = 0; i < 40 - ConnectTick - 1; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });
        var attackerAt40 = sim.GetState(1);
        Assert.Equal((ushort)(elapsedAtConnect + (40 - ConnectTick) - LmbFreeze), attackerAt40.AttackElapsedTicks);
        Assert.True(attackerAt40.AttackElapsedTicks < 40,
            $"attack must NOT have completed by tick 40 (frozen 10 ticks), elapsed={attackerAt40.AttackElapsedTicks}");

        // The attack completes strictly later and the attacker returns to Idle.
        for (int i = 0; i < 25; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });
        var done = sim.GetState(1);
        Assert.Equal(ActionState.Idle, done.State);
    }

    [Fact]
    public void Multihit_SecondHit_FreezesHalf()
    {
        // Deterministic formula half-halving.
        Assert.Equal(10, ServerSimulation.ComputeHitstopTicks(4, beyondFirst: false, null));
        Assert.Equal(5, ServerSimulation.ComputeHitstopTicks(4, beyondFirst: true, null));

        // Integration: a RehitIntervalTicks zone rehits the still-frozen victim —
        // the second pulse's freeze equals half the first.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var def = TestHelpers.CombatDef;
        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = Gpy;
        sim.RegisterEntity(100, def, npc);

        // Zone at the NPC, pulse every 3 ticks, damage 4 (F = 10, discounted 5).
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
        Assert.Equal((ushort)10, sim.GetState(100).HitstopTicks);
        sim.Tick(new() { { 100, default } });       // tick 2
        Assert.Equal((ushort)9, sim.GetState(100).HitstopTicks);
        sim.Tick(new() { { 100, default } });       // tick 3
        Assert.Equal((ushort)8, sim.GetState(100).HitstopTicks);
        sim.Tick(new() { { 100, default } });       // tick 4: second pulse (AgeTicks 3) — still frozen → half
        Assert.Equal((ushort)5, sim.GetState(100).HitstopTicks);
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
        // (damage, beyondFirst, spec) → freeze ticks.
        Assert.Equal(8, ServerSimulation.ComputeHitstopTicks(1, false, null));   // low damage ×2 (2+2 → 4 → 8)
        Assert.Equal(10, ServerSimulation.ComputeHitstopTicks(4, false, null));  // 2 + 2·4
        Assert.Equal(24, ServerSimulation.ComputeHitstopTicks(14, false, null)); // 2 + 28 → capped 24
        Assert.Equal(12, ServerSimulation.ComputeHitstopTicks(14, true, null));  // cap first, then ×0.5
        Assert.Equal(5, ServerSimulation.ComputeHitstopTicks(4, true, null));    // 10 × 0.5
        // Per-ability override via spec.Params.
        var capped = new AbilitySpec { Params = new() { ["hitstop_cap_ticks"] = 5f } };
        Assert.Equal(5, ServerSimulation.ComputeHitstopTicks(14, false, capped));
    }
}
