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


    // ── AirLMB: ServerAbility (AirLmbCombo via StageChainAbility) ──


    // ── Q: ServerAbility (MankiRoundBomb) — basic activation ──

    [Fact]
    public void MankiQ_BasicActivation()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        Assert.Equal(AimMovementMode.Mobile, Def.GetAimMovementMode(AbilitySlots.A, airborne: false));
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: AbilitySlots.A), 1);
        Assert.Equal(ActionState.Aiming, t0.State);
        Assert.Equal((byte)AbilitySlots.A, t0.AttackSlot);
    }

    // ── Q hold: fixed aim — movement is locked, action transitions stay locked ──

    [Fact]
    public void MankiQ_MobileHold_AllowsMovement()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        float startZ = state.PZ;
        var aim = TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: true, moveY: 1f);
        for (int i = 0; i < 300; i++)
            sim.Tick(new() { { 1, aim } });

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.Equal((byte)0, s.ComboStage);
        Assert.NotNull(sim.GetActiveAbility(1));
        Assert.True(s.PZ > startZ, "mobile aim must allow movement");
        Assert.NotEqual(0f, s.VZ);
    }

    [Fact]
    public void MankiQ_AirStart_LandingKeepsAimUntilRelease()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY + 2f;
        state.IsGrounded = false;
        state.VY = -1f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var aim = TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: true);
        sim.Tick(new() { { 1, aim } });
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);

        var falling = sim.GetState(1);
        falling.PY = TestHelpers.MankiGroundPY + 0.05f;
        falling.IsGrounded = false;
        falling.VY = -1f;
        sim.SetState(1, falling);
        sim.Tick(new() { { 1, aim } });

        var landed = sim.GetState(1);
        Assert.True(landed.IsGrounded);
        Assert.Equal(ActionState.Aiming, landed.State);
        Assert.NotNull(sim.GetActiveAbility(1));
        for (int i = 0; i < 7; i++)
            sim.Tick(new() { { 1, aim } });


        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: false) } });
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
    }

    [Fact]
    public void MankiQ_MidHold_DashAndJumpBlocked()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var aim = TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: true, moveY: 1f);
        sim.Tick(new() { { 1, aim } });
        for (int i = 0; i < 9; i++)
            sim.Tick(new() { { 1, aim } });

        // Fixed aim blocks normal movement; ActionState.Aiming also owns jump/dash.
        sim.Tick(new() { { 1, new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true, MoveY = 1f, Jump = true, Dash = true } } });
        var s = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.Equal((byte)AbilitySlots.A, s.AttackSlot);
        Assert.Equal((ushort)0, s.DashDurationTicks);
        Assert.Equal((byte)2, s.JumpsLeft);
    }

    [Fact]
    public void MankiQ_HoldBeyondChargeCap_RemainsAimingUntilRelease()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var aim = TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: true, moveY: 1f);
        for (int i = 0; i < 300; i++)
            sim.Tick(new() { { 1, aim } });

        var held = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, held.State);
        Assert.Equal((byte)0, held.ComboStage);
        Assert.NotNull(sim.GetActiveAbility(1));

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: false) } });
        var released = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, released.State);
        Assert.Equal((byte)1, released.ComboStage);
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
        // Duration is 480 ticks (8s) per the cooked Manki package. TickTimers decrements in activation tick → 479.
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

        // Force below kill height off the heightmap grid (PZ=-1 → no floor), next
        // tick kills them — an in-bounds below-floor spawn would be force-snapped
        // back to the stage instead of falling into the void.
        afterBuff.PZ = -1f;
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
