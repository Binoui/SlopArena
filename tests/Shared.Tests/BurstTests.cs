using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Burst (ADR-0014, issue #99): one per-entity tool on a 60 s cooldown with two uses.
/// Defensive: breaks hitstop/hitstun/knockback locks, grants startup invincibility,
/// shoves the last attacker. Offensive: cancels an attack lock into a forward hitbox
/// with fixed knockback (zero growth = zero damage scaling). Cooldown survives KO.
/// </summary>
public class BurstTests
{
    private static InputState BurstInput() => new InputState { Burst = true };

    /// <summary>Player (entity 1) at origin + NPC (entity 100) at z=npcZ, both grounded CombatDef.</summary>
    private static (ServerSimulation sim, CharacterState player, CharacterState npc) SetupPair(float npcZ)
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var p = TestHelpers.PlayerState();
        p.PY = TestHelpers.CombatGroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.CombatDef, p);
        var n = TestHelpers.NpcState(0f, npcZ);
        n.PY = TestHelpers.CombatGroundPY;
        TestHelpers.RegisterNpc(sim, TestHelpers.CombatDef, n);
        return (sim, p, n);
    }

    // ── Defensive ──

    [Fact]
    public void DefensiveBurst_DuringHitstun_ClearsLockAndKnockback_GrantsInvuln_PushesAttacker_SetsRecovery()
    {
        var (sim, p, _) = SetupPair(npcZ: 3f);
        p.State = ActionState.Hitstun;
        p.HitstunTicks = 20;
        p.KVX = 5f; p.KVY = 3f; p.KVZ = 0f;
        p.LastAttackerEntityId = 100;
        sim.SetState(1, p);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });

        // User: lock + knockback cleared, full stop, startup invuln, recovery + cooldown set
        // (TickTimers decremented the fresh values same tick).
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.Equal(0, s.HitstunTicks);
        Assert.Equal(0, s.HitstopTicks);
        Assert.Equal(0f, s.KVX); Assert.Equal(0f, s.KVY); Assert.Equal(0f, s.KVZ);
        Assert.Equal(0f, s.VX); Assert.Equal(0f, s.VY); Assert.Equal(0f, s.VZ);
        Assert.Equal(BurstConfig.DefensiveInvincibilityTicks - 1, s.InvincibilityTicks);
        Assert.Equal(BurstConfig.DefensiveRecoveryTicks - 1, s.BurstRecoveryTicks);
        Assert.Equal(BurstConfig.CooldownTicks - 1, s.BurstCooldownTicks);
        Assert.Equal((byte)0, s.BurstPending);
        Assert.Equal(0UL, s.LastAttackerEntityId);

        // Attacker: fixed shove magnitude (6), no hitstun lock (stun 0).
        var npc = sim.GetState(100);
        float kvMag = MathF.Sqrt(npc.KVX * npc.KVX + npc.KVY * npc.KVY + npc.KVZ * npc.KVZ);
        TestHelpers.AssertNear(BurstConfig.AttackerPushBaseKnockback, kvMag, 0.1f);
        Assert.NotEqual(ActionState.Hitstun, npc.State);
    }

    [Fact]
    public void DefensiveBurst_DuringHitstop_CancelsPendingLaunch()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var p = TestHelpers.PlayerState();
        p.PY = TestHelpers.CombatGroundPY;
        p.HitstopTicks = 10;
        p.QueuedKBDirX = 1f;
        p.QueuedKBAngle = 20;
        p.QueuedKBBase = 10f;
        p.QueuedKBGrowth = 5f;
        p.QueuedKBStun = 20;
        TestHelpers.RegisterPlayer(sim, TestHelpers.CombatDef, p);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });

        var s = sim.GetState(1);
        Assert.Equal(0, s.HitstopTicks);
        Assert.Equal(0f, s.QueuedKBDirX); Assert.Equal(0f, s.QueuedKBDirZ);
        Assert.Equal(0, s.QueuedKBAngle);
        Assert.Equal(0f, s.QueuedKBBase); Assert.Equal(0f, s.QueuedKBGrowth);
        Assert.Equal(0, s.QueuedKBStun);
        Assert.False(s.QueuedKVOverride);

        // Freeze already consumed the burst decision; the queue is gone — no launch ever fires.
        TestHelpers.TickDefault(sim, 15);
        var after = sim.GetState(1);
        Assert.Equal(0f, after.KVX); Assert.Equal(0f, after.KVY); Assert.Equal(0f, after.KVZ);
        Assert.Equal(0, after.HitstunTicks);
    }

    [Fact]
    public void DefensiveBurst_Invincibility_BeatsTheTriggeringHit()
    {
        var (sim, p, _) = SetupPair(npcZ: 3f);
        p.State = ActionState.Hitstun;
        p.HitstunTicks = 20;
        p.KVX = 5f; p.KVY = 3f;
        p.LastAttackerEntityId = 100;
        sim.SetState(1, p);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });
        TestHelpers.TickDefault(sim, 2);

        // A fresh hit from the (former) attacker arrives inside the startup invincibility.
        var s = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = s.PX + 0.2f, Y = s.PY, Z = s.PZ,
            EndX = s.PX + 0.2f, EndY = s.PY, EndZ = s.PZ,
            Radius = 0.5f,
            Damage = 50f,
            BaseKnockback = 30f,
            KnockbackGrowth = 0f,
            StunTicks = 20,
            DurationTicks = 2,
            OwnerId = 100,
        });
        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var after = sim.GetState(1);
        Assert.Equal(0, after.DamagePercent);
        Assert.Equal(0f, after.KVX); Assert.Equal(0f, after.KVY); Assert.Equal(0f, after.KVZ);
        Assert.True(after.InvincibilityTicks > 0, "invincibility window still active");
    }

    // ── Offensive ──

    [Fact]
    public void OffensiveBurst_MidAttack_CancelsAbilityAndChain_EndsWithIdle()
    {
        var (sim, _, _) = SetupPair(npcZ: 3f);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 1) } });
        Assert.NotNull(sim.GetActiveAbility(1)); // LmbCombo live in stage 1

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });

        var s = sim.GetState(1);
        Assert.Null(sim.GetActiveAbility(1));   // interrupted via State=Idle — no OnEnd
        Assert.Equal(0, s.AnimLockTicks);
        Assert.Equal((byte)0, s.ComboStage);    // chain reset to stage 1
        Assert.Equal((byte)0, s.AttackSlot);
        Assert.Equal(ActionState.Idle, s.State);
        Assert.Equal(BurstConfig.OffensiveRecoveryTicks - 1, s.BurstRecoveryTicks);
        Assert.Equal(BurstConfig.CooldownTicks - 1, s.BurstCooldownTicks);
    }

    [Fact]
    public void OffensiveBurst_Hitbox_FixedKnockback_NoDamageScaling_SmallStun()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var p = TestHelpers.PlayerState();
        p.PY = TestHelpers.CombatGroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.CombatDef, p);
        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = TestHelpers.CombatGroundPY;
        npc.DamagePercent = 400;
        TestHelpers.RegisterNpc(sim, TestHelpers.CombatDef, npc);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 1) } }); // LMB stage 1
        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });                    // burst → hitbox connects

        // Hitstop on the NPC: 1 + 1.5·4 = 7 ticks; the queued launch applies at freeze expiry.
        for (int i = 0; i < 7; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var n = sim.GetState(100);
        float kvMag = MathF.Sqrt(n.KVX * n.KVX + n.KVY * n.KVY + n.KVZ * n.KVZ);
        // Exactly BaseKnockback — growth 0 means zero damage scaling even at 400%.
        TestHelpers.AssertNear(BurstConfig.HitboxBaseKnockback, kvMag, 0.1f);
        Assert.Equal(BurstConfig.HitboxStunTicks, n.HitstunTicks);
        Assert.Equal(404, n.DamagePercent);
        Assert.Equal(ActionState.Hitstun, n.State);
    }

    // ── Cooldown / recovery ──

    [Fact]
    public void BurstCooldown_PersistsThroughRespawn()
    {
        var (sim, p, _) = SetupPair(npcZ: 3f);
        p.State = ActionState.Hitstun;
        p.HitstunTicks = 20;
        p.KVX = 5f; p.KVY = 3f;
        p.LastAttackerEntityId = 100;
        sim.SetState(1, p);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });
        var burstState = sim.GetState(1);
        Assert.Equal(BurstConfig.CooldownTicks - 1, burstState.BurstCooldownTicks);

        // Force a void death below KillHeight (-20).
        burstState.PY = -21f;
        sim.SetState(1, burstState);
        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var respawned = sim.GetState(1);
        Assert.Equal(BurstConfig.CooldownTicks - 2, respawned.BurstCooldownTicks); // decremented + carried, NOT reset
        Assert.Equal((byte)1, respawned.Deaths);
        Assert.Equal(0, respawned.BurstRecoveryTicks); // recovery deliberately NOT carried
        Assert.Equal(0, respawned.DamagePercent);
    }

    [Fact]
    public void Burst_RejectedDuringRecovery()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var p = TestHelpers.PlayerState();
        p.PY = TestHelpers.CombatGroundPY;
        p.State = ActionState.Hitstun;
        p.HitstunTicks = 10;
        p.KVX = 3f;
        p.BurstRecoveryTicks = 10;
        TestHelpers.RegisterPlayer(sim, TestHelpers.CombatDef, p);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });

        var s = sim.GetState(1);
        Assert.Equal(9, s.HitstunTicks);         // lock still ticking normally
        Assert.InRange(s.KVX, 2.5f, 3f);         // knockback NOT zeroed — normal decay only
        Assert.Equal(0, s.InvincibilityTicks);   // no startup telegraph
        Assert.Equal(0, s.BurstCooldownTicks);   // not spent
        Assert.Equal(9, s.BurstRecoveryTicks);   // recovery decremented normally
    }

    [Fact]
    public void Burst_RejectedDuringCooldown()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var p = TestHelpers.PlayerState();
        p.PY = TestHelpers.CombatGroundPY;
        p.State = ActionState.Hitstun;
        p.HitstunTicks = 10;
        p.KVX = 3f;
        p.BurstCooldownTicks = 100;
        TestHelpers.RegisterPlayer(sim, TestHelpers.CombatDef, p);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });

        var s = sim.GetState(1);
        Assert.Equal(9, s.HitstunTicks);
        Assert.Equal(0, s.InvincibilityTicks);
        Assert.Equal(99, s.BurstCooldownTicks);
    }

    [Fact]
    public void OffensiveBurst_LmbChain_ResetsToStage1()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var p = TestHelpers.PlayerState();
        p.PY = TestHelpers.CombatGroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.CombatDef, p);
        ushort stage1Duration = TestHelpers.CombatDef.LMB!.Stages[0].DurationTicks;

        // Stage 1 → 2: press LMB, buffer a second press, run to stage end (MankiLmbTests pattern).
        sim.Tick(new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 1) } });
        sim.Tick(new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 1) } });
        for (int i = 2; i < stage1Duration; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var mid = sim.GetState(1);
        Assert.True(mid.ComboStage >= 1, $"expected chain to advance, got ComboStage={mid.ComboStage}");

        // Burst resets the LMB chain to stage 1.
        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });
        Assert.Equal((byte)0, sim.GetState(1).ComboStage);

        // Wait out the short offensive recovery, then a fresh LMB press starts a new stage-1 combo.
        for (int i = 1; i < BurstConfig.OffensiveRecoveryTicks; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
        Assert.Equal(0, sim.GetState(1).BurstRecoveryTicks);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 1) } });
        var after = sim.GetState(1);
        Assert.Equal((byte)0, after.ComboStage); // stage 1, not a continuation
        Assert.NotNull(sim.GetActiveAbility(1));
        Assert.Equal((byte)0, after.AnimIndex);
    }
}
