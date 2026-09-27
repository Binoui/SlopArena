using System;

using System.Collections.Generic;
using Xunit;
using SlopArena.Shared;
using SlopArena.Shared.Abilities;
namespace SlopArena.Shared.Tests;

/// <summary>
/// ADR-0015 §2 refinement (2026-08-12): momentum-preserve is an AERIAL property.
///   - A grounded ability activation zeroes the incoming horizontal velocity (VX/VZ) —
///     grounded moves stop movement.
///   - Aerials ride their trajectory untouched (drift carries into the attack).
///   - AbilitySpec.PreserveMomentumOnStart opts a grounded move out of the stop
///     (dash-attack style moves).
/// The stop happens BEFORE OnStart, so a move's own lunge / OnStart velocity still applies.
/// </summary>
public class AttackMomentumTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static readonly float GroundPy = TestHelpers.GroundPY(Def);

    [Fact]
    public void GroundedNormal_StopsIncomingMomentum()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = GroundPy, VX = 10f, VZ = 5f };
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Low Kick (key 1): no lunge — activation must kill the run momentum.
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1), 2);
        Assert.Equal(ActionState.Attacking, t1.State);
        Assert.Equal(0f, t1.VX);
        Assert.Equal(0f, t1.VZ);
    }

    [Fact]
    public void AerialNormal_PreservesMomentum()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState()
            with { PY = 2f, IsGrounded = false, JumpsLeft = 0, VX = 10f, VZ = 5f };
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Air key 1 (Double Punch): drift rides into the aerial — no zeroing.
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1), 1);
        Assert.Equal(ActionState.Attacking, t1.State);
        Assert.InRange(t1.VX, 9.5f, 10.5f);   // air drag only (no ground friction gate)
        Assert.InRange(t1.VZ, 4.5f, 5.5f);
    }

    [Fact]
    public void GroundedNormal_PreserveMomentumOverride_KeepsVelocity()
    {
        var sim = TestHelpers.MakeSim();
        var def = TestHelpers.CloneDef(TestHelpers.KistuDef);
        def.Slot1 = CloneSpec(def.Slot1!, preserveMomentum: true);
        var groundPy = TestHelpers.GroundPY(def);
        var state = TestHelpers.PlayerState() with { PY = groundPy, VX = 10f, VZ = 5f };
        TestHelpers.RegisterPlayer(sim, def, state);

        // Same Low Kick with PreserveMomentumOnStart=true: the run velocity coasts through.
        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1), 1);
        Assert.Equal(ActionState.Attacking, t1.State);
        TestHelpers.AssertNear(10f, t1.VX, 0.01f);
        TestHelpers.AssertNear(5f, t1.VZ, 0.01f);
    }


    [Fact]
    public void OptedInSlideNormal_CapsIncomingVectorAndDecaysOnceBeforeIntegration()
    {
        var def = WithSlideCarry(TestHelpers.CombatDef);
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState()
            with
            {
                PY = TestHelpers.GroundPY(def),
                State = ActionState.Sliding,
                VX = def.Movement.RunSpeed * 1.2f,
                VZ = def.Movement.RunSpeed * .3f,
            };
        sim.RegisterEntity(1, def, state);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new InputState { ActiveSlot = AbilitySlots.Slot1, Down = true },
        });

        var after = sim.GetState(1);
        float capped = MathF.Sqrt(after.VX * after.VX + after.VZ * after.VZ);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.True(after.SlideAttackCarryActive);
        TestHelpers.AssertNear(def.Movement.RunSpeed
            - DownActionTuning.AttackDecelerationRatio * def.Movement.RunSpeed / 60f,
            capped, .01f);
    }

    [Fact]
    public void SlideCarry_HitstopFreezesDecay()
    {
        var def = TestHelpers.CombatDef;
        var state = TestHelpers.PlayerState()
            with
            {
                PY = TestHelpers.GroundPY(def),
                State = ActionState.Attacking,
                AttackSlot = AbilitySlots.Slot1,
                SlideAttackCarryActive = true,
                HitstopTicks = 3,
                VX = def.Movement.RunSpeed,
            };
        float before = state.VX;

        Simulation.SimulateTick(ref state, def, default, TestHelpers.TestArena(),
            out _, out _, DownActionTuning.Default, verticalMotionOwned: false);

        Assert.Equal(before, state.VX);
        Assert.True(state.SlideAttackCarryActive);
    }

    [Fact]
    public void SlideCarry_NaturalAbilityCompletionClearsCarry()
    {
        var def = WithSlideCarry(TestHelpers.CombatDef);
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState()
            with
            {
                PY = TestHelpers.GroundPY(def),
                State = ActionState.Sliding,
                VX = def.Movement.RunSpeed,
            });

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new InputState { ActiveSlot = AbilitySlots.Slot1, Down = true },
        });
        Assert.True(sim.GetState(1).SlideAttackCarryActive);

        for (int i = 0; i < 240 && sim.GetState(1).SlideAttackCarryActive; i++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });

        Assert.False(sim.GetState(1).SlideAttackCarryActive);
    }

[Fact]
public void SlideCarry_ShieldAdmissionClearsCarryAndBrakes()
{
    var def = TestHelpers.CombatDef;
    var sim = TestHelpers.MakeSim();
    sim.RegisterEntity(1, def, TestHelpers.PlayerState()
        with
        {
            PY = TestHelpers.GroundPY(def),
            State = ActionState.Sliding,
            SlideAttackCarryActive = true,
            VX = def.Movement.RunSpeed,
        });

    sim.Tick(new Dictionary<ulong, InputState>
    {
        [1] = new InputState { ShieldHeld = true, ShieldPressed = true },
    });

    var state = sim.GetState(1);
    Assert.Equal(ActionState.Shielding, state.State);
    Assert.False(state.SlideAttackCarryActive);
    Assert.Equal(0f, state.VX);
    Assert.Equal(0f, state.VZ);
}

    private static CharacterDefinition WithSlideCarry(CharacterDefinition source)
    {
        var slots = new List<CookedSlotDefinition>(source.CookedSlots!);
        var slot = slots[0];
        slots[0] = new CookedSlotDefinition(
            slot.Ordinal, slot.Id, slot.IsAir, slot.Name, slot.Description, slot.IconId,
            slot.Behavior, slot.AimMode, slot.CooldownTicks, slot.IsRecoveryMove,
            slot.PreserveMomentumOnStart, slot.Timeline, slot.ChargePool, slot.AimMovement,
            slot.AimAnimationId, allowSlideCarry: true);
        source.CookedSlots = slots;
        return source;
    }

    /// <summary>Shallow-clone a spec with the momentum override flag flipped.</summary>
    private static AbilitySpec CloneSpec(AbilitySpec src, bool preserveMomentum)
    {
        return new AbilitySpec
        {
            Behavior = src.Behavior,
            Name = src.Name,
            CooldownTicks = src.CooldownTicks,
            Stages = src.Stages,
            AnimationNames = src.AnimationNames,
            Params = new Dictionary<string, float>(src.Params),
            PreserveMomentumOnStart = preserveMomentum,
        };
    }
}
