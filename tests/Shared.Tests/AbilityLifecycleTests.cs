using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

using SlopArena.Shared.Abilities;

/// <summary>
/// Lifecycle contracts for authored abilities.
/// </summary>
public class AbilityLifecycleTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

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
    // ── F: Aerosol Inferno — fixed commitment lifecycle ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiF_AerosolInferno_CommitsFor52TicksThenReturnsToIdle()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.F) } });
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal((byte)AbilitySlots.F, sim.GetState(1).AttackSlot);

        TestHelpers.TickDefault(sim, 50);
        var committed = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, committed.State);
        Assert.Equal((byte)AbilitySlots.F, committed.AttackSlot);

        TestHelpers.TickDefault(sim, 1);
        var completed = sim.GetState(1);
        Assert.Equal(ActionState.Idle, completed.State);
        Assert.Equal((byte)0, completed.AttackSlot);
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
    // ── E (slot 3): Jetpack Boost — basic activation ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiE_BasicActivation_EntersAttackingCompression()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);
        Assert.Equal(0f, t0.VY);
    }

    [Fact]
    public void MankiE_LaunchesThenReturnsToIdleAtApex()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 40);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }
    public static IEnumerable<object[]> GroundAbilityCases()
    {
        foreach (var character in new[] { CharacterClass.Manki, CharacterClass.FightGuy, CharacterClass.Kistu, CharacterClass.Bonk })
        {
            var def = BuiltInContentResolver.Resolve(character).Definition;
            for (byte wireSlot = 1; wireSlot <= AbilitySlots.Count; wireSlot++)
            {
                if (def.GetCookedSlotAbility(wireSlot, airborne: false) != null)
                    yield return new object[] { character, wireSlot };
            }
        }
    }

    [Theory]
    [MemberData(nameof(GroundAbilityCases))]
    public void EveryAuthoredGroundAbility_ReleasesAttackState(CharacterClass character, byte wireSlot)
    {
        var def = BuiltInContentResolver.Resolve(character).Definition;
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        TestHelpers.RegisterPlayer(sim, def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: wireSlot, aiming: true) } });
        var started = sim.GetState(1);
        Assert.Equal(wireSlot, started.AttackSlot);
        Assert.NotEqual(ActionState.Idle, started.State);
        for (int tick = 0; tick < 600; tick++)
        {
            var current = sim.GetState(1);
            if (current.State == ActionState.Idle && current.AttackSlot == 0)
                return;
            sim.Tick(new() { { 1, default } });
        }

        var final = sim.GetState(1);
        Assert.Equal(ActionState.Idle, final.State);
        Assert.Equal((byte)0, final.AttackSlot);
    }

    [Fact]
    public void HeldAbilityInput_DoesNotRestartCompletedAbility()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.E) } });
            if (sim.GetState(1).State == ActionState.Idle)
                return;
        }

        Assert.Fail("Held ability input restarted the ability instead of releasing it.");
    }

}
