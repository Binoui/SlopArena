using System;
using System.Collections.Generic;
using System.IO;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CharacterContentSerializerTests
{
    private static string CharacterPath => FindRepoFile("content/characters/fightguy/character.json");

    [Fact]
    public void LoadFile_MatchesKnownGoodFightGuyBaseData()
    {
        var expected = CharacterRegistry.Get(CharacterClass.FightGuy);
        var actual = CharacterContentSerializer.LoadFile(CharacterPath);

        Assert.Equal(expected.Class, actual.Class);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Weight, actual.Weight);
        Assert.Equal(expected.CapsuleRadius, actual.CapsuleRadius);
        Assert.Equal(expected.CapsuleHeight, actual.CapsuleHeight);
        Assert.Equal(expected.HipHeight, actual.HipHeight);
        Assert.Equal(expected.HurtboxRadius, actual.HurtboxRadius);
        Assert.Equal(expected.BakedDataPath, actual.BakedDataPath);
        Assert.Equal(expected.ModelResourcePath, actual.ModelResourcePath);
        Assert.Equal(expected.HurtboxBoneScale, actual.HurtboxBoneScale);
        Assert.Equal(expected.ModelYOffset, actual.ModelYOffset);
        Assert.Equal(expected.ModelSoleOffset, actual.ModelSoleOffset);
        Assert.Equal(expected.AutoModelYOffset, actual.AutoModelYOffset);
        Assert.Equal(expected.VisualScale, actual.VisualScale);
        Assert.Equal(expected.IdleAnim, actual.IdleAnim);
        Assert.Equal(expected.RunAnim, actual.RunAnim);
        Assert.Equal(expected.DashAnim, actual.DashAnim);
        Assert.Equal(expected.JumpAnim, actual.JumpAnim);
        Assert.Equal(expected.FallAnim, actual.FallAnim);
        Assert.Equal(expected.HitSmallAnim, actual.HitSmallAnim);
        Assert.Equal(expected.HitMediumAnim, actual.HitMediumAnim);
        Assert.Equal(expected.HitHardAnim, actual.HitHardAnim);
        Assert.Equal(expected.LandStartOffset, actual.LandStartOffset);
        Assert.Equal(expected.Movement.RunSpeed, actual.Movement.RunSpeed);
        Assert.Equal(expected.Movement.Gravity, actual.Movement.Gravity);
        Assert.Equal(expected.Movement.DashDurationTicks, actual.Movement.DashDurationTicks);
        Assert.Equal(expected.Movement.MaxJumps, actual.Movement.MaxJumps);

        Assert.Equal(expected.HurtboxCapsules, actual.HurtboxCapsules);
        Assert.Equal(expected.HurtboxBoneDefs, actual.HurtboxBoneDefs);
        Assert.Equal(expected.ClipOverrides, actual.ClipOverrides);
    }

    [Fact]
    public void LoadFile_MatchesRepresentativeNormalAbilityData()
    {
        var expected = CharacterRegistry.Get(CharacterClass.FightGuy);
        var actual = CharacterContentSerializer.LoadFile(CharacterPath);

        Assert.NotNull(expected.Slot1);
        Assert.NotNull(actual.Slot1);
        Assert.Equal(expected.Slot1!.Name, actual.Slot1!.Name);
        Assert.Equal(expected.Slot1.CooldownTicks, actual.Slot1.CooldownTicks);
        Assert.Equal(expected.Slot1.AnimationNames, actual.Slot1.AnimationNames);

        var expectedStage = expected.Slot1.Stages[0];
        var actualStage = actual.Slot1.Stages[0];
        Assert.Equal(expectedStage.DurationTicks, actualStage.DurationTicks);
        Assert.Equal(expectedStage.IasaTicks, actualStage.IasaTicks);
        Assert.Equal(expectedStage.UseTargetLock, actualStage.UseTargetLock);
        Assert.Equal(expectedStage.RotateTowardTarget, actualStage.RotateTowardTarget);
        Assert.Equal(expectedStage.TrackingStrength, actualStage.TrackingStrength);

        var expectedEvent = expectedStage.HitboxEvents[0];
        var actualEvent = actualStage.HitboxEvents[0];
        Assert.Equal(expectedEvent.TriggerTick, actualEvent.TriggerTick);
        Assert.Equal(expectedEvent.Shape, actualEvent.Shape);
        Assert.Equal(expectedEvent.Radius, actualEvent.Radius);
        Assert.Equal(expectedEvent.OffX, actualEvent.OffX);
        Assert.Equal(expectedEvent.OffY, actualEvent.OffY);
        Assert.Equal(expectedEvent.OffZ, actualEvent.OffZ);
        Assert.Equal(expectedEvent.BoneName, actualEvent.BoneName);
        Assert.Equal(expectedEvent.Damage, actualEvent.Damage);
        Assert.Equal(expectedEvent.StunTicks, actualEvent.StunTicks);
        Assert.Equal(expectedEvent.Interruptible, actualEvent.Interruptible);
        Assert.Equal(expectedEvent.Knockback.Profile, actualEvent.Knockback.Profile);
        Assert.Equal(expectedEvent.Knockback.Angle, actualEvent.Knockback.Angle);
        Assert.Equal(expectedEvent.Knockback.BaseKnockback, actualEvent.Knockback.BaseKnockback);
        Assert.Equal(expectedEvent.Knockback.KnockbackGrowth, actualEvent.Knockback.KnockbackGrowth);

        Assert.NotNull(expected.AirSlot2);
        Assert.NotNull(actual.AirSlot2);
        var expectedAirStage = expected.AirSlot2!.Stages[0];
        var actualAirStage = actual.AirSlot2!.Stages[0];
        Assert.Equal(expectedAirStage.LandingLagTicks, actualAirStage.LandingLagTicks);
        Assert.Equal(expectedAirStage.AutoCancelBeforeTicks, actualAirStage.AutoCancelBeforeTicks);
        Assert.Equal(expectedAirStage.AutoCancelAfterTicks, actualAirStage.AutoCancelAfterTicks);
        var expectedAirEvent = expectedAirStage.HitboxEvents[0];
        var actualAirEvent = actualAirStage.HitboxEvents[0];
        Assert.Equal(HitboxShape.Capsule, actualAirEvent.Shape);
        Assert.Equal(expectedAirEvent.EndBoneName, actualAirEvent.EndBoneName);
        Assert.Equal(expectedAirEvent.HitGroup, actualAirEvent.HitGroup);
    }

    [Fact]
    public void LoadFile_PreservesSpecialDataAndAirAliases()
    {
        var expected = CharacterRegistry.Get(CharacterClass.FightGuy);
        var actual = CharacterContentSerializer.LoadFile(CharacterPath);

        AssertSpecial(expected.E, actual.E);
        AssertSpecial(expected.R, actual.R);
        AssertSpecial(expected.F, actual.F);
        AssertSpecial(expected.A, actual.A);
        Assert.Same(actual.E, actual.AirE);
        Assert.Same(actual.R, actual.AirR);
        Assert.Same(actual.F, actual.AirF);
        Assert.Same(actual.A, actual.AirA);
        Assert.NotSame(actual.Slot1, actual.AirSlot1);
        Assert.NotSame(actual.Slot2, actual.AirSlot2);
    }

    [Fact]
    public void Serialize_LoadedFightGuy_IsByteDeterministic()
    {
        string expectedText = File.ReadAllText(CharacterPath);
        var loaded = CharacterContentSerializer.LoadFile(CharacterPath);

        Assert.Equal(expectedText, CharacterContentSerializer.Serialize("fightguy", loaded));
        Assert.Equal(
            CharacterContentSerializer.Serialize("fightguy", loaded),
            CharacterContentSerializer.Serialize("fightguy", loaded));
    }

    [Fact]
    public void LoadedDefinition_RegistersAndStepsInSimulation()
    {
        var definition = CharacterContentSerializer.LoadFile(CharacterPath);
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(definition) };

        sim.RegisterEntity(1, definition, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });

        Assert.Equal(1UL, sim.GetState(1).EntityId);
    }

    [Fact]
    public void Load_RejectsMissingSchemaVersion()
        => Assert.Contains("Missing character schemaVersion", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{}}" )).Message);

    [Fact]
    public void Load_RejectsMissingId()
        => Assert.Contains("Missing character id", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"class\":\"FightGuy\",\"abilities\":{}}" )).Message);

    [Fact]
    public void Load_RejectsUnsupportedSchemaVersion()
        => Assert.Contains("Unsupported character schemaVersion 2", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":2,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{}}" )).Message);

    [Fact]
    public void Load_RejectsUnknownEnum()
        => Assert.Contains("Invalid character content", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"slot1\":{\"behavior\":\"NotABehavior\",\"stages\":[]}}}" )).Message);

    [Fact]
    public void Load_RejectsNumericUnknownEnum()
        => Assert.Contains("Invalid character content", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"slot1\":{\"behavior\":99,\"stages\":[]}}}" )).Message);

    [Fact]
    public void Load_RejectsUnknownAbilityKey()
        => Assert.Contains("unknown ability key", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"unknown\":{\"stages\":[]}}}" )).Message);

    [Fact]
    public void Load_RejectsInvalidAliasTarget()
        => Assert.Contains("invalid alias target", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"e\":{\"stages\":[]}},\"airAliases\":{\"airE\":\"missing\"}}" )).Message);

    [Fact]
    public void Load_RejectsAbilityMissingStages()
        => Assert.Contains("missing stages", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"slot1\":{\"name\":\"No stages\"}}}" )).Message);

    private static CharacterDefinition LoadText(string json) => CharacterContentSerializer.Load(json);

    private static void AssertSpecial(AbilitySpec? expected, AbilitySpec? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected!.SpecialEffectKeys, actual!.SpecialEffectKeys);
        Assert.Equal(expected.Behavior, actual.Behavior);
        Assert.Equal(expected.AimMode, actual.AimMode);
        Assert.Equal(expected.AnimSpeed, actual.AnimSpeed);
        Assert.Equal(expected.AnimationNames, actual.AnimationNames);
        Assert.Equal(expected.Params.Count, actual.Params.Count);
        foreach (var pair in expected.Params)
            Assert.Equal(pair.Value, actual.Params[pair.Key]);
    }

    private static string FindRepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repo file: {relative}");
    }
}
