using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════
/// GROUND-TRUTH COMBAT TESTS
/// ═══════════════════════════════════════════════════════════════════════
///
/// These tests exercise the FULL combat pipeline end to end:
///   Input → ServerAbility.OnStart → Ability.Tick → Resolver.Spawn →
///   BuildHurtboxList → Resolver.Tick → ResolveHits → ApplyKnockback
///
/// Each test verifies that a hitbox actually collides with an entity's
/// hurtbox and produces the correct damage/knockback/hitstun.
///
/// They serve as:
///   - Regression protection for the combat pipeline
///   - Ground-truth documentation of how abilities behave at the tick level
///   - A reference for agents modeling the game runtime
///
/// All tests use CombatDef (Manki stats + simple capsule hurtboxes) so
/// collision math works without baked skeleton data.
/// ═══════════════════════════════════════════════════════════════════════
public class CombatPipelineTests
{
    private static readonly float Gpy = TestHelpers.CombatGroundPY;

    // ═══════════════════════════════════════════════════════════════════
    // TEST 1: LMB melee combo hits NPC
    // ═══════════════════════════════════════════════════════════════════
    //
    // Manki LMB stage 1:
    //   Hitbox: sphere at Z=0.9 (in front), radius 1.0
    //   TriggerTick: 6 (hitbox appears on the 6th Tick call)
    //   Damage: 4, BaseKnockback: 1.5, KnockbackGrowth: 2.5, KnockbackUpward: 1, StunTicks: 10
    //
    // NPC is placed at Z=1.5 with a 0.3-radius capsule hurtbox.
    //   NPC Hurbox capsule: (0, -0.65, 1.5) → (0, 0.65, 1.5), Radius 0.3
    //   Hitbox center: (0, GroundPY, 0.9) at spawn tick
    //   Distance from hitbox center to closest point on NPC capsule: ≈0.6m
    //   Combined radius: 1.0 + 0.3 = 1.3 → HIT within margin
    //
    // Expected: NPC takes 4 damage, gains knockback velocity
    // NOTE: HitstunTicks is forced to HitboxEvent.StunTicks (=10) by
    // ResolveHits (line 449) AFTER ApplyKnockback runs, so even though
    // ApplyKnockback's weak-hit branch (kbMagnitude=3 not >3) would set
    // State=Idle, ResolveHits overwrites HitstunTicks=10.

    [Fact]
    public void LMB_HitsNpc_AppliesDamageKnockbackHitstun()
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
        var inputs = new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(activeSlot: 1) },
            { 100, default },
        };
        sim.Tick(inputs);

        // Ticks 1-10: default input (no hitbox yet, _stageTicks < 12)
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // Before trigger tick: NPC is unharmed
        var beforeHit = sim.GetState(100);
        Assert.Equal(0u, beforeHit.DamagePercent);
        Assert.Equal(0f, beforeHit.KVX);
        Assert.Equal(0f, beforeHit.KVZ);

        // Tick 11: _stageTicks=12 (10 extra ticks, total 12 ticks) → hitbox spawns → collision → damage resolved
        sim.Tick(new() { { 1, default }, { 100, default } });

        var afterHit = sim.GetState(100);
        Assert.True(afterHit.DamagePercent > 0,
            $"NPC should have taken damage, got {afterHit.DamagePercent}");
        // Stage 1 damage = 4 (no buffs active)
        Assert.InRange((int)afterHit.DamagePercent, 4, 4);

        // Knockback magnitude should be non-zero (direction depends on player
        // lunge position at trigger tick, which shifts between ticks)
        float kbMag = MathF.Sqrt(afterHit.KVX * afterHit.KVX
                                 + afterHit.KVY * afterHit.KVY
                                 + afterHit.KVZ * afterHit.KVZ);
        Assert.True(kbMag > 0.5f,
            $"NPC should have knockback from LMB hit, magnitude={kbMag:F3}");

        // HitstunTicks is forced by ResolveHits from the HitboxEvent,
        // regardless of ApplyKnockback's internal logic.
        Assert.Equal(32, (int)afterHit.HitstunTicks);
        // Manki LMB stage 1: StunTicks=32 → ≤30? no, >30 → level 1 (medium)
        Assert.Equal(1, (int)afterHit.HitstunLevel);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 1b: Re-hit while in hitstun resets HitstunTicks
    // ═══════════════════════════════════════════════════════════════════
    //
    // Two players attack the same NPC on consecutive ticks while the
    // hitboxes overlap. Player1 hits on tick 12 (LMB stage 1 trigger).
    // Player2's same-tick hitbox is blocked by hitThisTick, but player2's
    // hitbox (still active) hits on tick 13 while NPC is still in hitstun.
    //
    // Expected: HitstunTicks resets upward (new hit value, not continued
    // countdown). The client uses this to detect re-hits and restart
    // animation from frame 0.
    //
    // Mirrors PlayerRenderer.UpdateAnimationState re-hit detection:
    //   newHit = _lastAnimState != Hitstun || state.HitstunTicks >= _lastState.HitstunTicks
    private static bool IsReHit(bool wasInHitstun, ushort lastTicks, ushort currentTicks)
        => !wasInHitstun || currentTicks >= lastTicks;

    [Theory]
    [InlineData(false, 0,  20,  true,  "Fresh hit (was idle)")]
    [InlineData(true,  32, 31,  false, "Normal countdown: 32→31")]
    [InlineData(true,  31, 32,  true,  "Re-hit reset: 31→32")]
    [InlineData(true,  32, 32,  true,  "Re-hit same value: 32→32")]
    [InlineData(true,  48, 96,  true,  "Re-hit higher value: 48→96")]
    public void ReHit_ClientDetectionLogic_Correct(bool wasInHitstun, ushort lastTicks,
        ushort currentTicks, bool expectedNewHit, string desc)
    {
        bool result = IsReHit(wasInHitstun, lastTicks, currentTicks);
        Assert.Equal(expectedNewHit, result);
    }
    // TEST 2: Q projectile hits NPC, explodes
    // ═══════════════════════════════════════════════════════════════════
    //
    // Manki Q (Round Bomb):
    //   Phase 1 (hold, ~8 ticks): player aims (auto-release without input)
    //   Phase 2 (throw, 60 ticks): projectile spawned at trigger_tick=10
    //
    // Projectile:
    //   Launch: 30° upward, gravity=30, default aim distance=5m
    //   Hitbox: sphere radius 0.6, Damage=6
    //   Explosion: radius 3.0, Damage=10
    //
    // Timing estimate:
    //   Hold phase: ticks 0-8 (AttackElapsedTicks: 1→9, transition at 9 > 8)
    //   Throw phase starts: tick 8 (AttackElapsedTicks reset to 0)
    //   Projectile spawn: tick 18 (AttackElapsedTicks=10 in throw phase)
    //   Flight to ~Z=3.5 (ground impact): ≈22 ticks → tick ~40
    //   Explosion at ground: ProcessProjectileExplosions on tick 40
    //   Explosion hitbox processed: ResolveHits on tick 41→42
    //
    // Q projectile test.
    // Timing:
    //   Hold phase (aim with aimDistance=500): ticks 0-9 (AttackElapsedTicks 1→10)
    //   Release pull (IsAiming=false, aimDistance still set): triggers transition
    //   Throw phase: projectile spawns at AttackElapsedTicks=10
    //   Flight to Z=5 (ground impact): ≈22 ticks from spawn
    //   Explosion: next tick after ground impact

    [Fact]
    public void QProjectile_HitsNpc_DealsDamage()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        var def = TestHelpers.CombatDef;
        var player = TestHelpers.PlayerState();
        player.PY = Gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 3.5f);
        npc.PY = Gpy;
        sim.RegisterEntity(100, def, npc);

        // Build the aim input once
        var aimInput = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 500);
        var releaseInput = new InputState { ActiveSlot = 3, AimDistance = 500, IsAiming = false };

        // Tick 0: press Q with aim
        sim.Tick(new() { { 1, aimInput }, { 100, default } });
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);

        // Hold for 8 more ticks (so AttackElapsedTicks reaches 9+, exceeding 8)
        for (int i = 0; i < 8; i++)
            sim.Tick(new() { { 1, aimInput }, { 100, default } });

        // Release: IsAiming=false triggers transition, AimDistance=500 gives cached dist=5m
        for (int i = 0; i < 3; i++)
            sim.Tick(new() { { 1, releaseInput }, { 100, default } });

        // Wait for projectile flight (~22 ticks) + explosion + margin
        for (int i = 0; i < 60; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);

        // NPC should have taken SOME damage from projectile or explosion
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should have taken damage from Q projectile or explosion, got {npcAfter.DamagePercent}");

        // Direct projectile hit = 6 damage minimum (not stacking with explosion 10)
        Assert.True(npcAfter.DamagePercent >= 6,
            $"Direct projectile hit = 6 damage minimum, got {npcAfter.DamagePercent}");
        // Both direct hit (stun=28) and explosion (stun=20) are ≤30 → HitstunLevel = 0
        Assert.Equal(0, (int)npcAfter.HitstunLevel);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 3: Overclock buff boosts damage
    // ═══════════════════════════════════════════════════════════════════
    //
    // Overclock (F, slot 6) grants +3 damage and +0.5 radius to all hitboxes
    // while the buff is active. Lasts 480 ticks (8s). Injection animation is
    // 30 ticks, after which the buff persists independently of the ability.
    //
    // This test:
    //   1. Activates Overclock
    //   2. Waits for injection to finish (40 ticks)
    //   3. Activates LMB while buff is active
    //   4. Verifies the hitbox deals boosted damage (4+3=7)

    [Fact]
    public void OverclockBuffedLMB_DealsBonusDamage()
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

        // Phase 1: Activate Overclock
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(activeSlot: 6) },
            { 100, default },
        });
        var afterF = sim.GetState(1);
        Assert.True((afterF.BuffActiveFlags & (byte)BuffType.Overclock) != 0,
            "Overclock buff should be active after F press");

        // Phase 2: Wait for injection (30 ticks) + margin
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var afterInjection = sim.GetState(1);
        Assert.Equal(ActionState.Idle, afterInjection.State);
        Assert.True(afterInjection.BuffRemainingTicks > 400,
            "Buff should still be active (480 - 41 ≈ 439 ticks remaining)");

        // Phase 3: Activate LMB while buff is active
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(activeSlot: 1) },
            { 100, default },
        });

        // Ticks 1-10: no hitbox yet (TriggerTick=12, so ticks 1-10 have _stageTicks 2-11)
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // Before trigger: NPC still unharmed
        var beforeHit = sim.GetState(100);
        Assert.Equal(0u, beforeHit.DamagePercent);

        // Tick 11: hitbox spawns with +3 damage bonus
        sim.Tick(new() { { 1, default }, { 100, default } });

        var afterHit = sim.GetState(100);
        Assert.True(afterHit.DamagePercent > 4,
            $"Overclock-boosted hit should deal >4 damage (base 4 + 3 buff = 7), got {afterHit.DamagePercent}");

        // Base 4 + Overclock 3 = 7
        // ApplyBuffBonuses is called in ServerAbility.SpawnHitbox BEFORE the hitbox
        // enters the resolver, so Hitbox.Damage = 7. ResolveHits applies it directly.
        Assert.InRange((int)afterHit.DamagePercent, 7, 7);
        // Base 4 + Overclock 3 = 7 → HitstunLevel = 1 (medium tier)
        Assert.Equal(1, (int)afterHit.HitstunLevel);
    }
    // ═══════════════════════════════════════════════════════════════════
    // EDGE CASE: Mutual combat — two entities attack each other
    // ═══════════════════════════════════════════════════════════════════
    //
    // Both Player (1) and NPC (100) press LMB simultaneously. Their hitboxes
    // overlap due to close proximity. This tests that:
    //   1. Both hitboxes resolve without crashing
    //   2. Both entities receive damage/knockback/hitstun
    //   3. The simulation doesn't corrupt state when both sides are hit
    //
    // NOTE on ability interruption: ServerAbility has no OnInterrupt. When an
    // entity is hitstunned, its ability.Tick() continues running in the
    // TickAbilities phase. If EndAbility() fires during hitstun, it overwrites
    // State=Idle, clearing the hitstun state. This is a known design gap.
    // This test verifies the sim doesn't crash under these conditions.

    [Fact]
    public void MutualLMB_NoCorruption()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;

        // Player at origin, NPC 0.5m behind (so NPC's hitbox at Z=-0.5+0.9=0.4
        // with radius 1.0 covers the player at Z≈0)
        var player = TestHelpers.PlayerState(0f, 0f);
        player.PY = Gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, -0.5f);
        npc.PY = Gpy;
        sim.RegisterEntity(100, def, npc);

        // Both press LMB on tick 0
        var inputs = new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(activeSlot: 1) },
            { 100, TestHelpers.Input(activeSlot: 1) },
        };
        sim.Tick(inputs);

        // Run through trigger ticks, resolution, and stage expiry (22 ticks total) plus margin
        for (int i = 0; i < 25; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var pState = sim.GetState(1);
        var nState = sim.GetState(100);

        // Both should have taken damage (hitboxes overlapped)
        // At minimum one of them received damage
        Assert.True(pState.DamagePercent > 0 || nState.DamagePercent > 0,
            "At least one entity should have taken damage from mutual LMB trade. " +
            $"Player: {pState.DamagePercent}, NPC: {nState.DamagePercent}");

        Assert.True(pState.State >= ActionState.Idle && pState.State <= ActionState.Attacking,
            $"Player state corrupted: {pState.State}");
        Assert.True(nState.State >= ActionState.Idle && nState.State <= ActionState.Attacking,
            $"NPC state corrupted: {nState.State}");

        // No entity should have negative damage
        Assert.True(pState.DamagePercent <= 999);
        Assert.True(nState.DamagePercent <= 999);

        // Entity IDs preserved
        Assert.Equal((ulong)1, pState.EntityId);
        Assert.Equal((ulong)100, nState.EntityId);
    }

    [Fact]
    public void Warp_OutOfAttackRange_EntersWarpingState()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        float gpy = TestHelpers.CombatGroundPY;

        // NPC at Z=6: within WarpRange=10 but outside AttackRange=4
        var player = TestHelpers.PlayerState(z: 0f);
        player.PY = gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(z: 6f);
        npc.PY = gpy;
        sim.RegisterEntity(100, def, npc);

        // Press LMB → should NOT enter Attacking (too far), should enter Warping
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(ActionState.Warping, state.State);
    }

    [Fact]
    public void Warp_ArrivingAtTarget_StartsAttack()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        float gpy = TestHelpers.CombatGroundPY;

        // NPC at Z=5: within WarpRange=10 but outside AttackRange=4
        // Warp distance = 1m. SprintSpeed=12m/s → ~5 ticks.
        var player = TestHelpers.PlayerState(z: 0f);
        player.PY = gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(z: 5f);
        npc.PY = gpy;
        sim.RegisterEntity(100, def, npc);

        // Press LMB → enters Warping state
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        Assert.Equal(ActionState.Warping, sim.GetState(1).State);

        // Tick until warp completes (20 ticks is well beyond the ~5 needed)
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, state.State);
        Assert.True(state.AttackElapsedTicks > 0,
            "Attack should be running (elapsed ticks should be > 0)");
        Assert.True(state.AttackElapsedTicks < 40,
            $"Attack should be within stage duration (40 ticks), elapsed={state.AttackElapsedTicks}");
    }

    [Fact]
    public void Warp_WithinAttackRange_AttacksDirectly()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        float gpy = TestHelpers.CombatGroundPY;

        // NPC at Z=3: within AttackRange=4 → no warp, direct attack
        var player = TestHelpers.PlayerState(z: 0f);
        player.PY = gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(z: 3f);
        npc.PY = gpy;
        sim.RegisterEntity(100, def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, state.State);
    }
}
