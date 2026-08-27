using System.Collections.Generic;
using Xunit;
using SlopArena.Shared;

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
