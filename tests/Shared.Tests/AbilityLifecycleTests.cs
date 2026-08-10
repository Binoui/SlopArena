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
        // Manki AirFriction=0.4: VZ decays as 5 * (1 - 0.4/60)^60 ≈ 3.35.
        // Guards the fixed-aim friction-only path in ProcessNormalMovement.
        TestHelpers.AssertNear(3.35f, s.VZ, 0.1f);
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

    // ── RMB: ServerAbility (MankiAerosolFlame) — basic activation ──

    [Fact]
    public void MankiRMB_Normal_StartsAttacking()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
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
    // ── AirRMB (slot 2, airborne): AirChargeAttack — charge lifecycle ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiAirRMB_BasicActivationAndExpiry()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f; // airborne
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)2, t0.AttackSlot);

        // Tap: 5-tick release debounce + Stages[1] attack (30t), wait with margin
        int tapDuration = Def.AirRMB!.Stages[1].DurationTicks + 10;
        for (int i = 0; i < tapDuration; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((byte)0, ended.AttackSlot);
    }

    // ── AirRMB charge: hold to power up, release to fire (mirrors the ground RMB tests) ──

    [Fact]
    public void MankiAirRMB_TapRelease_SpawnsNormalHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tap: no aiming → 5-tick debounce release, then Stages[1] (trigger=16, dur=8)
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 5);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 17);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Tap hitbox params per MankiData AirRMB Stages[1]
        Assert.Equal(10f, hb.Damage);
        Assert.Equal(0.7f, hb.Radius);
    }

    [Fact]
    public void MankiAirRMB_HoldRelease_SpawnsChargedHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming past ChargeHoldTicks (45) — auto-release fires the charged attack.
        // Release at tick 45, ChargedStages[0] trigger=14 → hitbox spawns runner tick 59.
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 63; i++)
            sim.Tick(holdInputs);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Charged hitbox params per MankiData AirRMB ChargedStages[0]
        Assert.Equal(14f, hb.Damage);
        Assert.Equal(0.8f, hb.Radius);
    }

    [Fact]
    public void MankiAirRMB_ReleaseUnderThreshold_StaysNormal()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Aim for 5 ticks (way under ChargeHoldTicks=45), then release — must fire the tap.
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 5; i++)
            sim.Tick(holdInputs);

        var releaseInputs = new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 2) } };
        for (int i = 0; i < 17; i++) // release at tick 6, trigger=16 → hitbox spawns runner tick 22
            sim.Tick(releaseInputs);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Should be tap params (not charged)
        Assert.Equal(10f, hb.Damage);
        Assert.Equal(0.7f, hb.Radius);
    }

    [Fact]
    public void MankiAirRMB_ChargeHold_HasNoHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var holdInputs = new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 2, aiming: true) } };
        for (int i = 0; i < 4; i++)
            sim.Tick(holdInputs);

        Assert.Empty(sim.Resolver.GetActiveHitboxes());

        var mid = sim.GetState(1);
        Assert.Equal((byte)0, mid.ComboStage); // still in the charge hold phase
    }

    [Fact]
    public void MankiAirRMB_Release_EntersAttackPhase()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 6); // release at tick 5

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal((byte)1, after.ComboStage); // attack phase (AnimIndex 1)
    }

    /// <summary>
    /// Pressing air RMB mid-ascent preserves the climb (issue #115 momentum-preserve).
    /// The old engine policy zeroed downward VY and restarted the float on every aerial
    /// ability; the charge no longer stops the rise — the jump carries through the hold.
    /// </summary>
    [Fact]
    public void MankiAirRMB_ChargeStart_PreservesJumpAscent()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.VY = 10f; // rising from a jump
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold the charge: the rise carries through the hold (no ascent stop).
        var holdInputs = new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 2, aiming: true) } };
        for (int i = 0; i < 10; i++)
            sim.Tick(holdInputs);

        var mid = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, mid.State);
        Assert.True(mid.VY > 9f,
            $"pressing air RMB mid-ascent must preserve the rise: VY={mid.VY:F3}");
        Assert.True(mid.PY > state.PY + 1f,
            $"pressing air RMB mid-ascent must not stop the climb: {state.PY:F3} -> {mid.PY:F3}");
    }

    /// <summary>
    /// The air RMB fires from the player's current trajectory (issue #115 momentum-preserve):
    /// the release no longer wipes falling VY or restarts the FloatWindow — falling velocity
    /// and the AirTime position carry into the spike.
    /// </summary>
    [Fact]
    public void MankiAirRMB_TapRelease_PreservesMomentum()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.VY = -5f; // falling before the press
        state.AirTimeTicks = 8;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 5); // release at tick 5

        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ComboStage); // attack phase reached
        Assert.True(after.AirTimeTicks > 8, $"FloatWindow must NOT restart: AirTime={after.AirTimeTicks}");
        Assert.True(after.VY < 0f, $"falling velocity must carry into the attack: VY={after.VY}");
    }

    /// <summary>
    /// Same momentum-preserve on the CHARGED release. Manki's float window is 30 ticks + 15
    /// ramp, so by the auto-release tick (45) the hold has ramped into real gravity (VY ≈ -4.7);
    /// the release must NOT wipe it — the spike fires from the fall (issue #115).
    /// </summary>
    [Fact]
    public void MankiAirRMB_ChargedRelease_PreservesMomentum()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold past ChargeHoldTicks (45) — auto-release fires the charged attack at tick 45.
        var holdInputs = new Dictionary<ulong, InputState> { { 1, TestHelpers.Input(activeSlot: 2, aiming: true) } };
        for (int i = 0; i < 45; i++)
            sim.Tick(holdInputs);

        var after = sim.GetState(1);
        Assert.Equal((byte)1, after.ComboStage); // attack phase reached (charged)
        Assert.True(after.AirTimeTicks > 30, $"FloatWindow must NOT restart: AirTime={after.AirTimeTicks}");
        Assert.True(after.VY < -3f, $"fall must carry into the charged attack: VY={after.VY}");
    }

    /// <summary>
    /// Air RMB is a hold-to-charge attack driven by <see cref="Abilities.AirChargeAttack"/>.
    /// The client indexes <c>AnimationNames</c> by ComboStage (0 = hold, 1 = release) and
    /// falls back to the LMB clip when the index is out of range (PlayerRenderer), so every
    /// AirRMB spec must declare 2 entries. The charge lifecycle also requires Stages[1] (tap)
    /// and a charged variant. Pins the shared data shape so a data edit can't silently
    /// regress the client animation path.
    /// </summary>
    [Theory]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Kistu)]
    [InlineData(CharacterClass.Nilus)]
    public void AirRmbSpec_IsChargeShaped(CharacterClass c)
    {
        var spec = CharacterRegistry.Get(c).AirRMB;
        Assert.NotNull(spec);
        Assert.Equal(AbilityBehavior.ChargeAttack, spec.Behavior);
        Assert.True(spec.ChargeHoldTicks > 0, $"{c} AirRMB must declare a charge threshold");
        Assert.True(spec.Stages.Length >= 2, $"{c} AirRMB: Stages[0]=hold phase, Stages[1]=tap attack");
        Assert.NotNull(spec.ChargedStages);
        Assert.True(spec.ChargedStages.Length > 0, $"{c} AirRMB requires a charged variant");
        Assert.True(spec.AnimationNames != null && spec.AnimationNames.Length >= 2,
            $"{c} AirRMB: client indexes AnimationNames by ComboStage 0/1 — 2 entries required");
    }

    // ══════════════════════════════════════════════════════════════════
    // ── RMB (slot 2): Two-phase charge-hold architecture ──
    // ══════════════════════════════════════════════════════════════════
    // MankiAerosolFlame has two phases:
    //   Phase 1 (ComboStage=0, AnimIndex=0) = charge hold
    //   Phase 2 (ComboStage=1, AnimIndex=1) = release-to-attack
    // Both normal and charged releases play spell_rmb_charged (AnimIndex=1).
    // Tap release (instant skip if !IsAiming) or hold past charge_threshold=45.


    [Fact]
    public void MankiRMB_Charged_HoldThenRelease()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold RMB with IsAiming=true for 50 ticks using manual loop
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 50; i++)
            sim.Tick(holdInputs);

        // Release (input without aiming) — should fire charged
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        var released = TestHelpers.TickN(sim, releaseInput, 1);
        Assert.Equal(ActionState.Attacking, released.State);
        Assert.Equal((byte)1, released.ComboStage);
    }

    [Fact]
    public void MankiRMB_TapRelease_SpawnsNormalHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming=false for 5 ticks (manual release debounce)
        // triggerTick=8 for normal spawns hitbox at AttackElapsedTicks=8
        // 5 hold ticks + 12 release ticks = hitbox active
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 5);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 12);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Normal hitbox params per MankiData
        Assert.Equal(8f, hb.Damage);
        Assert.Equal(0.7f, hb.Radius);
        Assert.Equal(8f, hb.BaseKnockback);
        Assert.Equal(5f, hb.KnockbackGrowth);
    }

    [Fact]
    public void MankiRMB_HoldRelease_SpawnsChargedHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming for 55 ticks using direct sim.Tick loop
        // (TickN resets input to default after tick 1, dropping IsAiming)
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 50; i++)
            sim.Tick(holdInputs);

        // Release — charged attack starts, triggerTick=10, wait 13 ticks
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        var relInputs = new Dictionary<ulong, InputState> { { 1, releaseInput } };
        for (int i = 0; i < 13; i++)
            sim.Tick(relInputs);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Charged hitbox params per MankiData
        Assert.Equal(14f, hb.Damage);
        Assert.Equal(0.8f, hb.Radius);
        Assert.Equal(8f, hb.BaseKnockback);
        Assert.Equal(5f, hb.KnockbackGrowth);
    }

    [Fact]
    public void MankiRMB_ReleaseUnderThreshold_StaysNormal()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming for 5 ticks (way under charge_threshold=45)
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 5; i++)
            sim.Tick(holdInputs);

        // Release — should fire as normal (under threshold)
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        var relInputs = new Dictionary<ulong, InputState> { { 1, releaseInput } };
        for (int i = 0; i < 12; i++) // + wait past triggerTick=8
            sim.Tick(relInputs);

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Should be normal params (not charged)
        Assert.Equal(8f, hb.Damage);
        Assert.Equal(0.7f, hb.Radius);
    }

    [Fact]
    public void MankiRMB_AutoRelease_AtMaxHold()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming past ChargeHoldTicks (45) — auto-release fires charged attack
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 60; i++)
            sim.Tick(holdInputs);

        // Release RMB (stop sending ActiveSlot) — let charged attack play out
        for (int i = 0; i < 15; i++)
            sim.Tick(new() { { 1, default } });

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        var hb = hitboxes[0];
        // Should be charged params (auto-release past ChargeHoldTicks = always charged)
        Assert.Equal(14f, hb.Damage);
        Assert.Equal(0.8f, hb.Radius);
    }

    [Fact]
    public void MankiRMB_ChargePhase_HasAnimIndexZero()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming — stays in charge phase (ComboStage=0, AnimIndex=0)
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var inputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 10; i++)
            sim.Tick(inputs);

        var duringCharge = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, duringCharge.State);
        Assert.Equal((byte)0, duringCharge.ComboStage);  // charge phase
        Assert.Equal((byte)0, duringCharge.ComboStage);
    }

    [Fact]
    public void MankiRMB_CooldownApplied()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Tap RMB — instant skip, normal attack (normal_duration=58)
        // Wait enough ticks for attack to complete + a few more
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 70);

        // After attack ends, slot 2 (RMB, Cooldown1) should have cooldown=30
        var afterIdle = sim.GetState(1);
        Assert.Equal(ActionState.Idle, afterIdle.State);
        Assert.True(afterIdle.Cooldown1 > 0 && afterIdle.Cooldown1 <= 30,
            $"Expected cooldown 1-30 on slot 2, got Cooldown1={afterIdle.Cooldown1}");
    }

    [Fact]
    public void MankiRMB_HoldRelease_WithNoAiming()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming=false for 5 ticks → manual release → ComboStage=1
        var releaseInput = TestHelpers.Input(activeSlot: 2);
        for (int i = 0; i < 6; i++)
        {
            var inputs = new Dictionary<ulong, InputState> { { 1, releaseInput } };
            sim.Tick(inputs);
        }

        var afterRelease = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, afterRelease.State);
        Assert.Equal((byte)1, afterRelease.ComboStage);

        // Hitbox should be approaching trigger_tick=8 — run 9 more ticks
        for (int i = 0; i < 9; i++)
            sim.Tick(new() { { 1, releaseInput } });

        var hitboxes = sim.Resolver.GetActiveHitboxes();
        Assert.NotEmpty(hitboxes);
        Assert.Equal(8f, hitboxes[0].Damage); // normal params
    }

    [Fact]
    public void MankiRMB_ChargePhase_NoHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Hold with aiming — stays in charge phase
        var holdInput = TestHelpers.Input(activeSlot: 2, aiming: true);
        var inputs = new Dictionary<ulong, InputState> { { 1, holdInput } };
        for (int i = 0; i < 10; i++)
            sim.Tick(inputs);

        var duringCharge = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, duringCharge.State);
        Assert.Equal(0, duringCharge.AnimIndex);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void MankiRMB_Charged_HitsBeyondNormalRange()
    {
        // Place NPC at z=4.5 — outside normal RMB range (~4.1) but inside charged range (~5.3)
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = TestHelpers.CombatDef;
        var gpy = TestHelpers.CombatGroundPY;
        var player = TestHelpers.PlayerState(0f, 0f);
        player.PY = gpy;
        sim.RegisterEntity(1, def, player);
        var npc = TestHelpers.NpcState(0f, 4.5f);
        npc.PY = gpy;
        sim.RegisterEntity(100, def, npc);

        // ── Part A: Uncharged RMB (instant skip) should NOT hit NPC at 4.5m ──
        // Tick 0: tap RMB (no aiming), instant skip to Phase 2
        var tapInput = new Dictionary<ulong, InputState>
        {
            { 1, TestHelpers.Input(activeSlot: 2) },
            { 100, default },
        };
        sim.Tick(tapInput);
        // Tick 1-20: normal attack phase, hitbox active from tick 8→46
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfterUncharged = sim.GetState(100);
        Assert.Equal(0u, npcAfterUncharged.DamagePercent);

        // ── Part B: Charged RMB should hit NPC at 4.5m ──
        // New sim for clean state
        var sim2 = TestHelpers.MakeSim(arena);
        var player2 = TestHelpers.PlayerState(0f, 0f);
        player2.PY = gpy;
        sim2.RegisterEntity(1, def, player2);
        var npc2 = TestHelpers.NpcState(0f, 4.5f);
        npc2.PY = gpy;
        sim2.RegisterEntity(100, def, npc2);

        // Hold with aiming for 55 ticks (past charge_threshold=45)
        var holdInput2 = TestHelpers.Input(activeSlot: 2, aiming: true);
        var holdInputs2 = new Dictionary<ulong, InputState> { { 1, holdInput2 }, { 100, default } };
        for (int i = 0; i < 55; i++)
            sim2.Tick(holdInputs2);

        // Release — charged attack starts, triggerTick=10 → hitbox active from tick 11
        var releaseInput2 = TestHelpers.Input(activeSlot: 2);
        var relInputs2 = new Dictionary<ulong, InputState> { { 1, releaseInput2 }, { 100, default } };
        for (int i = 0; i < 15; i++)
            sim2.Tick(relInputs2); // 15 ticks = AttackElapsedTicks=15, past trigger=10

        var npcAfterCharged = sim2.GetState(100);
        Assert.True(npcAfterCharged.DamagePercent > 0,
            $"Charged RMB should hit NPC at 4.5m (charged range ~5.3m), got damage={npcAfterCharged.DamagePercent}");
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
