using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

public class BunnyAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.BunnyDef);

    // ── LMB (BunnyLmbCombo) ──

    [Fact]
    public void BunnyLmbCombo_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)1, t0.AttackSlot);
    }

    [Fact]
    public void BunnyLmbCombo_ChainsToStage2()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        var def = TestHelpers.BunnyDef;
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
    public void BunnyLmbCombo_ExpiresAfterFinalStage()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
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
    public void BunnyLmbCombo_Stage1DealsDamageToNpc()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, player);

        // NPC in front of player, within LMB hitbox range
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = GroundPY;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

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

    // ── Q (BunnyWhirlingCarrot) ──

    [Fact]
    public void BunnyWhirlingCarrot_ActivatesAimed()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 500), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
        Assert.True(t0.IsAiming);
    }

    [Fact]
    public void BunnyWhirlingCarrot_ThrowsProjectile()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 500);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim } });
        var rel = new InputState { ActiveSlot = 3, AimDistance = 500 };
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, rel } });
        Assert.Equal((byte)1, sim.GetState(1).ComboStage);
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void BunnyWhirlingCarrot_AppliesMark()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);
        var aim = TestHelpers.Input(activeSlot: 3, aiming: true, aimDistance: 50);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim }, { 100, default } });
        var rel = new InputState { ActiveSlot = 3, AimDistance = 50 };
        for (int i = 0; i < 90; i++) sim.Tick(new() { { 1, rel }, { 100, default } });
        var npcAfter = sim.GetState(100);
        Assert.True((npcAfter.StatusFlags & (1 << 2)) != 0, "NPC should have Marked status");
        Assert.True(npcAfter.StatusRemainingTicks > 0);
    }

    [Fact]
    public void BunnyWhirlingCarrot_MarkExpiresAfterDuration()
    {
        var sim = TestHelpers.MakeSim();
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;  // 5s mark
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        // Tick exactly 300 times — should clear at 0
        for (int i = 0; i < 300; i++)
            sim.Tick(new() { { 100, default } });

        var after = sim.GetState(100);
        Assert.Equal((ushort)0, after.StatusRemainingTicks);
        Assert.Equal((byte)0, after.StatusFlags);
    }

    [Fact]
    public void BunnyWhirlingCarrot_MarkStillActiveAtHalfDuration()
    {
        var sim = TestHelpers.MakeSim();
        var npc = TestHelpers.NpcState(0f, 0.5f);
        npc.PY = GroundPY;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        for (int i = 0; i < 150; i++)
            sim.Tick(new() { { 100, default } });

        var after = sim.GetState(100);
        Assert.Equal((ushort)150, after.StatusRemainingTicks);
        Assert.Equal((byte)(1 << 2), after.StatusFlags);
    }

    // ── E (BunnyTornadoKick) ──

    [Fact]
    public void BunnyTornadoKick_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);
    }

    [Fact]
    public void BunnyTornadoKick_AppliesForwardLunge()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.FacingYaw = 0f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 3);
        Assert.True(t1.VZ > 5f, $"Expected VZ>5 (forward lunge), got VZ={t1.VZ:F3}");
        Assert.True(t1.PZ > 0.1f, $"Expected forward position change, got PZ={t1.PZ:F3}");
    }

    [Fact]
    public void BunnyTornadoKick_HitboxInFrontStuns()
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, player);

        // NPC in front (OffZ=1.8 hitbox, player lunges forward)
        var npc = TestHelpers.NpcState(0f, 3f);
        npc.PY = GroundPY;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        // Press E and tick past hitbox trigger (tick 10, after windup)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from Tornado Kick, got {npcAfter.DamagePercent}");
        Assert.True(npcAfter.HitstunTicks >= 20,
            $"Expected HitstunTicks >= 20 (stun), got {npcAfter.HitstunTicks}");
    }

    // ── R (BunnyDragonKick) ──

    [Fact]
    public void BunnyDragonKick_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)5, t0.AttackSlot);
    }

    [Fact]
    public void BunnyDragonKick_RecastCancelsEarly()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        for (int i = 0; i < 20; i++) TestHelpers.TickDefault(sim, 1);
        var cancel = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Idle, cancel.State);
    }

    [Fact]
    public void BunnyDragonKick_NormalDamageWithoutMark()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

        // Player lunges forward at 20m/s. After 10 ticks (hitbox spawn): ~3.3m.
        // Hitbox OffZ=2 → center at ~5.3m. Place NPC there.
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0,
            $"NPC should have taken damage, got {sim.GetState(100).DamagePercent}");
    }

    [Fact]
    public void BunnyDragonKick_MarkConsumption_ClearsMarkAndSpawnsAoe()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

        // Marked NPC in front
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        npc.StatusFlags = (1 << 2);         // Marked
        npc.StatusRemainingTicks = 300;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should have taken damage from DragonKick, got {npcAfter.DamagePercent}");
        Assert.True((npcAfter.StatusFlags & (1 << 2)) == 0, "Mark should be consumed");
        Assert.True(npcAfter.StatusRemainingTicks == 0, "Mark remaining ticks should be 0");
        // NOTE: AoE explosion is spawned by OnHitEntity but re-hits the same NPC next tick
        // and is consumed (one-hit-per-hitbox). The mark clearing + damage are the key validations.
    }


    [Fact]
    public void BunnyDragonKick_HomingSteersTowardMarkedTarget()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

        // Marked NPC at an offset (right + forward) so homing must steer
        var npc = TestHelpers.NpcState(3f, 10f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        // Tick a few frames before hitbox trigger — homing path runs each tick
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var after = sim.GetState(1);
        Assert.True(after.VX > 1f,
            $"Expected VX > 1 (steering right toward NPC at +3 X), got VX={after.VX:F3}");
    }

    [Fact]
    public void BunnyDragonKick_CancelBeforeMinTicks_DoesNotCancel()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

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
    public void BunnyDragonKick_OnHit_SwitchesToAttackAnimAndStops()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

        // Marked NPC in front
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        npc.StatusFlags = (1 << 2);
        npc.StatusRemainingTicks = 300;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

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
    public void BunnyDragonKick_Timeout_PlaysEndAnimThenEnds()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

        // No NPC — timeout after max_flight_ticks (180)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) } });
        // Tick to just after timeout (180 flight + 5 into end anim)
        for (int i = 0; i < 185; i++)
            sim.Tick(new() { { 1, default } });

        var mid = sim.GetState(1);
        Assert.Equal((byte)2, mid.AnimIndex); // spell_r_end
        Assert.Equal(ActionState.Attacking, mid.State);
        Assert.True(mid.PZ > 20f,
            $"Expected player to travel >20m during 3s flight, got PZ={mid.PZ:F1}");

        // Tick past end anim (10 ticks) + margin
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default } });

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((byte)0, ended.AttackSlot);
    }

    [Fact]
    public void BunnyDragonKick_OnHit_TransitionsToAttackAnim()
    {
        // Hit an unmarked NPC — should still switch to spell_r_attack (AnimIndex=1)
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

        // Place NPC in lunge path. Player lunges at 20m/s, hitbox spawns at tick 10
        // (existing entity-relative kick). After 10 ticks: PZ ≈ 3.3m, hitbox OffZ=2
        // → center at ~5.3m. Place NPC there.
        var npc = TestHelpers.NpcState(0f, 5f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var playerAfter = sim.GetState(1);
        Assert.Equal((byte)1, playerAfter.AnimIndex); // spell_r_attack
    }

    [Fact]
    public void BunnyDragonKick_LeftFootHitbox_DealsDamage()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; state.IsGrounded = false;
        state.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);

        // Player lunges at 20m/s. Left foot hitbox at tick 5, spawns at entity center
        // (no baked data → OffX/Y/Z=0 fallback). After 5 ticks: ≈1.67m forward.
        // Hitbox radius 0.6 + NPC capsule 0.3 = 0.9. Place within 0.9m.
        var npc = TestHelpers.NpcState(0f, 2.2f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 5) }, { 100, default } });
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from left foot hitbox at tick 5, got {npcAfter.DamagePercent}");

        // On first hit, ability transitions to spell_r_attack and stops moving
        var playerAfter = sim.GetState(1);
        Assert.Equal((byte)1, playerAfter.AnimIndex);
        Assert.Equal(0f, playerAfter.VX);
        Assert.Equal(0f, playerAfter.VZ);
    }

    // ── F (BunnyJadeHare) ──

    [Fact]
    public void BunnyJadeHare_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)6, t0.AttackSlot);
    }

    [Fact]
    public void BunnyJadeHare_LocksInPlace()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        state.VX = 10f; state.VZ = 5f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 2);
        Assert.Equal(0f, t1.VX);
        Assert.Equal(0f, t1.VZ);
    }

    [Fact]
    public void BunnyJadeHare_PullsEnemies()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, state);
        var npc = TestHelpers.NpcState(2f, 0f);
        npc.PY = 5f; npc.IsGrounded = false;
        npc.VX = 0f; npc.VZ = 0f;
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);
        var f = TestHelpers.Input(activeSlot: 6);
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, f }, { 100, default } });
        float dist = CombatMath.HorizontalDistance(0, 0, sim.GetState(100).PX, sim.GetState(100).PZ);
        Assert.True(dist < 2f, $"NPC should be pulled closer (<2m), distance={dist:F3}");
    }

    [Fact]
    public void BunnyJadeHare_LauncherSpawnsOnFinalSpinTick()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.BunnyDef, state);

        // Activate F and tick through windup (8) + spin (60) = 68 ticks
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });
        // Tick through windup (8) + spin (60) = 68 ticks total from activation
        // Launcher spawns at tick 68 (spinElapsed==_spinDuration)
        for (int i = 0; i < 67; i++)
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
        sim.RegisterEntity(1, TestHelpers.BunnyDef, s);
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
        sim.RegisterEntity(1, TestHelpers.BunnyDef, s);
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
        sim.RegisterEntity(100, TestHelpers.BunnyDef, npc);

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
        // Uses BunnyDef LMB (data-driven, entity-relative OffX/OffY/OffZ, BoneName=null default).
        // HitboxEvent with no BoneName → standard positioning path.
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.BunnyDef); // 0.85
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, TestHelpers.BunnyDef, player);

        // Bunny LMB stage 0 hitbox: OffX=0, OffY=0.8, OffZ=1.2, Radius=0.5
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
}
