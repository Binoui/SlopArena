using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Per-ability lifecycle tests.
/// ServerAbility classes (MankiLmbCombo, MankiAerosolFlame, MankiRoundBomb)
/// are partially implemented — tests for them check basic activation only.
/// Data-driven path (no ServerAbility) is fully working.
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

    // ── AirLMB: data-driven, no ServerAbility ──

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

        // Press LMB while airborne → data-driven attack via AirLMB spec
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
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
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

    // ── E: data-driven mine placement ──

    [Fact]
    public void MankiE_DataDriven_ExpiresToIdle()
    {
        // E has no ServerAbility → data-driven path: DurationTicks=20
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);

        // Tick past DurationTicks (20)
        for (int i = 0; i < 30; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((byte)0, ended.AttackSlot);
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

    // ══════════════════════════════════════════════════════════════════
    // ── ApplyBuffBonuses — pure math validation ──
    // ══════════════════════════════════════════════════════════════════

    private sealed class BuffTestAbility : ServerAbility
    {
        public override void OnStart(ref CharacterState s, CharacterDefinition def) { }
        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def) { }

        public void ExposeApplyBuffBonuses(ref CharacterState s, ref float damage, ref float radius)
        {
            ApplyBuffBonuses(ref s, ref damage, ref radius);
        }
    }

    [Fact]
    public void OverclockBuffs_AddsDamageAndRadius()
    {
        var ability = new BuffTestAbility();
        var state = new CharacterState
        {
            BuffActiveFlags = (byte)BuffType.Overclock,
            BuffRemainingTicks = 400,
        };

        float damage = 10f;
        float radius = 2f;
        ability.ExposeApplyBuffBonuses(ref state, ref damage, ref radius);

        Assert.Equal(13f, damage);  // 10 + 3
        Assert.Equal(2.5f, radius); // 2 + 0.5
    }

    [Fact]
    public void OverclockBuffs_DoesNotApplyWithoutBuff()
    {
        var ability = new BuffTestAbility();
        var state = new CharacterState
        {
            BuffActiveFlags = 0,
            BuffRemainingTicks = 0,
        };

        float damage = 10f;
        float radius = 2f;
        ability.ExposeApplyBuffBonuses(ref state, ref damage, ref radius);

        Assert.Equal(10f, damage);  // unchanged
        Assert.Equal(2f, radius);   // unchanged
    }

    // ══════════════════════════════════════════════════════════════════
    // ── R: Dive Bomb — ability lifecycle ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiR_BasicActivation_Airborne()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f; // well above ground
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press R (slot 5, index 4) while airborne
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);

        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal((byte)5, after.AttackSlot);
    }

    [Fact]
    public void MankiR_RisePhase_AppliesUpwardVelocity()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f; // well above ground
        state.IsGrounded = false;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press R — should rise upward with rise_velocity (14f)
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 2);

        // VY should be positive (upward) from rise velocity
        Assert.True(t1.VY > 0f,
            $"Expected upward velocity (rise), got VY={t1.VY}");
        Assert.True(t1.PY > 5f,
            $"Expected position to rise above start, got PY={t1.PY}");
    }

    [Fact]
    public void MankiR_AimPhase_SetsIsAiming()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // During aim window, IsAiming should be true
        // TickN with aim input that has aiming=true + some aim distance
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5, aiming: true), 3);

        Assert.True(t1.IsAiming,
            "IsAiming should be true during the aim window");
    }

    [Fact]
    public void MankiR_TransitionsFromAimingToResolving()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press R with aim input
        var afterRise = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5, aiming: true), 1);

        // Tick through aim window (30 ticks)
        for (int i = 0; i < 35; i++)
            TestHelpers.TickDefault(sim, 1);

        var afterAim = sim.GetState(1);

        // After aim window, IsAiming should be false and character should be plunging down
        Assert.False(afterAim.IsAiming,
            "IsAiming should be false after aim window expires");
        Assert.True(afterAim.VY < 0f,
            $"Should be plunging downward after aim window, got VY={afterAim.VY}");
    }

    [Fact]
    public void MankiR_EndsOnGroundContact()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press R
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5, aiming: true), 1);

        // Tick through aim window (~30) + plunge to ground (~10-20)
        for (int i = 0; i < 80; i++)
            TestHelpers.TickDefault(sim, 1);

        var landed = sim.GetState(1);

        // Ability should have ended — character should be Idle
        Assert.Equal(ActionState.Idle, landed.State);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── AimedGroundAbility — edge cases ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiR_GroundedStart_StillLaunchesUpward()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.IsGrounded = true;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press R while grounded — should still launch upward
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 2);

        Assert.True(after.VY > 0f,
            $"Grounded start should still launch upward, got VY={after.VY}");
    }

    [Fact]
    public void MankiR_AnimLockSetDuringAim()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 2);
        Assert.True(after.AnimLockTicks > 25 && after.AnimLockTicks <= 30,
            $"Expected AnimLockTicks ~29 (30 set, 1 decremented by TickTimers), got {after.AnimLockTicks}");
    }
}
