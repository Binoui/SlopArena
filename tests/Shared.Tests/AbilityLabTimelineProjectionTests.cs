using System;
using System.Collections.Generic;
using System.IO;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class AbilityLabTimelineProjectionTests
{
    [Fact]
    public void BuildUsesCumulativeStageOffsetsAndHalfOpenRanges()
    {
        var hitbox = new HitboxSource(AuthoringHitboxShape.Sphere, 0.5f, 0f, 0f, 1f, 0f, 0f, 1f,
            null, null, 10f, 45f, 20f, 1f, 4, 3, true, 0);
        var slot = Slot(new CharacterStageSource(
                5, 1, 2, 0, 0, new[] { "first" }, new CharacterTimelineOperationSource[]
                {
                    new SpawnHitboxOperationSource(2, AuthoringUnit.Meters, hitbox),
                    new SetVelocityOperationSource(4, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Additive, 1f, 0f, 0f),
                }),
            new CharacterStageSource(
                7, 0, 0, 0, 0, new[] { "second" }, new CharacterTimelineOperationSource[]
                {
                    new CompleteTimelineOperationSource(1, AuthoringUnit.Ticks),
                }));

        var projection = AbilityLabTimelineProjection.Build(slot);

        Assert.Equal(12, projection.DurationTicks);
        Assert.Equal(2, projection.Stages.Count);
        Assert.Equal((0, 5), (projection.Stages[0].StartTick, projection.Stages[0].EndTick));
        Assert.Equal((5, 12), (projection.Stages[1].StartTick, projection.Stages[1].EndTick));
        Assert.Equal((2, 5), (projection.Stages[0].Operations[0].StartTick, projection.Stages[0].Operations[0].EndTick));
        Assert.Equal((4, 5), (projection.Stages[0].Operations[1].StartTick, projection.Stages[0].Operations[1].EndTick));
        Assert.Equal((6, 7), (projection.Stages[1].Operations[0].StartTick, projection.Stages[1].Operations[0].EndTick));
    }

    [Fact]
    public void BuildPreservesOperationOrderKindsAndFriendlySummaries()
    {
        var operations = new CharacterTimelineOperationSource[]
        {
            new SetVelocityOperationSource(0, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Absolute, 0f, 0f, 0f),
            new SpawnProjectileOperationSource(1, AuthoringUnit.Meters, new ProjectileSource(0f, 0f, 0f, 1f, 0f, 1f, 1f, 45f, 2f, 1f, 1, 10)),
            new SetAimStateOperationSource(2, AuthoringUnit.Normalized, AuthoringAimMode.GroundVector),
            new StartCapabilityOperationSource(3, AuthoringUnit.Ticks, "slop.test.v1", "1", new RisingDragonCapabilityParameters(1f, 2, 0)),
            new EmitPresentationOperationSource(4, AuthoringUnit.Ticks, "test"),
            new CompleteTimelineOperationSource(5, AuthoringUnit.Ticks),
        };
        var projection = AbilityLabTimelineProjection.Build(Slot(new CharacterStageSource(
            6, 0, 0, 0, 0, Array.Empty<string>(), operations)));

        Assert.Collection(projection.Stages[0].Operations,
            operation => Assert.Equal((CookedOperationKind.SetVelocity, "Set velocity"), (operation.Kind, operation.Summary)),
            operation => Assert.Equal((CookedOperationKind.SpawnProjectile, "Projectile"), (operation.Kind, operation.Summary)),
            operation => Assert.Equal((CookedOperationKind.SetAimState, "Set aim"), (operation.Kind, operation.Summary)),
            operation => Assert.Equal((CookedOperationKind.StartCapability, "Start ability"), (operation.Kind, operation.Summary)),
            operation => Assert.Equal((CookedOperationKind.EmitPresentation, "Presentation"), (operation.Kind, operation.Summary)),
            operation => Assert.Equal((CookedOperationKind.CompleteTimeline, "Complete move"), (operation.Kind, operation.Summary)));
    }

    [Fact]
    public void EmptyAndNullTimelinesAreHandledExplicitly()
    {
        var projection = AbilityLabTimelineProjection.Build(Slot(new CharacterStageSource(
            0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<CharacterTimelineOperationSource>())));
        Assert.Equal(0, projection.DurationTicks);
        Assert.Empty(projection.Stages[0].Operations);
        Assert.Throws<ArgumentNullException>(() => AbilityLabTimelineProjection.Build(null!));
    }

    [Fact]
    public void UnknownOperationTypeFailsClosed()
    {
        Assert.Throws<InvalidDataException>(() => AbilityLabTimelineProjection.Build(Slot(new CharacterStageSource(
            1, 0, 0, 0, 0, Array.Empty<string>(), new CharacterTimelineOperationSource[]
            {
                new UnknownOperation(0, AuthoringUnit.Ticks),
            }))));
    }

    [Fact]
    public void SnapTickClampsNormalizedInputAndPreservesEndpoint()
    {
        Assert.Equal(0, AbilityLabTimelineProjection.SnapTick(0d, 10));
        Assert.Equal(5, AbilityLabTimelineProjection.SnapTick(0.5d, 10));
        Assert.Equal(10, AbilityLabTimelineProjection.SnapTick(1d, 10));
        Assert.Equal(0, AbilityLabTimelineProjection.SnapTick(-1d, 10));
        Assert.Equal(10, AbilityLabTimelineProjection.SnapTick(2d, 10));
        Assert.Equal(0, AbilityLabTimelineProjection.SnapTick(0.5d, 0));
    }

    [Fact]
    public void ClampHelpersKeepOperationsAndHitboxesInsidePlayableStage()
    {
        Assert.Equal(0, AbilityLabTimelineProjection.ClampOperationTick(-1, 5));
        Assert.Equal(4, AbilityLabTimelineProjection.ClampOperationTick(99, 5));
        Assert.Equal(0, AbilityLabTimelineProjection.ClampOperationTick(2, 0));
        Assert.Equal(1, AbilityLabTimelineProjection.ClampHitboxDuration(0, 0, 5));
        Assert.Equal(1, AbilityLabTimelineProjection.ClampHitboxDuration(4, 99, 5));
        Assert.Equal(5, AbilityLabTimelineProjection.ClampHitboxDuration(0, 99, 5));
        Assert.Equal(0, AbilityLabTimelineProjection.ClampHitboxDuration(0, 1, 0));
    }

    [Fact]
    public void RetimingPreservesOperationSubtypePayloadAndSourceAddress()
    {
        var velocity = new SetVelocityOperationSource(1, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Additive, 1f, 2f, 3f);
        var source = WithSlot(new CharacterSlotSource("ground.1", "Test", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false, new CharacterTimelineSource(new[]
            {
                new CharacterStageSource(5, 0, 0, 0, 0, Array.Empty<string>(), new CharacterTimelineOperationSource[] { new CompleteTimelineOperationSource(2, AuthoringUnit.Ticks) }),
                new CharacterStageSource(7, 0, 0, 0, 0, Array.Empty<string>(), new CharacterTimelineOperationSource[] { velocity }),
            })));

        var result = CharacterPackageSourceCodec.ReplaceOperationTick(source, 0, 1, 0, 4);

        Assert.True(result.IsValid);
        var edited = Assert.IsType<SetVelocityOperationSource>(result.Source!.Character.Slots[0].Timeline.Stages[1].Operations[0]);
        Assert.Equal((ushort)4, edited.Tick);
        Assert.Equal((AuthoringVelocityMode.Additive, 1f, 2f, 3f), (edited.VelocityMode, edited.X, edited.Y, edited.Z));
        var projection = AbilityLabTimelineProjection.Build(result.Source.Character.Slots[0]);
        Assert.Equal((1, 0, 9), (projection.Stages[1].Operations[0].SourceStageIndex, projection.Stages[1].Operations[0].SourceOperationIndex, projection.Stages[1].Operations[0].StartTick));
    }

    [Fact]
    public void RetimingHitboxDurationPreservesAllOtherFields()
    {
        var hitbox = new HitboxSource(AuthoringHitboxShape.Capsule, 0.5f, 1f, 2f, 3f, 4f, 5f, 6f,
            "hand", "foot", 10f, 45f, 20f, 2f, 3, 2, true, 7);
        var source = WithSlot(new CharacterSlotSource("ground.1", "Test", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false, new CharacterTimelineSource(new[]
            {
                new CharacterStageSource(8, 0, 0, 0, 0, Array.Empty<string>(), new CharacterTimelineOperationSource[] { new SpawnHitboxOperationSource(2, AuthoringUnit.Meters, hitbox) }),
            })));

        var result = CharacterPackageSourceCodec.ReplaceHitboxDuration(source, 0, 0, 0, 6);

        Assert.True(result.IsValid);
        var edited = Assert.IsType<SpawnHitboxOperationSource>(result.Source!.Character.Slots[0].Timeline.Stages[0].Operations[0]);
        Assert.Equal((ushort)6, edited.Hitbox.DurationTicks);
        Assert.Equal(hitbox with { DurationTicks = 6 }, edited.Hitbox);
    }

    [Fact]
    public void RetimingRejectsInvalidAddressesAndStageBoundaries()
    {
        var source = WithSlot(new CharacterSlotSource("ground.1", "Test", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false, new CharacterTimelineSource(new[]
            {
                new CharacterStageSource(5, 0, 0, 0, 0, Array.Empty<string>(), new CharacterTimelineOperationSource[] { new CompleteTimelineOperationSource(2, AuthoringUnit.Ticks) }),
            })));

        Assert.Equal("edit.index.out-of-range", CharacterPackageSourceCodec.ReplaceOperationTick(source, 0, 0, 1, 1).Diagnostics[0].Code);
        Assert.Equal("edit.tick.out-of-range", CharacterPackageSourceCodec.ReplaceOperationTick(source, 0, 0, 0, 5).Diagnostics[0].Code);
        Assert.Equal("edit.operation.not-hitbox", CharacterPackageSourceCodec.ReplaceHitboxDuration(source, 0, 0, 0, 1).Diagnostics[0].Code);
        Assert.Equal("edit.index.out-of-range", CharacterPackageSourceCodec.ReplaceOperationTick(source, 0, 1, 0, 1).Diagnostics[0].Code);
    }

    [Fact]
    public void RetimingHitboxRejectsNonPositiveAndOutOfStageDurations()
    {
        var hitbox = new HitboxSource(AuthoringHitboxShape.Sphere, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f,
            null, null, 1f, 45f, 1f, 1f, 1, 1, true, 0);
        var source = WithSlot(new CharacterSlotSource("ground.1", "Test", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false, new CharacterTimelineSource(new[]
            {
                new CharacterStageSource(5, 0, 0, 0, 0, Array.Empty<string>(), new CharacterTimelineOperationSource[] { new SpawnHitboxOperationSource(3, AuthoringUnit.Meters, hitbox) }),
            })));

        Assert.Equal("edit.duration.out-of-range", CharacterPackageSourceCodec.ReplaceHitboxDuration(source, 0, 0, 0, 0).Diagnostics[0].Code);
        Assert.Equal("edit.duration.out-of-range", CharacterPackageSourceCodec.ReplaceHitboxDuration(source, 0, 0, 0, 3).Diagnostics[0].Code);
    }

    private static CharacterPackageSource WithSlot(CharacterSlotSource slot)
    {
        var source = CharacterPackageSourceCodec.CreateMinimal("test", "Test", "test", "MIT", "");
        return CharacterPackageSourceCodec.ReplaceSlot(source, 0, slot).Source!;
    }

    private static CharacterSlotSource Slot(params CharacterStageSource[] stages)
        => new("ground.1", "Test", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false, new CharacterTimelineSource(stages));

    private sealed record UnknownOperation(ushort Tick, AuthoringUnit Unit) : CharacterTimelineOperationSource(Tick, Unit);
}
