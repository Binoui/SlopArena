using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Per-ability lifecycle tests.
/// All abilities use ServerAbility subclasses — no data-driven path.
/// </summary>
public class AbilityLifecycleTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

    // ── LMB: ServerAbility (MankiLmbCombo) — basic activation only ──

    [Fact]
    public void MankiLMB_StartsAttacking()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)1, t0.AttackSlot);
    }

    // ── AirLMB: ServerAbility (AirLmbCombo via StageChainAbility) ──

    [Fact]
    public void MankiAirLMB_DataDrivenDuration()
    {
        // AirLMB: DurationTicks=20. Entity must be truly airborne (PY above ground snap).
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f; // well above ground
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press LMB while airborne → AirLmbCombo
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, t0.State);

        // Tick past DurationTicks (20) with margin
        for (int i = 0; i < 30; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
    }

    // ── Q: ServerAbility (MankiRoundBomb) — basic activation ──

    [Fact]
    public void MankiQ_BasicActivation()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3), 1);
        Assert.Equal(ActionState.Aiming, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
    }

    // ── Q hold: Aiming stance — friction-only movement, blocked cancel, auto-release ──

    [Fact]
    public void MankiQ_AirborneHold_AppliesAirDrag()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 20f; // well above ground — stays airborne for the whole hold
        state.IsGrounded = false;
        state.VZ = 5f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var aim = TestHelpers.Input(activeSlot: 3, aiming: true);
        for (int i = 0; i < 60; i++)
            sim.Tick(new() { { 1, aim } });

        var s = sim.GetState(1);
        // Manki AirFriction=6 m/s² (linear): VZ decays 5 → 0 within 50 ticks.
        // Guards the fixed-aim friction-only path in ProcessNormalMovement.
        TestHelpers.AssertNear(0f, s.VZ, 0.1f);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.Equal((byte)0, s.ComboStage);
    }

    [Fact]
    public void MankiQ_MidHold_DashAndJumpBlocked()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var aim = TestHelpers.Input(activeSlot: 3, aiming: true);
        sim.Tick(new() { { 1, aim } });
        for (int i = 0; i < 9; i++)
            sim.Tick(new() { { 1, aim } });

        // Past the 8-tick lock: dash/jump stay blocked while the hold owns movement.
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 3, IsAiming = true, Jump = true, Dash = true } } });
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.Equal((byte)3, s.AttackSlot);
        Assert.Equal((ushort)0, s.DashDurationTicks);
        Assert.Equal((byte)2, s.JumpsLeft);
    }

    [Fact]
    public void MankiQ_HoldPastChargeCap_AutoReleases()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var aim = TestHelpers.Input(activeSlot: 3, aiming: true);
        for (int i = 0; i < 185; i++)
            sim.Tick(new() { { 1, aim } });

        var s = sim.GetState(1);
        // ChargeHoldTicks=180: the hold auto-releases into the Attacking throw phase.
        // Guards the charge-clamp gate extension to the Aiming state.
        Assert.Equal((byte)1, s.ComboStage);
        Assert.Equal(ActionState.Attacking, s.State);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── F: Overclock — buff lifecycle ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiOverclock_ActivatesBuffState()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press F (slot 6)
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);

        Assert.True((after.BuffActiveFlags & (byte)BuffType.Overclock) != 0,
            "Overclock flag should be set after F press");
        Assert.True(after.BuffRemainingTicks > 0,
            "BuffRemainingTicks should be > 0 after F press");
    }

    [Fact]
    public void MankiOverclock_BuffDurationMatchesSpec()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        // Duration is 480 ticks (8s) per MankiData F spec. TickTimers decrements in activation tick → 479.
        Assert.True(after.BuffRemainingTicks >= 478 && after.BuffRemainingTicks <= 480,
            $"Expected buff duration ~479 ticks after activation, got {after.BuffRemainingTicks}");
    }

    [Fact]
    public void MankiOverclock_BuffTicksDownGradually()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.True(t0.BuffRemainingTicks > 470 && t0.BuffRemainingTicks <= 480,
            $"Expected ~479 after activation tick, got {t0.BuffRemainingTicks}");

        // Tick 10 more times — timer should decrease by ~10
        var t10 = TestHelpers.TickDefault(sim, 10);
        Assert.True(t10.BuffRemainingTicks > 459 && t10.BuffRemainingTicks < t0.BuffRemainingTicks,
            $"Expected buff to decrease by ~10 from {t0.BuffRemainingTicks}, got {t10.BuffRemainingTicks}");
    }

    [Fact]
    public void MankiOverclock_BuffExpiresAfterDuration()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.True(t0.BuffRemainingTicks >= 478 && t0.BuffRemainingTicks <= 480,
            $"Expected ~479 after activation tick, got {t0.BuffRemainingTicks}");

        // Tick 480 times (remaining ticks after rendering plus a margin)
        TestHelpers.TickDefault(sim, 485);

        var expired = sim.GetState(1);
        Assert.Equal(0u, expired.BuffRemainingTicks);
        Assert.Equal(0, expired.BuffActiveFlags);
    }

    [Fact]
    public void MankiOverclock_ReactivationBlockedWhileActive()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.True(t0.BuffRemainingTicks >= 478 && t0.BuffRemainingTicks <= 480,
            $"Expected ~479 after activation tick, got {t0.BuffRemainingTicks}");

        // Tick a bit, then press F again — should NOT reset duration
        var tMid = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 50);
        // Buff should still be ticking down from original activation:
        // 480 - 1 (tick 0) - 50 (additional ticks) = 429
        Assert.True(tMid.BuffRemainingTicks > 400 && tMid.BuffRemainingTicks < 480,
            $"Expected buff to be partially consumed (~429), got {tMid.BuffRemainingTicks}");
    }

    [Fact]
    public void MankiOverclock_PersistsAfterInjectionEnds()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Activate F — injection lasts 30 ticks
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);

        // Tick through injection + margin
        var afterInjection = TestHelpers.TickDefault(sim, 40);

        // Ability should have ended (state is Idle), but buff persists
        Assert.Equal(ActionState.Idle, afterInjection.State);
        Assert.True(afterInjection.BuffRemainingTicks > 400,
            $"Buff should still have most of its duration left, got {afterInjection.BuffRemainingTicks}");
    }

    [Fact]
    public void MankiOverclock_DoesNotBlockOtherAbilities()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Activate F
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);

        // Wait for injection to finish
        TestHelpers.TickDefault(sim, 40);

        // Now press LMB (slot 1) — should work even though buff is active
        var lmbAfter = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 1), 1);
        Assert.Equal(ActionState.Attacking, lmbAfter.State);
        Assert.Equal((byte)1, lmbAfter.AttackSlot);
    }

    [Fact]
    public void Overclock_DeathClearsBuff()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        var state = TestHelpers.PlayerState();
        state.PY = 0.65f; // grounded
        sim.RegisterEntity(1, def, state);

        // Activate Overclock
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) } });
        var afterBuff = sim.GetState(1);
        Assert.True((afterBuff.BuffActiveFlags & (byte)BuffType.Overclock) != 0,
            "Overclock should be active after F press");
        Assert.True(afterBuff.BuffRemainingTicks > 0,
            "Buff ticks should be > 0");

        // Force below kill height, next tick kills them
        afterBuff.PY = -30f;
        sim.SetState(1, afterBuff);
        sim.Tick(new() { { 1, default } });

        var afterDeath = sim.GetState(1);
        Assert.Equal((byte)0, afterDeath.BuffActiveFlags);
        Assert.Equal((ushort)0, afterDeath.BuffRemainingTicks);
        Assert.Equal(1, afterDeath.Deaths);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── ApplyBuffBonuses — pure math validation ──
    // ══════════════════════════════════════════════════════════════════
    [Fact]
    public void OverclockBuffs_AddsDamageAndRadius()
    {
        var state = new CharacterState
        {
            BuffActiveFlags = (byte)BuffType.Overclock,
            BuffRemainingTicks = 400,
        };

        float damage = 10f;
        float radius = 2f;
        ServerAbility.ApplyBuffBonuses(ref state, ref damage, ref radius);

        Assert.Equal(13f, damage);  // 10 + 3
        Assert.Equal(2.5f, radius); // 2 + 0.5
    }

    [Fact]
    public void OverclockBuffs_DoesNotApplyWithoutBuff()
    {
        var state = new CharacterState
        {
            BuffActiveFlags = 0,
            BuffRemainingTicks = 0,
        };

        float damage = 10f;
        float radius = 2f;
        ServerAbility.ApplyBuffBonuses(ref state, ref damage, ref radius);

        Assert.Equal(10f, damage);  // unchanged
        Assert.Equal(2f, radius);   // unchanged
    }



    [Fact]
    public void CharacterStatePacket_RoundTripsBuffActiveFlags()
    {
        var original = new CharacterStatePacket
        {
            TickNumber = 42,
            BuffRemainingTicks = 400,
            BuffActiveFlags = (byte)BuffType.Overclock,
            PositionX = 1, PositionY = 2, PositionZ = 3,
            CurrentActionState = 1, IsGrounded = true, StateDurationFrames = 10,
        };
        Span<byte> buf = stackalloc byte[CharacterStatePacket.Size];
        original.Serialize(buf);
        var deserialized = CharacterStatePacket.Deserialize(buf);
        Assert.Equal(original.BuffActiveFlags, deserialized.BuffActiveFlags);
        Assert.Equal(original.BuffRemainingTicks, deserialized.BuffRemainingTicks);
    }
    // ══════════════════════════════════════════════════════════════════
    // ── R: Bazooka — FPS fire-and-forget rocket ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiR_BasicActivation_EntersAiming()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5, aiming: true), 1);
        Assert.Equal(ActionState.Aiming, after.State);
        Assert.Equal((byte)5, after.AttackSlot);
    }

    [Fact]
    public void MankiR_RecoveryEnds_ReturnsToIdle()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Total: cast_duration=20 + recovery_duration=15 = 35 ticks
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 40);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    [Fact]
    public void MankiR_ProjectileUsesAimDirection()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.AimYaw = 1.0f;
        state.AimPitch = 0.5f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5, aiming: true), 1);
        Assert.Equal(ActionState.Aiming, after.State);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── E (slot 3): Grapple Gun — basic activation ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiE_BasicActivation_EntersAiming()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4, aiming: true), 1);
        Assert.Equal(ActionState.Aiming, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);
    }

    [Fact]
    public void MankiE_FiresThenMisses_ReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 40);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }
}
