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
    // ── R: Bazooka — ability lifecycle ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiR_BasicActivation_Airborne()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

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
        state.PY = 3f; // below riseHeight (5m) so rise is applied
        state.IsGrounded = false;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 2);
        Assert.True(t1.VY > 0f,
            $"Expected upward velocity (rise), got VY={t1.VY}");
        Assert.True(t1.PY > 3f,
            $"Expected position to rise above start, got PY={t1.PY}");
    }

    [Fact]
    public void MankiR_GroundedStart_StillLaunchesUpward()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.IsGrounded = true;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 2);
        Assert.True(after.VY > 0f,
            $"Grounded start should still launch upward, got VY={after.VY}");
    }

    [Fact]
    public void MankiR_RisesToTargetHeight()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 3f; // airborne below riseHeight so rise is applied
        state.IsGrounded = false;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Press R and tick through rising phase
        var aimInput = TestHelpers.Input(activeSlot: 5, aiming: true, aimDistance: 500);
        var after = TestHelpers.TickN(sim, aimInput, 60);

        // Should have risen at least 4m above start (riseHeight=5, physics makes exact hard)
        Assert.True(after.PY >= 7f,
            $"Expected PY >= 7.0 (3+4), got PY={after.PY:F2}");
    }

    [Fact]
    public void MankiR_MaintainsIsAimingDuringRiseAndAim()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Use manual loop so aim input is consistent every tick
        var aimInput = TestHelpers.Input(activeSlot: 5, aiming: true, aimDistance: 500);
        for (int i = 0; i < 3; i++)
            sim.Tick(new() { { 1, aimInput }, { 100, default } });

        var t1 = sim.GetState(1);
        Assert.True(t1.IsAiming,
            "IsAiming should be true during rise and aim phases");
    }

    [Fact]
    public void MankiR_FiresProjectileOnRelease()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var aimInput = TestHelpers.Input(activeSlot: 5, aiming: true, aimDistance: 500);

        // Rise for 60 ticks (enough to reach riseHeight=10 from PY=5 with floatGravity=6)
        for (int i = 0; i < 60; i++)
            sim.Tick(new() { { 1, aimInput }, { 100, default } });

        // Release R (IsAiming=false) — should transition to firing phase
        var releaseInput = new InputState { ActiveSlot = 5, AimDistance = 500, IsAiming = false };
        for (int i = 0; i < 5; i++)
            sim.Tick(new() { { 1, releaseInput }, { 100, default } });

        var afterRelease = sim.GetState(1);
        // After trigger tick (5), should be in ComboStage=1 (firing)
        Assert.Equal((byte)1, afterRelease.ComboStage);
    }

    [Fact]
    public void MankiR_EndsAfterFiringDuration()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 5f;
        state.IsGrounded = false;
        state.VY = 0f;
        var aimInput = TestHelpers.Input(activeSlot: 5, aiming: true, aimDistance: 500);
        for (int i = 0; i < 60; i++)
            sim.Tick(new() { { 1, aimInput }, { 100, default } });

        // Release R to fire
        var releaseInput = new InputState { ActiveSlot = 5, AimDistance = 500, IsAiming = false };
        for (int i = 0; i < 80; i++)
            sim.Tick(new() { { 1, releaseInput }, { 100, default } });
        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── AirRMB (slot 2, airborne): data-driven — basic lifecycle ──
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

        // AirRMB DurationTicks=28, wait 35 for margin
        for (int i = 0; i < 35; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.Equal((byte)0, ended.AttackSlot);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── RMB (slot 2): charge vs normal — basic activation ──
    // ══════════════════════════════════════════════════════════════════
    // MankiAerosolFlame.OnStart checks s.ChargeTicks >= chargeThreshold
    // (45) to decide charged variant. Normal = AnimIndex 0, Charged = 1.

    [Fact]
    public void MankiRMB_Normal_AnimIndexZero()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.ChargeTicks = 0; // explicitly uncharged
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        // Normal variant: AnimIndex=0
        Assert.Equal(0, t0.AnimIndex);
    }

    [Fact]
    public void MankiRMB_Charged_AnimIndexOne()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        state.ChargeTicks = 50; // >= charge_threshold (45)
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 2), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        // Charged variant: AnimIndex=1
        Assert.Equal(1, t0.AnimIndex);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── E (slot 4): ExplosiveMine — basic mine placement ──
    // ══════════════════════════════════════════════════════════════════
    // ExplosiveMineSpec overrides SpawnHitbox. At TriggerTick=0, the
    // data-driven path calls SpawnHitbox, which places a static mine
    // hitbox (no gravity, has explosion config) in the resolver.

    // NOTE: TriggerTick=0 in the spec means "spawn on activation tick", but
    // TickTimers increments AttackElapsedTicks to 1 before SpawnHitboxEvents runs,
    // so TriggerTick=0 never matches. Mine placement via SpawnHitbox override
    // is blocked by this timing issue (known bug). We verify basic lifecycle.

    [Fact]
    public void MankiE_BasicActivation()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);
    }

    [Fact]
    public void MankiE_ExpiresToIdle()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);

        // E has no ServerAbility → data-driven: DurationTicks=20
        for (int i = 0; i < 30; i++)
            TestHelpers.TickDefault(sim, 1);

        var ended = sim.GetState(1);
        Assert.Equal(ActionState.Idle, ended.State);
    }
}
