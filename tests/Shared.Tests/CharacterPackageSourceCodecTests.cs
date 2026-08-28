using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CharacterPackageSourceCodecTests
{
    private static string Fixture(string name) => File.ReadAllText(FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy/" + name));
    private static string FindRepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void FightGuy_RoundTripsDeterministically()
    {
        var first = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(first.IsValid, string.Join("\n", first.Diagnostics));
        var second = CharacterPackageSourceCodec.Load(CharacterPackageSourceCodec.SerializeManifest(first.Source!.Manifest), CharacterPackageSourceCodec.SerializeCharacter(first.Source.Character));
        Assert.True(second.IsValid);
        Assert.Equal(CharacterPackageSourceCodec.SerializeManifest(first.Source.Manifest), CharacterPackageSourceCodec.SerializeManifest(second.Source!.Manifest));
        Assert.Equal(CharacterPackageSourceCodec.SerializeCharacter(first.Source.Character), CharacterPackageSourceCodec.SerializeCharacter(second.Source.Character));
    }

    [Fact]
    public void FloatSerializationIsInvariantShortestRoundTrip()
    {
        var minimal = CharacterPackageSourceCodec.CreateMinimal("test-character", "Test", "Binoui", "MIT", "SlopArena");
        var character = minimal.Character with
        {
            Weight = 0.35f,
            Movement = minimal.Character.Movement with
            {
                RunSpeed = 1.7f,
                AirSpeedMax = 0.8f,
                AirAccelStick = 0.85f,
                Gravity = 1.2345678f,
            },
            Presentation = minimal.Character.Presentation with { LandStartOffsetSeconds = 0.49f },
        };

        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var json = CharacterPackageSourceCodec.SerializeCharacter(character);
            Assert.Contains("\"weight\": 0.35", json);
            Assert.Contains("\"runSpeed\": 1.7", json);
            Assert.Contains("\"airSpeedMax\": 0.8", json);
            Assert.Contains("\"gravity\": 1.2345678", json);
            Assert.Contains("\"landStartOffsetSeconds\": 0.49", json);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
        }
    }

    [Fact]
    public void UnchangedAuthoredSourceIsByteStable()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
        Assert.Equal(Fixture("character.json"), CharacterPackageSourceCodec.SerializeCharacter(parsed.Source!.Character));
    }

    [Fact]
    public void MinimalTemplateHasExactlyUniversalSlotsAndNoCapabilities()
    {
        var source = CharacterPackageSourceCodec.CreateMinimal("test-character", "Test Character", "Binoui", "MIT", "SlopArena");
        Assert.Equal(new[] { "ground.1", "ground.2", "ground.3", "ground.4", "ground.A", "ground.E", "ground.R", "ground.F", "air.1", "air.2", "air.3", "air.4", "air.A", "air.E", "air.R", "air.F" }, source.Character.Slots.Select(x => x.Id));
        Assert.Empty(source.Character.CapabilityRequirements);
        Assert.All(source.Character.Slots, x => Assert.Empty(x.Timeline.Stages));
        Assert.DoesNotContain("FightGuy", CharacterPackageSourceCodec.SerializeCharacter(source.Character));
    }

    [Fact]
    public void CodecRefusesUnknownLegacyAndUnsupportedFields()
    {
        string package = Fixture("package.json").Replace("\"dependencies\": []", "\"dependencies\": [], \"id\": \"legacy\"");
        var result = CharacterPackageSourceCodec.Load(package, Fixture("character.json"));
        Assert.Contains(result.Diagnostics, x => x.Code == "source.identity-forbidden");
        string unsupported = Fixture("character.json").Replace("\"authoringSchemaVersion\": 1", "\"authoringSchemaVersion\": 2");
        result = CharacterPackageSourceCodec.Load(Fixture("package.json"), unsupported);
        Assert.Contains(result.Diagnostics, x => x.Code == "schema.unsupported");
    }

    [Fact]
    public void TypedOperationsPreserveAuthoredOrder()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid);
        var original = parsed.Source!.Character.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).ToArray();
        var roundTrip = CharacterPackageSourceCodec.Load(Fixture("package.json"), CharacterPackageSourceCodec.SerializeCharacter(parsed.Source.Character));
        var restored = roundTrip.Source!.Character.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).ToArray();
        Assert.Equal(original.Select(x => x.GetType()), restored.Select(x => x.GetType()));
        Assert.Equal(original.Select(x => x.Tick), restored.Select(x => x.Tick));
    }

    [Fact]
    public void RenameUpdatesSourceReferencesAndRejectsCollision()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid);
        var source = parsed.Source!;
        var snapshots = new[] { new CharacterAssetCatalogBindingSnapshot("anim.run", "anim.run") };
        var renamed = CharacterPackageSourceCodec.RenameSemanticId(source, "anim.run", "anim.sprint", snapshots);
        Assert.Equal("anim.sprint", renamed.Source!.Character.Presentation.Run);
        Assert.DoesNotContain("anim.run", renamed.Source.Character.PresentationIds);
        var collision = CharacterPackageSourceCodec.RenameSemanticId(source, "anim.run", "anim.idle", snapshots);
        Assert.Contains(collision.Diagnostics, x => x.Code == "rename.collision");
    }

    [Fact]
    public void EditHelpersRejectOutOfRangeWithoutMutation()
    {
        var source = CharacterPackageSourceCodec.CreateMinimal("test-character", "Test", "Binoui", "MIT", "SlopArena");
        var result = CharacterPackageSourceCodec.RemoveStage(source, 16, 0);
        Assert.False(result.IsValid);
        Assert.Equal(16, source.Character.Slots.Count);
    }
}
