using Xunit;
using SlopArena.Shared;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Issue #117 — the 3-state ground/air slot semantics and the FightGuy kit rework:
///   - null air spec = grounded-only (Tempest; slot 5 empty)
///   - normals 1-4 have DISTINCT air specs (AirSlot1-4 — the air normal pass)
///   - Air* = ground spec reference = shared (Rising Dragon, Cyclone, Ki Shot)
///   - the E-slot rising kick: anti-air on the ground, recovery burst in the air
///   - cooldowns on slots 6-11 now tick down (TickTimers loop fix)
/// </summary>
public class Kit117SlotSemanticsTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static readonly float GroundPy = TestHelpers.GroundPY(Def);

    private static CharacterState AirborneState()
    {
        var s = TestHelpers.PlayerState();
        s.PY = GroundPy + 5f;
        s.IsGrounded = false;
        s.AirTimeTicks = 100;
        return s;
    }

    // ── GetSlotAbility air resolution (unit) ──

    [Fact]
    public void GetSlotAbility_NormalsHaveDistinctAirSpecs()
    {
        // Normals 1-4 (slot indices 2, 6, 7, 8) each declare a DISTINCT air spec —
        // AirSlot1-4 are separate objects, not aliases of the ground spec.
        Assert.NotNull(Def.GetSlotAbility(2, true));   // Low Kick → Double Punch
        Assert.NotNull(Def.GetSlotAbility(6, true));   // Roundhouse → Floating Kick
        Assert.NotNull(Def.GetSlotAbility(7, true));   // Double Uppercut → High Kick
        Assert.NotNull(Def.GetSlotAbility(8, true));   // Tornado Kick → Air Tornado
        Assert.NotSame(Def.GetSlotAbility(2, false), Def.GetSlotAbility(2, true));
        Assert.NotSame(Def.GetSlotAbility(6, false), Def.GetSlotAbility(6, true));
        Assert.NotSame(Def.GetSlotAbility(7, false), Def.GetSlotAbility(7, true));
        Assert.NotSame(Def.GetSlotAbility(8, false), Def.GetSlotAbility(8, true));
        Assert.Null(Def.GetSlotAbility(5, true));      // Tempest — ult is grounded-only
        Assert.NotNull(Def.GetSlotAbility(2, false));
    }

    [Fact]
    public void GetSlotAbility_SharedAbilitySlots_ResolveInAir()
    {
        // Rising Dragon / Cyclone / Ki Shot share their spec across states (Air* = ground ref).
        Assert.Same(Def.E, Def.GetSlotAbility(3, true));
        Assert.Same(Def.R, Def.GetSlotAbility(4, true));
        Assert.Same(Def.A, Def.GetSlotAbility(10, true));
        Assert.Same(Def.E, Def.GetSlotAbility(3, false));
    }

    // ── Grounded-only gating (behavior) ──

    [Fact]
    public void AirNormal_ActivatesWhileAirborne()
    {
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, AirborneState());

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3), 1); // key 1 air — Double Punch
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
    }

    [Fact]
    public void NormalSlot1_ActivatesOnGround()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3), 1); // Low Kick
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
    }

    [Fact]
    public void Tempest_RejectedWhileAirborne()
    {
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, AirborneState());

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 6), 1);
        Assert.Equal(ActionState.Idle, t0.State);
        Assert.Equal((byte)0, t0.AttackSlot);
    }

    [Fact]
    public void EmptySlot5_Noop_NoCrash()
    {
        // Key "5" has no data — the old code NRE'd on spec.Stages; must be a silent no-op.
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, TestHelpers.PlayerState());

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 10), 1);
        Assert.Equal(ActionState.Idle, t0.State);
        Assert.Equal((byte)0, t0.AttackSlot);
    }

    // ── Shared abilities in the air ──

    [Fact]
    public void KiShot_Shared_ActivatesWhileAirborne()
    {
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, AirborneState());

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 11, aiming: true, aimDistance: 500), 1);
        Assert.Equal(ActionState.Aiming, t0.State);
        Assert.Equal((byte)11, t0.AttackSlot);
    }

    // ── E-slot Rising Dragon ──

    [Fact]
    public void RisingDragon_Ground_LaunchesUpward()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.True(t0.VY > 10f, $"expected upward burst (VY>10), got VY={t0.VY:F3}");
        Assert.True(t0.AnimLockTicks > 0);
    }

    [Fact]
    public void RisingDragon_Ground_SpawnsHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        // Hitbox triggers at tick 6 of the 24-tick duration.
        for (int i = 0; i < 10 && sim.Resolver.GetActiveHitboxes().Count == 0; i++)
            sim.Tick(new() { { 1, default } });
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void RisingDragon_Air_ResetsFloatWindow()
    {
        // The ADR-0015 recovery contract: airborne use resets AirTimeTicks (FloatWindow).
        var sim = TestHelpers.MakeSim();
        var state = AirborneState();
        Assert.True(state.AirTimeTicks > 0);
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        // Reset at activation (100 → 0), then the air-time counter +1s during the same tick.
        Assert.True(t0.AirTimeTicks < 2, $"expected FloatWindow reset, got AirTimeTicks={t0.AirTimeTicks}");
        Assert.True(t0.VY > 0f, $"expected upward burst in the air, got VY={t0.VY:F3}");
    }

    // ── Cooldowns on slots 6-11 (TickTimers fix) ──

    [Fact]
    public void KiShot_Cooldown_AppliesAndExpires()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Fire Ki Shot: aim, then release to throw.
        var aim = TestHelpers.Input(activeSlot: 11, aiming: true, aimDistance: 500);
        for (int i = 0; i < 15; i++) sim.Tick(new() { { 1, aim } });
        var rel = new InputState { ActiveSlot = 11, AimDistance = 500 };
        for (int i = 0; i < 90; i++) sim.Tick(new() { { 1, rel } });

        ushort cd = sim.GetState(1).GetCooldown(AbilitySlots.A);
        Assert.True(cd > 0, $"expected cooldown on the Q slot after firing, got {cd}");

        // The 120-tick cooldown must expire (slots 6-11 decrement since issue #117).
        for (int i = 0; i < 130; i++) sim.Tick(new() { { 1, default } });
        Assert.Equal((ushort)0, sim.GetState(1).GetCooldown(AbilitySlots.A));
    }
}
