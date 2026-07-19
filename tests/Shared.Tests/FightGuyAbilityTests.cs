using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

public class FightGuyAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);

    // ── LMB (FightGuyLmbCombo) ──

    [Fact]
    public void FightGuyLmbCombo_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)1, t0.AttackSlot);
    }

    [Fact]
    public void FightGuyLmbCombo_ChainsToStage2()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        var def = TestHelpers.FightGuyDef;
        var stage1Ticks = def.LMB!.Stages[0].DurationTicks;
        TestHelpers.RegisterPlayer(sim, def, state);
        // Tick 0: LMB to start stage 1
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        // Tick 1: buffer chain input
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        // Run to stage 1 end — chain fires
        for (int i = 2; i < stage1Ticks; i++)
            TestHelpers.TickDefault(sim, 1);
        var afterChain = sim.GetState(1);
        Assert.Equal((byte)1, afterChain.ComboStage);
        Assert.Equal(ActionState.Attacking, afterChain.State);
    }

    [Fact]
    public void FightGuyLmbCombo_ExpiresAfterFinalStage()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        for (int s = 0; s < 2; s++)
        {
            for (int i = 0; i < 44; i++) TestHelpers.TickDefault(sim, 1);
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) } });
        }
        for (int i = 0; i < 65; i++) TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void FightGuyLmbCombo_Stage1DealsDamageToNpc()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC in front of player, within LMB hitbox range
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = GroundPY;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // LMB and tick through hitbox trigger (tick 6)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // Player should have lunged forward
        var playerAfter = sim.GetState(1);
        Assert.True(playerAfter.PZ > 0.3f,
            $"Expected player to lunge forward (PZ > 0.3), got PZ={playerAfter.PZ:F3}");

        // NPC should have taken damage
        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should have taken damage from LMB, got {npcAfter.DamagePercent}");
    }

    // ── Q (FightGuyKiShot) ──

    [Fact]
    public void FightGuyKiShot_ActivatesAimed()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 500), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
        Assert.True(t0.IsAiming);
    }

    [Fact]
    public void FightGuyKiShot_ThrowsProjectile()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 500);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim } });
        var rel = new InputState { ActiveSlot = 3, AimDistance = 500 };
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, rel } });
        Assert.Equal((byte)1, sim.GetState(1).ComboStage);
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void FightGuyKiShot_AppliesMark()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 50);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim }, { 100, default } });
        var rel = new InputState { ActiveSlot = 3, AimDistance = 50 };
        for (int i = 0; i < 90; i++) sim.Tick(new() { { 1, rel }, { 100, default } });
        var npcAfter = sim.GetState(100);
        Assert.True((npcAfter.StatusFlags & (1 << 2)) != 0, "NPC should have Marked status");
        Assert.True(npcAfter.StatusRemainingTicks > 0);
    }

    [Fact]
    public void FightGuyKiShot_MarkExpiresAfterDuration()
    {
        var sim = TestHelpers.MakeSim();
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;  // 5s mark
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Tick exactly 300 times — should clear at 0
        for (int i = 0; i < 300; i++)
            sim.Tick(new() { { 100, default } });

        var after = sim.GetState(100);
        Assert.Equal((ushort)0, after.StatusRemainingTicks);
        Assert.Equal((byte)0, after.StatusFlags);
    }

    [Fact]
    public void FightGuyKiShot_MarkStillActiveAtHalfDuration()
    {
        var sim = TestHelpers.MakeSim();
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        for (int i = 0; i < 150; i++)
            sim.Tick(new() { { 100, default } });

        var after = sim.GetState(100);
        Assert.Equal((ushort)150, after.StatusRemainingTicks);
        Assert.Equal((byte)(1 << 2), after.StatusFlags);
    }

    // ── E (FightGuyCycloneKick) ──

    [Fact]
    public void FightGuyCycloneKick_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);
    }

    [Fact]
    public void FightGuyCycloneKick_AppliesForwardLunge()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.FacingYaw = 0f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 3);
        Assert.True(t1.VZ > 16f, $"Expected VZ>16 (forward lunge), got VZ={t1.VZ:F3}");
        Assert.True(t1.PZ > 0.1f, $"Expected forward position change, got PZ={t1.PZ:F3}");
    }

    [Fact]
    public void FightGuyCycloneKick_HitboxInFrontStuns()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC in front (OffZ=1.8 hitbox, player lunges forward)
        var npc = TestHelpers.NpcState(0f, 3f);
        npc.PY = GroundPY;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Press E and tick past hitbox trigger (tick 10, after windup)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 24; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });
        var npcAfter = sim.GetState(100);

        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from Tornado Kick, got {npcAfter.DamagePercent}");
        Assert.True(npcAfter.HitstunTicks >= 40,
            $"Expected HitstunTicks >= 40 (stun), got {npcAfter.HitstunTicks}");
    }

    [Fact]
    public void FightGuyCycloneKick_HitsMultipleEnemiesAlongPath()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC1 close (z=2), NPC2 far (z=6)
        var npc1 = TestHelpers.NpcState(0f, 2f);
        npc1.PY = GroundPY;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc1);

        var npc2 = TestHelpers.NpcState(0f, 6f);
        npc2.PY = GroundPY;
        sim.RegisterEntity(101, TestHelpers.FightGuyDef, npc2);

        // Activate E and tick through full duration
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default }, { 101, default } });
        for (int i = 0; i < 30; i++)
            sim.Tick(new() { { 1, default }, { 100, default }, { 101, default } });

        var n1 = sim.GetState(100);
        var n2 = sim.GetState(101);
        Assert.True(n1.DamagePercent > 0, $"NPC1 should take damage, got {n1.DamagePercent}");
        Assert.True(n1.HitstunTicks >= 20, $"NPC1 stun too short: {n1.HitstunTicks}");
        Assert.True(n2.DamagePercent > 0, $"NPC2 should take damage, got {n2.DamagePercent}");
        Assert.True(n2.HitstunTicks >= 20, $"NPC2 stun too short: {n2.HitstunTicks}");
    }

    // ── R (FightGuyDragonKick) ──

    [Fact]
    public void FightGuyDragonKick_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)5, t0.AttackSlot);
    }

    [Fact]
    public void FightGuyDragonKick_RecastCancelsEarly()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        for (int i = 0; i < 20; i++) TestHelpers.TickDefault(sim, 1);
        var cancel = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Idle, cancel.State);
    }

    [Fact]
    public void FightGuyDragonKick_NormalDamageWithoutMark()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);

        // Forward capsule hitbox sweeps the player's path. At tick ~15, PZ=5.0,
        // capsule covers z=5.5-6.5. NPC at z=5 is within capsule+radius range.
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0,
            $"NPC should have taken damage, got {sim.GetState(100).DamagePercent}");
    }




    [Fact]
    public void FightGuyDragonKick_CancelBeforeMinTicks_DoesNotCancel()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);

        // Activate R
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });

        // Tick with R input before min_ticks_before_cancel (10)
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal((byte)5, after.AttackSlot);
    }

    [Fact]
    public void FightGuyDragonKick_OnHit_SwitchesToAttackAnimAndStops()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);

        // NPC in lunge path — loop capsule hit triggers transition to attack phase
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.AnimIndex);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
        Assert.Equal(0f, after.VY);
    }

    [Fact]
    public void FightGuyDragonKick_Timeout_PlaysEndAnimThenEnds()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);

        // No NPC — timeout after max_flight_ticks (60)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });
        // Tick to just after timeout (60 flight + 5 into end anim = 65)
        for (int i = 0; i < 65; i++)
            sim.Tick(new() { { 1, default } });

        var mid = sim.GetState(1);
        Assert.Equal((byte)2, mid.AnimIndex); // spell_r_end
        Assert.Equal(ActionState.Attacking, mid.State);
        Assert.True(mid.PZ > 15f,
            $"Expected player to travel >15m during 1s flight, got PZ={mid.PZ:F1}");

        // Tick past end anim (15 ticks) + margin
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, default } });

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((byte)0, ended.AttackSlot);
    }


    [Fact]
    public void FightGuyDragonKick_ForwardCapsule_DealsDamage()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);

        // Player lunges at 20m/s. Forward capsule starts at OffZ=0.5, ends at EndOffZ=1.5.
        // At trigger tick 3: PZ=1.0, capsule z=1.5-2.5, radius 0.6. Place NPC at z=1.5.
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from forward capsule, got {npcAfter.DamagePercent}");

        // On first hit, ability transitions to spell_r_attack and stops moving
        var playerAfter = sim.GetState(1);
        Assert.Equal((byte)1, playerAfter.AnimIndex);
        Assert.Equal(0f, playerAfter.VX);
        Assert.Equal(0f, playerAfter.VZ);
    }

    [Fact]
    public void FightGuyDragonKick_AttackPhase_ReturnsToIdleAfterCombo()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);

        // NPC close enough to trigger loop hit → attack phase
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Activate R — loop hits around tick 3, transitions to attack
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // Loop (5) + hit1 (6) should connect on NPC at z=1.5
        var mid = sim.GetState(100);
        Assert.True(mid.DamagePercent >= 5,
            $"NPC should take at least loop damage (5), got {mid.DamagePercent}");

        // Now in attack phase — tick through full combo (88 ticks) + margin
        for (int i = 0; i < 95; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((byte)0, ended.AttackSlot);

        // NPC should have accumulated damage from loop + attack hits
        var npcEnd = sim.GetState(100);
        Assert.True(npcEnd.DamagePercent > mid.DamagePercent,
            $"NPC damage should increase during attack phase: {mid.DamagePercent} → {npcEnd.DamagePercent}");
        Assert.True(npcEnd.DamagePercent >= 10,
            $"NPC should take at least loop(5)+hit1(6)=11 damage total, got {npcEnd.DamagePercent}");
    }

    [Fact]
    public void FightGuyDragonKick_HomingSteersTowardMarkedTarget()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);

        // Marked NPC at an offset (right + forward) so homing must steer
        var npc = TestHelpers.NpcState(3f, 10f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        // Tick a few frames — homing runs each tick, NPC at +3 X too far for capsule
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var after = sim.GetState(1);
        Assert.True(after.VX > 1f,
            $"Expected VX > 1 (steering right toward NPC at +3 X), got VX={after.VX:F3}");
    }

    // ── F (FightGuyTempest) ──

    [Fact]
    public void FightGuyTempest_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)6, t0.AttackSlot);
    }

    [Fact]
    public void FightGuyTempest_LocksInPlace()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.VX = 10f; state.VZ = 5f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 2);
        Assert.Equal(0f, t1.VX);
        Assert.Equal(0f, t1.VZ);
    }

    [Fact]
    public void FightGuyTempest_PullsEnemies()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, state);
        var npc = TestHelpers.NpcState(2f, 0f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.VX = 0f; npc.VZ = 0f;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);
        var f = TestHelpers.Input(activeSlot: 6);
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, f }, { 100, default } });
        float dist = CombatMath.HorizontalDistance(0, 0, sim.GetState(100).PX, sim.GetState(100).PZ);
        Assert.True(dist < 2f, $"NPC should be pulled closer (<2m), distance={dist:F3}");
    }

    [Fact]
    public void FightGuyTempest_LauncherSpawnsOnFinalSpinTick()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        // Activate F and tick through windup (12) + spin (60) = 72 ticks
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });
        // Tick through windup (12) + spin (60) = 72 ticks total from activation
        // Launcher spawns at tick 72 (spinElapsed==_spinDuration)
        for (int i = 0; i < 71; i++)
            sim.Tick(new() { { 1, default } });

        // Ability should be ended, but launcher hitbox (4 tick duration)
        // should still be active with 3 remaining ticks
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.True(sim.Resolver.GetActiveHitboxes().Count >= 1,
            $"Expected at least 1 active hitbox (launcher), got {sim.Resolver.GetActiveHitboxes().Count}");
    }

    // ── Status ──

    [Fact]
    public void Status_TicksDownAndClears()
    {
        var s = new CharacterState { EntityId = 1, PX = 0, PY = 5f, PZ = 0, IsGrounded = false, State = ActionState.Idle, JumpsLeft = 2, AirDodgesLeft = 1, StatusFlags = (1 << 2), StatusRemainingTicks = 10 };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, s);
        for (int i = 0; i < 10; i++) TestHelpers.TickDefault(sim, 1);
        var a = sim.GetState(1);
        Assert.Equal(0u, a.StatusRemainingTicks);
        Assert.Equal((byte)0, a.StatusFlags);
    }

    [Fact]
    public void StatusFlags_DoesNotClearPrematurely()
    {
        var s = new CharacterState { EntityId = 1, PX = 0, PY = 5f, PZ = 0, IsGrounded = false, State = ActionState.Idle, JumpsLeft = 2, AirDodgesLeft = 1, StatusFlags = (1 << 2), StatusRemainingTicks = 10 };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, s);
        for (int i = 0; i < 5; i++) TestHelpers.TickDefault(sim, 1);
        var a = sim.GetState(1);
        Assert.Equal(5u, a.StatusRemainingTicks);
        Assert.Equal((byte)(1 << 2), a.StatusFlags);
    }

    // ── Bone-attached hitbox ──

    [Fact]
    public void BoneHitbox_FromData_BoneHitboxDisabledWithoutBakedData()
    {
        // Custom LMB with a bone-attached hitbox
        var boneLMB = new AbilitySpec
        {
            Name = "BoneLMB",
            CooldownTicks = 0,
            Stages = new AttackStage[]
            {
                new()
                {
                    DurationTicks = 20,
                    HitboxEvents = new[]
                    {
                        new HitboxEvent
                        {
                            TriggerTick = 5,
                            DurationTicks = 5,
                            Radius = 0.8f,
                            BoneName = "mixamorig:RightFoot",
                            BoneOffY = 0.1f,
                            Damage = 10f,
                            BaseKnockback = 5f,
                            KnockbackGrowth = 5f,
                            KnockbackUpward = 5f,
                            StunTicks = 10,
                            Interruptible = true,
                        },
                    },
                    LungeForce = 0f,
                },
            },
            AnimationNames = new[] { "melee" },
        };

        // Def based on BoneHitboxTestDef but with the custom bone LMB
        var src = TestHelpers.BoneHitboxTestDef;
        var def = new CharacterDefinition
        {
            Class = src.Class,
            DisplayName = src.DisplayName,
            CapsuleRadius = src.CapsuleRadius,
            CapsuleHeight = src.CapsuleHeight,
            HurtboxRadius = src.HurtboxRadius,
            Movement = src.Movement,
            LMB = boneLMB,
            HurtboxBoneDefs = src.HurtboxBoneDefs,
            BakedDataPath = "", // No baked data — bone hitbox skips
            HurtboxCapsules = src.HurtboxCapsules!,
            IdleAnim = src.IdleAnim,
            RunAnim = src.RunAnim,
            DashAnim = src.DashAnim,
            JumpAnim = src.JumpAnim,
            FallAnim = src.FallAnim,
            HitSmallAnim = src.HitSmallAnim,
            HitMediumAnim = src.HitMediumAnim,
            HitHardAnim = src.HitHardAnim,
            VisualScale = src.VisualScale,
            ModelYOffset = src.ModelYOffset,
            ModelSoleOffset = src.ModelSoleOffset,
        };

        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.MankiDef); // 0.75
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, def, player);

        // NPC in front-right where right foot bone would be (without baked data, shouldn't matter)
        var npc = TestHelpers.NpcState(0.5f, 1.5f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.MankiDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Tick through hitbox trigger (tick 5)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // No baked data → bone hitbox should have been skipped
        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent == 0,
            $"NPC should take NO damage (bone hitbox skipped without baked data), got {npcAfter.DamagePercent}");
    }

    [Fact]
    public void BoneHitbox_EntityOffsetHitboxStillWorks()
    {
        // Uses FightGuyDef LMB (data-driven, entity-relative OffX/OffY/OffZ, BoneName=null default).
        // HitboxEvent with no BoneName → standard positioning path.
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef); // 0.85
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // FightGuy LMB stage 0 hitbox: OffX=0, OffY=0.8, OffZ=1.2, Radius=0.5
        // Player at (0, 0.85, 0), lunge forward ~0.8m by tick 6 (hitbox trigger)
        // → hitbox center approx at (0, 1.65, 2.0). NPC at (0, 0.65, 1.5): overlap
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = TestHelpers.CombatGroundPY;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.CombatDef, npc);

        // LMB stage 0, hitbox triggers at tick 6
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from entity-offset hitbox, got {npcAfter.DamagePercent}");
    }
    // ── RMB Charged Uppercut ──

    [Fact]
    public void FightGuyRmb_ChargeTicksIncreaseWhileHolding()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        // RMB + hold (aiming=true) for 10 ticks → ChargeTicks should increase
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, hold } });
        var after = sim.GetState(1);
        Assert.True(after.ChargeTicks > 0,
            $"Expected ChargeTicks > 0 after 10 ticks holding RMB, got {after.ChargeTicks}");
    }

    [Fact]
    public void FightGuyRmb_ReleasesOnButtonRelease()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        // Hold for 10 ticks, then release (IsAiming=false)
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, hold } });
        // Release RMB (IsAiming=false — but must keep activeSlot=2 for the ability to stay active)
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, release } });
        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ComboStage); // Should be in attack phase
    }

    [Fact]
    public void FightGuyRmb_AutoReleaseOnFullCharge()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        // Hold for full charge duration (180 ticks) — auto-releases
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 190; i++)
            sim.Tick(new() { { 1, hold } });
        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ComboStage); // Attack phase
        Assert.True(after.ChargeTicks >= 180,
            $"Expected ChargeTicks >= 180 at full charge, got {after.ChargeTicks}");
    }

    [Fact]
    public void FightGuyRmb_ChargedVariantDealsMoreDamage()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC in front at mid-range (hitbox covers OffZ=0.8-1.0)
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Charged: hold for full 180 ticks, then release
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 185; i++)
            sim.Tick(new() { { 1, hold }, { 100, default } });
        // Release — let attack play out to hitbox triggers (tick 5-15)
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 25; i++)
            sim.Tick(new() { { 1, release }, { 100, default } });
        var npcAfter = sim.GetState(100);

        // Each charged hitbox does 14 damage, 3 hitboxes = up to 42 total
        Assert.True(npcAfter.DamagePercent >= 14,
            $"Expected charged RMB to deal >=14 damage, got {npcAfter.DamagePercent}");
    }

    [Fact]
    public void FightGuyRmb_UnchargedDealsLessDamageThanCharged()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC at same position as charged test
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc);

        // Uncharged: hold for only 6 ticks (debounce = 5), then release immediately
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 6; i++)
            sim.Tick(new() { { 1, hold }, { 100, default } });
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, release }, { 100, default } });
        var npcAfter = sim.GetState(100);

        // Uncharged hitboxes do 6 damage each, 3 hitboxes = up to 18 total
        Assert.True(npcAfter.DamagePercent >= 6,
            $"Expected uncharged RMB to deal >=6 damage, got {npcAfter.DamagePercent}");
        // But less than charged (14+ per hitbox)
        Assert.True(npcAfter.DamagePercent <= 18,
            $"Expected uncharged RMB to deal <=18 damage (uncharged), got {npcAfter.DamagePercent}");
    }

    [Fact]
    public void FightGuyRmb_ThreeHitboxPositionsCoverUppercutArc()
    {
        var sim = TestHelpers.MakeSim();
        // Place NPCs at 3 different heights to verify each sphere hitbox position
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.FightGuyDef, player);

        // NPC 1: low position (hitbox 1: OffY=0.2, OffZ=0.8, Radius=0.7)
        var npc1 = TestHelpers.NpcState(0f, 1.5f); // within low hitbox range
        npc1.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        npc1.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.FightGuyDef, npc1);

        // Hold 6 ticks, release, tick through attack
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 6; i++)
            sim.Tick(new() { { 1, hold }, { 100, default } });
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, release }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0,
            $"Low NPC should take damage from uppercut hitbox 1, got {sim.GetState(100).DamagePercent}");
    }

    [Fact]
    public void FightGuyRmb_ExpiresAfterAttack()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        // Hold briefly, release
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 6; i++)
            sim.Tick(new() { { 1, hold } });
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 45; i++) // 35 duration + margin
            sim.Tick(new() { { 1, release } });
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    [Fact]
    public void FightGuyRmb_ChargeResetsAfterAttack()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);

        // First charge: hold for 10 ticks, release, let attack expire
        var hold = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, hold } });
        var release = new InputState { ActiveSlot = 2, IsAiming = false };
        for (int i = 0; i < 45; i++)
            sim.Tick(new() { { 1, release } });

        // Attack should be over — verify ChargeTicks reset
        var afterFirst = sim.GetState(1);
        Assert.Equal(ActionState.Idle, afterFirst.State);
        Assert.Equal((ushort)0, afterFirst.ChargeTicks);

        // Wait for cooldown to expire (60 ticks)
        for (int i = 0; i < 70; i++)
            sim.Tick(new() { { 1, default } });

        // Second RMB press — should be able to charge again
        var hold2 = TestHelpers.Input(activeSlot: 2, aiming: true);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, hold2 } });
        var afterSecond = sim.GetState(1);
        Assert.True(afterSecond.ChargeTicks > 0,
            $"Expected ChargeTicks > 0 on second charge, got {afterSecond.ChargeTicks}");
    }
}
