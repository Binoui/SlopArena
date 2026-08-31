using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void AimAnimationId_RoundTripsOnAimedSlots()
    {
        var fg = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(fg.IsValid, string.Join("\n", fg.Diagnostics));
        Assert.Equal("anim.ki-shot-loop", fg.Source!.Character.Slots.Single(s => s.Id == "ground.A").AimAnimationId);

        string mk = FindRepoFile("client/Unity/Assets/CharacterPackages/manki/character.json");
        var manki = CharacterPackageSourceCodec.Load(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(mk)!, "package.json")),
            File.ReadAllText(mk));
        Assert.True(manki.IsValid, string.Join("\n", manki.Diagnostics));
        Assert.Equal("anim.manki.ga-loop", manki.Source!.Character.Slots.Single(s => s.Id == "ground.A").AimAnimationId);

        // Serialized round-trip preserves the field.
        var re = CharacterPackageSourceCodec.Load(
            CharacterPackageSourceCodec.SerializeManifest(fg.Source.Manifest),
            CharacterPackageSourceCodec.SerializeCharacter(fg.Source.Character));
        Assert.True(re.IsValid);
        Assert.Equal("anim.ki-shot-loop", re.Source!.Character.Slots.Single(s => s.Id == "ground.A").AimAnimationId);
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
    public void AimMovementPolicy_RoundTripsAndMissingDefaultsToFixed()
    {
        var missingJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)missingJson["slots"]![0]!).Remove("aimMovement");
        var missing = CharacterPackageSourceCodec.Load(Fixture("package.json"), missingJson.ToJsonString());
        Assert.True(missing.IsValid, string.Join("\n", missing.Diagnostics));
        Assert.Equal(AuthoringAimMovementMode.Fixed, missing.Source!.Character.Slots[0].AimMovement);

        var mobileJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)mobileJson["slots"]![0]!)["aimMovement"] = "mobile";
        string mobileSource = mobileJson.ToJsonString();
        var mobile = CharacterPackageSourceCodec.Load(Fixture("package.json"), mobileSource);
        Assert.True(mobile.IsValid, string.Join("\n", mobile.Diagnostics));
        Assert.Equal(AuthoringAimMovementMode.Mobile, mobile.Source!.Character.Slots[0].AimMovement);
        Assert.Contains("\"aimMovement\": \"mobile\"", CharacterPackageSourceCodec.SerializeCharacter(mobile.Source.Character));

        var unknownJson = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        ((JsonObject)unknownJson["slots"]![0]!)["aimMovement"] = "unknown";
        var unknown = CharacterPackageSourceCodec.Load(Fixture("package.json"), unknownJson.ToJsonString());
        Assert.Contains(unknown.Diagnostics, x => x.Code == "enum.unknown");
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
    public void AuthoredSourceRoundTripIsByteStable()
    {
        var parsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json"));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
        var serialized = CharacterPackageSourceCodec.SerializeCharacter(parsed.Source!.Character);
        var reparsed = CharacterPackageSourceCodec.Load(Fixture("package.json"), serialized);
        Assert.True(reparsed.IsValid, string.Join("\n", reparsed.Diagnostics));
        Assert.Equal(serialized, CharacterPackageSourceCodec.SerializeCharacter(reparsed.Source!.Character));
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
    public void AuthoringReadyTemplateHasUsableDefaultsAndIndependentMoveAnimations()
    {
        var source = CharacterPackageSourceCodec.CreateAuthoringReady("test-character", "Test Character", "Binoui", "MIT", "SlopArena");
        Assert.Equal(100f, source.Character.Weight);
        Assert.Equal(14f, source.Character.Movement.RunSpeed);
        Assert.Equal(1.7f, source.Character.CapsuleHeight);
        Assert.Equal(7, source.Character.HurtboxBoneDefs.Count);
        Assert.All(source.Character.Slots, slot =>
        {
            Assert.Single(slot.Timeline.Stages);
            Assert.Equal(30, slot.Timeline.Stages[0].DurationTicks);
            Assert.Single(slot.Timeline.Stages[0].AnimationIds);
            Assert.Empty(slot.Timeline.Stages[0].Operations);
        });
        Assert.Equal(source.Character.Slots.Count,
            source.Character.Slots.SelectMany(slot => slot.Timeline.Stages).SelectMany(stage => stage.AnimationIds).Distinct(StringComparer.Ordinal).Count());
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
