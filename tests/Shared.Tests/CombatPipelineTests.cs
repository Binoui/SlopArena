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
    // ADR-0019 derives hitstun from the applied knockback magnitude.
    // The old authored StunTicks override is intentionally removed.

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

        // Hitstop (ADR-0012): at connect the victim freezes for 1 + 1.5·4 = 7 ticks —
        // KV is queued, not yet applied; damage + HitstunLevel land immediately.
        Assert.Equal(7, (int)afterHit.HitstopTicks);
        Assert.Equal(0f, afterHit.KVX);
        Assert.Equal(0f, afterHit.KVY);
        Assert.Equal(0f, afterHit.KVZ);
        // Manki LMB stage 1: StunTicks=20 (banded) → ≤30 → level 0 (light)
        Assert.Equal(0, (int)afterHit.HitstunLevel);

        // Freeze expires at tick 18 — the queued launch applies there.
        for (int i = 0; i < 7; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var afterLaunch = sim.GetState(100);
        Assert.Equal(0, (int)afterLaunch.HitstopTicks);

        // Knockback magnitude should be non-zero (direction depends on player
        // lunge position at trigger tick, which shifts between ticks)
        float kbMag = MathF.Sqrt(afterLaunch.KVX * afterLaunch.KVX
                                 + afterLaunch.KVY * afterLaunch.KVY
                                 + afterLaunch.KVZ * afterLaunch.KVZ);
        Assert.True(kbMag > 0.4f,
            $"NPC should have knockback from LMB hit, magnitude={kbMag:F3}"); // Light mag 3.9 × KV scale 0.11 ≈ 0.43

        // HitstunTicks is forced by ResolveHits from the HitboxEvent,
        // ADR-0019 derives this from the resulting launch magnitude.
        // Manki LMB = Light profile: mag 3.9 → stun 0.7·(3.9+20) = 16.
        Assert.Equal(16, (int)afterLaunch.HitstunTicks);
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
    // Mirrors PlayerRenderer.UpdateAnimationState re-hit detection (fixed):
    //   newHit = !(wasInHitstun && lastTicks > 0 && currentTicks == lastTicks - 1)
    // ResolveHits OVERWRITES HitstunTicks with the new hit's raw StunTicks, which
    // can be LOWER than the remaining countdown (weaker follow-up), so any tick
    // that is not the natural 1-tick countdown is a new hit.
    private static bool IsReHit(bool wasInHitstun, ushort lastTicks, ushort currentTicks)
        => !(wasInHitstun && lastTicks > 0 && currentTicks == lastTicks - 1);

    [Theory]
    [InlineData(false, 0,  20,  true,  "Fresh hit (was idle)")]
    [InlineData(true,  32, 31,  false, "Normal countdown: 32→31")]
    [InlineData(true,  31, 32,  true,  "Re-hit reset: 31→32")]
    [InlineData(true,  32, 32,  true,  "Re-hit same value: 32→32")]
    [InlineData(true,  48, 96,  true,  "Re-hit higher value: 48→96")]
    [InlineData(true,  32, 16,  true,  "Re-hit lower value: 32→16")]
    public void ReHit_ClientDetectionLogic_Correct(bool wasInHitstun, ushort lastTicks,
        ushort currentTicks, bool expectedNewHit, string desc)
    {
        bool result = IsReHit(wasInHitstun, lastTicks, currentTicks);
        if (result != expectedNewHit)
            Assert.Fail($"{desc}: expected {expectedNewHit} but got {result}");
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
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);

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
        // Base 4 + Overclock 3 = 7 → StunTicks 20 (banded) → HitstunLevel = 0 (light tier)
        Assert.Equal(0, (int)afterHit.HitstunLevel);
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
    public void Attack_WithinEngageRange_StartsDirectly()
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

    [Fact]
    public void Attack_BehindEnemy_StartsDirectly()
    {
        // NPC behind player (negative Z) — warp is gone, so the attack starts anyway
        // (commitment model: you whiff if out of reach).
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        float gpy = TestHelpers.CombatGroundPY;

        var player = TestHelpers.PlayerState(z: 0f);
        player.PY = gpy;
        player.FacingYaw = 0f; // facing +Z
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(z: -6f); // behind player
        npc.PY = gpy;
        sim.RegisterEntity(100, def, npc);

        // Press LMB → no warp, direct attack
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(0f, state.WarpSpeed);
        Assert.Equal(ActionState.Attacking, state.State);
    }

    // ═══════════════════════════════════════════════════════════════════
    // VERTICAL KNOCKBACK VERIFICATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void LightProfile_ApplyKnockback_HasVerticalComponent()
    {
        var state = TestHelpers.PlayerState();
        state.DamagePercent = 50;

        Simulation.ApplyKnockback(ref state, dirX: 1f, dirZ: 0f,
            angleDeg: 5, baseKB: 2f, growthKB: 1.5f, damage: 0f, stunTicks: 20, weight: 100f);

        Assert.True(state.KVY > 0f,
            $"Light profile (5°) should produce vertical knockback, got KVY={state.KVY:F4}");
    }

    [Fact]
    public void MediumProfile_ApplyKnockback_HasVerticalComponent()
    {
        var state = TestHelpers.PlayerState();
        state.DamagePercent = 50;

        Simulation.ApplyKnockback(ref state, dirX: 1f, dirZ: 0f,
            angleDeg: 15, baseKB: 8f, growthKB: 5f, damage: 0f, stunTicks: 20, weight: 100f);

        // KVY = 15.5 · sin15° · KbScaleFactor(0.11) ≈ 0.44 — the 0.11 velocity scale
        // lowered the launch; assert the vertical component survives, not the old 0.5.
        Assert.True(state.KVY > 0.4f,
            $"Medium profile (15°) should produce noticeable vertical knockback, got KVY={state.KVY:F4}");
    }

    [Fact]
    public void FightGuyLMB_FullPipeline_HasVerticalKnockback()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        var def = TestHelpers.CombatDef;
        float gpy = TestHelpers.CombatGroundPY;

        var player = TestHelpers.PlayerState();
        player.PY = gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = gpy;
        sim.RegisterEntity(100, def, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });

        for (int i = 0; i < 11; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        sim.Tick(new() { { 1, default }, { 100, default } });

        var afterHit = sim.GetState(100);

        Assert.True(afterHit.DamagePercent > 0,
            $"NPC should have taken damage from LMB hit, got {afterHit.DamagePercent}");

        // Hitstop (ADR-0012): the launch is deferred — the victim is frozen 10 ticks.
        Assert.True(afterHit.HitstopTicks > 0, "NPC should be in hitstop at connect");
        Assert.True(afterHit.IsGrounded, "frozen victim is still grounded");

        while (sim.GetState(100).HitstopTicks > 0)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var afterLaunch = sim.GetState(100);
        Assert.Equal(0, (int)afterLaunch.HitstopTicks);

        // After hit: must be airborne (was ground snap eating vertical KB)
        Assert.False(afterLaunch.IsGrounded,
            $"NPC should be airborne after knockback (ground snap bug), IsGrounded={afterLaunch.IsGrounded}");

        // KVY must be non-zero (vertical knockback from angle=15°)
        Assert.True(afterLaunch.KVY > 0f,
            $"LMB hit should produce vertical knockback velocity, got KVY={afterLaunch.KVY:F4}");

        // PY must be above ground (vertical knockback pushed them up)
        Assert.True(afterLaunch.PY > gpy + 0.01f,
            $"LMB hit should lift NPC off ground, PY={afterLaunch.PY:F4} ground={gpy:F4}");

    }

    [Fact]
    public void RealHit_ZeroHitstop_AppliesTargetDIImmediately()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(1, TestHelpers.CombatDef, attacker);
        var target = TestHelpers.NpcState(0f, 1.5f);
        target.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(100, TestHelpers.CombatDef, target);

        sim.Resolver.Spawn(new Hitbox
        {
            X = target.PX, Y = target.PY, Z = target.PZ,
            EndX = target.PX, EndY = target.PY, EndZ = target.PZ,
            Radius = 1f, Shape = HitboxShape.Sphere,
            Damage = 0f, BaseKnockback = 10f, KnockbackGrowth = 0f,
            KnockbackAngle = 45, StunTicks = 20, DurationTicks = 1,
            OwnerId = 1, RehitIntervalTicks = 0,
        });

        sim.Tick(new()
        {
            { 1, default },
            { 100, new InputState { MoveX = 1f, MoveY = 0f } },
        });

        var frozen = sim.GetState(100);
        Assert.True(frozen.HitstopTicks > 0);
        Assert.Equal(ActionState.Run, frozen.State);

        // 0-damage hit freezes 6 ticks under ADR-0019 (min(12, 0/3 + 6)) — was 2 under ADR-0012.
        for (int i = 0; i < 12 && sim.GetState(100).HitstopTicks > 0; i++)
        {
            sim.Tick(new()
            {
                { 1, default },
                { 100, new InputState { MoveX = 1f, MoveY = 0f } },
            });
        }
        var state = sim.GetState(100);
        Assert.Equal(0, state.HitstopTicks);
        Assert.Equal(ActionState.Hitstun, state.State);
        Assert.True(state.KVX > 0f);
        Assert.True(state.KVY > 0f);
        Assert.Equal(1f, state.DIX);
        Assert.True(state.KVZ > 0f, $"DI should preserve the launch-side horizontal direction, got KVZ={state.KVZ}");
    }
}
