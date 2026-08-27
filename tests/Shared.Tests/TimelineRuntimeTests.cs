using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class TimelineRuntimeTests
{
    [Fact]
    public void SameTickOperationsPreserveOrderAndCompleteImmediately()
    {
        var hitbox = Hitbox(2, duration: 3);
        var slot = Slot(10,
            new CookedSetVelocityOperation(0, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Absolute, 1f, 2f, 3f),
            new CookedSetVelocityOperation(0, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Additive, .5f, 0f, 0f),
            new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters, hitbox),
            new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters, hitbox),
            new CookedEmitPresentationOperation(0, AuthoringUnit.Ticks, "presentation.hit", 4),
            new CookedCompleteTimelineOperation(0, AuthoringUnit.Ticks),
            new CookedSetVelocityOperation(0, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Absolute, 99f, 99f, 99f));
        var (sim, def) = Create(slot);

        sim.ActivateAbility(1, new CookedTimelineAbility(slot, Array.Empty<string>()), 2, def);
        var state = sim.GetState(1);

        Assert.Equal(ActionState.Idle, state.State);
        Assert.Equal((byte)0, state.AttackSlot);
        TestHelpers.AssertNear(1.5f, state.VX);
        TestHelpers.AssertNear(2f, state.VY);
        TestHelpers.AssertNear(3f, state.VZ);
        Assert.Equal(2, sim.Resolver.GetActiveHitboxes().Count);
        var presentation = Assert.Single(sim.GetPresentationEvents());
        Assert.Equal(0u, presentation.MatchTick);
        Assert.Equal(1ul, presentation.EntityId);
        Assert.Equal(4, presentation.OperationIndex);
        Assert.Equal(new PresentationEventKey(0, 1, 4), presentation.Key);
        Assert.Equal("presentation.hit", presentation.PresentationId);

        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal(2, sim.Resolver.GetActiveHitboxes().Count);
    }

    [Fact]
    public void ProjectileUsesAimDirectionAndMaxFlightLifetime()
    {
        var slot = Slot(5, new CookedSpawnProjectileOperation(0, AuthoringUnit.Meters,
            new CookedProjectile(0f, 1f, 0f, 12f, 2f, .5f, 4f, 30, 3f, 4f, 6, 7)));
        var (sim, def) = Create(slot, TestHelpers.PlayerState() with { AimYaw = MathF.PI / 2f, AimPitch = 0f });

        sim.ActivateAbility(1, new CookedTimelineAbility(slot, Array.Empty<string>()), 2, def);
        var projectile = Assert.Single(sim.Resolver.GetActiveHitboxes());

        TestHelpers.AssertNear(12f, projectile.VX);
        TestHelpers.AssertNear(0f, projectile.VY);
        TestHelpers.AssertNear(0f, projectile.VZ);
        Assert.Equal((ushort)7, projectile.DurationTicks);
        TestHelpers.AssertNear(1f, projectile.Y - TestHelpers.PlayerState().PY);
    }

    [Fact]
    public void AimStateTransitionsAndMultiStageAdvanceUseCookedTiming()
    {
        var slot = new CookedSlotDefinition(0, "ground.1", false, "Test", "Test", "icon.test",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.GroundCursor, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(2, 0, 0, 0, 0, new[] { "anim.one" }, new CookedTimelineOperation[]
                {
                    new CookedSetAimStateOperation(0, AuthoringUnit.Ticks, AuthoringAimMode.GroundCursor),
                }),
                new CookedStage(2, 0, 0, 0, 0, new[] { "anim.two" }, new CookedTimelineOperation[]
                {
                    new CookedSetAimStateOperation(0, AuthoringUnit.Ticks, AuthoringAimMode.None),
                }),
            }));
        var (sim, def) = Create(slot);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot, Array.Empty<string>()), 2, def);
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal((byte)0, sim.GetState(1).ComboStage);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal((byte)1, sim.GetState(1).ComboStage);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void InternalCapabilityRegistryAdmitsOnlyExactTypedEntries()
    {
        var cases = new (string Id, CookedCapabilityParameters Parameters, Type Type)[]
        {
            ("slop.internal.fightguy.ki-shot.v1", new CookedKiShotCapabilityParameters(1, 2, 1, 2, 1, 1, 1, 1, 1, 30, 1, 3), typeof(FightGuyKiShot)),
            ("slop.internal.fightguy.rising-dragon.v1", new CookedRisingDragonCapabilityParameters(11, 12, 8), typeof(FightGuyRisingKick)),
            ("slop.internal.fightguy.cyclone-kick.v1", new CookedCycloneKickCapabilityParameters(17, 6, 34, 40, 1, 1, 1, 7, 15, 8, 5, 6, 1, 1), typeof(FightGuyCycloneKick)),
            ("slop.internal.fightguy.dragon-beam.v1", new CookedDragonBeamCapabilityParameters(28, 24, 1, 18, 1, 14, 20, 18, 10, 24, 2), typeof(FightGuyDragonBeam)),
        };
        foreach (var item in cases)
        {
            Assert.True(InternalCapabilityRegistry.TryCreate(item.Id, "1", item.Parameters, out var capability));
            Assert.IsType(item.Type, capability);
        }

        Assert.False(InternalCapabilityRegistry.TryCreate(cases[0].Id, "2", cases[0].Parameters, out _));
        Assert.False(InternalCapabilityRegistry.TryCreate("slop.internal.fightguy.unknown.v1", "1", cases[0].Parameters, out _));
        Assert.False(InternalCapabilityRegistry.TryCreate(cases[0].Id, "1", cases[1].Parameters, out _));
    }

    [Fact]
    public void CapabilityCancellationClearsAimWithoutClearingVelocity()
    {
        var slot = Slot(10,
            new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                "slop.internal.fightguy.ki-shot.v1", "1",
                new CookedKiShotCapabilityParameters(8, 24, 1.2f, 25f, 1f, .5f, 6f, 3f, 4.5f, 30, 12, 90)),
            new CookedSetAimStateOperation(0, AuthoringUnit.Ticks, AuthoringAimMode.CameraForward3D));
        var initial = TestHelpers.PlayerState() with { PY = 100f, IsGrounded = false, VX = 4f, VZ = 5f };
        var (sim, def) = Create(slot, initial);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot, Array.Empty<string>()), 2, def);
        var state = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, state.State);
        state.State = ActionState.Hitstun;
        sim.SetState(1, state);
        sim.TickAbilities(new Dictionary<ulong, InputState> { [1] = default });

        Assert.Null(sim.GetActiveAbility(1));
        state = sim.GetState(1);
        Assert.False(state.IsAiming);
        TestHelpers.AssertNear(4f, state.VX);
        TestHelpers.AssertNear(5f, state.VZ);
    }
    [Fact]
    public void InterruptionAndRemovalCancelWithoutNaturalEnd()
    {
        var sim = TestHelpers.MakeSim();
        var def = TestHelpers.FightGuyDef;
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        var probe = new ProbeAbility();
        sim.ActivateAbility(1, probe, 0, def);
        var state = sim.GetState(1);
        state.State = ActionState.Hitstun;
        sim.SetState(1, state);
        sim.TickAbilities(new Dictionary<ulong, InputState> { [1] = default });

        Assert.Equal(1, probe.CancelCount);
        Assert.Equal(0, probe.EndCount);
        Assert.Null(sim.GetActiveAbility(1));

        var removalSim = TestHelpers.MakeSim();
        removalSim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        var removalProbe = new ProbeAbility();
        removalSim.ActivateAbility(1, removalProbe, 0, def);
        removalSim.RemoveEntity(1);
        Assert.Equal(1, removalProbe.CancelCount);
        Assert.Equal(0, removalProbe.EndCount);
    }

    private sealed class ProbeAbility : ServerAbility
    {
        public int CancelCount;
        public int EndCount;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
            => s.State = ActionState.Attacking;

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def) { }
        public override void OnEnd(ref CharacterState s) => EndCount++;
        public override void OnCancel(ref CharacterState s) => CancelCount++;
    }


    [Fact]
    public void FightGuyCookedResolutionCoversEightWireNormalAndSpecialSlots()
    {
        var def = TestHelpers.FightGuyDef;
        var expectedGround = new[] { "ground.1", "ground.E", "ground.R", "ground.F", "ground.2", "ground.3", "ground.4", "ground.A" };
        var wireSlots = new byte[] { 3, 4, 5, 6, 7, 8, 9, 11 };
        for (var i = 0; i < wireSlots.Length; i++)
        {
            Assert.Equal(expectedGround[i], def.GetCookedSlotAbility(wireSlots[i], false)!.Id);
            Assert.Equal("air." + expectedGround[i].Substring(expectedGround[i].IndexOf('.') + 1), def.GetCookedSlotAbility(wireSlots[i], true)!.Id);
        }
        Assert.Null(AbilityFactory.CreateServer(CharacterClass.FightGuy, 2, false));
        Assert.Null(def.GetCookedSlotAbility(1, false));
        Assert.Null(def.GetCookedSlotAbility(10, false));
    }


    [Fact]
    public void FightGuyCycloneKickEmitsSemanticPresentationEvent()
    {
        var def = TestHelpers.FightGuyDef;
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def),
        });

        sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { ActiveSlot = 5 } });

        var evt = Assert.Single(sim.GetPresentationEvents());
        Assert.Equal(new PresentationEventKey(1, 1, 10), evt.Key);
        Assert.Equal("presentation.cyclone-kick.start", evt.PresentationId);
    }

    private static CookedSlotDefinition Slot(ushort duration, params CookedTimelineOperation[] operations)
        => new(0, "ground.1", false, "Test", "Test", "icon.test", AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(duration, 0, 0, 0, 0, Array.Empty<string>(), operations) }));

    private static CookedHitbox Hitbox(ushort tick, ushort duration)
        => new(AuthoringHitboxShape.Sphere, .5f, 1f, 0f, 0f, 0f, 0f, 0f, null, null, 3f, 20f, 2f, 4f, 5, duration, true, 0);

    private static (ServerSimulation Sim, CharacterDefinition Def) Create(CookedSlotDefinition slot, CharacterState? state = null)
    {
        var def = new CharacterDefinition
        {
            Class = CharacterClass.Manki,
            DisplayName = "Cooked Test",
            CapsuleHeight = 1.7f,
            CapsuleRadius = .35f,
            Movement = TestHelpers.FightGuyDef.Movement,
            CookedSlots = new[] { slot },
            HurtboxCapsules = Array.Empty<HurtboxCapsule>(),
        };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, state ?? TestHelpers.PlayerState());
        return (sim, def);
    }
}
