using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CharacterPackageCompilerTests
{
    private static string Fixture(string name)
    {
        string relative = name == "cooked.expected.hex"
            ? "tests/Shared.Tests/Fixtures/FightGuyAuthoring/cooked.expected.hex"
            : $"client/Unity/Assets/CharacterPackages/fightguy/{name}";
        return File.ReadAllText(FindRepoFile(relative));
    }
    private static CharacterCompileResult CompileCharacter(Action<JsonObject>? mutate = null, CharacterCookProfile profile = CharacterCookProfile.TrustedBuiltIn)
    {
        var character = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        mutate?.Invoke(character);
        return CharacterPackageCompiler.Compile(Fixture("package.json"), character.ToJsonString(), profile);
    }
    private static string[] Codes(CharacterCompileResult result) => result.Diagnostics.Select(x => x.Code).ToArray();
    private static void AssertError(CharacterCompileResult result, string code)
    {
        Assert.Contains(result.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error && x.Code == code);
        Assert.Null(result.CookedPackage);
    }

    [Fact]
    public void FightGuy_TrustedProfile_CooksSixteenExplicitSlots()
    {
        var result = CharacterPackageCompiler.Compile(Fixture("package.json"), Fixture("character.json"), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(result.CookedPackage);
        Assert.DoesNotContain(result.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(16, result.CookedPackage!.Definition.Slots.Count);
        Assert.Equal(16, result.CookedPackage.Budget.SlotCount);
        Assert.Equal("anim.cyclone-kick", result.CookedPackage.Definition.Slots.Single(x => x.Id == "ground.R").Timeline.Stages[0].AnimationIds.Single());
        Assert.Equal("anim.cyclone-kick", result.CookedPackage.Definition.Slots.Single(x => x.Id == "air.R").Timeline.Stages[0].AnimationIds.Single());
        Assert.Equal(new ushort[] { 25, 25, 5 }, result.CookedPackage.Definition.Slots.Single(x => x.Id == "ground.E").Timeline.Stages[0].Operations.OfType<CookedSpawnHitboxOperation>().Select(x => x.Hitbox.DurationTicks).ToArray());
        var groundR = result.CookedPackage.Definition.Slots.Single(x => x.Id == "ground.R");
        var presentation = Assert.IsType<CookedEmitPresentationOperation>(groundR.Timeline.Stages[0].Operations[1]);
        Assert.Equal("presentation.cyclone-kick.start", presentation.PresentationId);
        Assert.Equal(10, presentation.OperationIndex);
        Assert.Equal(Fixture("cooked.expected.hex").Trim(), Convert.ToHexString(result.CookedPackage.CanonicalBytes));
    }

    [Fact]
    public void WorkshopRejectsTrustedCapabilities()
    {
        var result = CharacterPackageCompiler.Compile(Fixture("package.json"), Fixture("character.json"), CharacterCookProfile.Workshop);
        AssertError(result, "capability.untrusted");
    }

    [Fact]
    public void LegacyAndFutureSchemasFailClosed()
    {
        AssertError(CompileCharacter(x => x["authoringSchemaVersion"] = 2), "schema.unsupported");
        AssertError(CompileCharacter(x => x.Remove("authoringSchemaVersion")), "schema.missing");
        AssertError(CompileCharacter(x => { x["schemaVersion"] = 1; x["id"] = "fightguy"; x["class"] = "FightGuy"; }), "schema.unsupported");
        var manifest = JsonNode.Parse(Fixture("package.json"))!.AsObject();
        manifest["manifestSchemaVersion"] = 2;
        var result = CharacterPackageCompiler.Compile(manifest.ToJsonString(), Fixture("character.json"), CharacterCookProfile.TrustedBuiltIn);
        AssertError(result, "schema.unsupported");
    }

    [Fact]
    public void StrictReaderRejectsUnknownDuplicateAndIntegerEnumFields()
    {
        AssertError(CompileCharacter(x => x["unknown"] = true), "field.unknown");
        AssertError(CompileCharacter(x => x["movement"]!["unknown"] = true), "field.unknown");
        var duplicate = Fixture("character.json").Replace("\"displayName\": \"FightGuy\",", "\"displayName\": \"FightGuy\",\n  \"displayName\": \"FightGuy\",", StringComparison.Ordinal);
        AssertError(CharacterPackageCompiler.Compile(Fixture("package.json"), duplicate, CharacterCookProfile.TrustedBuiltIn), "field.duplicate");
        AssertError(CompileCharacter(x => x["slots"]![0]!["behavior"] = 0), "enum.unknown");
    }

    [Fact]
    public void StrictReaderRejectsOperationsAndParameters()
    {
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![0]!["kind"] = "branch"), "operation.unknown");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![0]!["unit"] = "bogus"), "unit.unknown");
        AssertError(CompileCharacter(x => x["slots"]![8]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["extra"] = 1), "operation.parameter-unknown");
        AssertError(CompileCharacter(x => ((JsonObject)x["slots"]![8]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!).Remove("startupTicks")), "operation.parameter-missing");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![0]!["hitbox"]!["durationTicks"] = 0), "value.out-of-range");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![0]!["hitbox"]!["radius"] = -1), "value.out-of-range");
    }

    [Fact]
    public void AliasesResolveAndRejectMissingCyclesDuplicates()
    {
        var missing = CompileCharacter(x => ((JsonArray)x["aliases"]!).RemoveAt(0));
        AssertError(missing, "alias.missing-target");
        var cycle = CompileCharacter(x => { x["aliases"]![0]!["to"] = "air.E"; x["aliases"]![1]!["to"] = "air.A"; });
        AssertError(cycle, "alias.cycle");
        var duplicateAlias = CompileCharacter(x => ((JsonArray)x["aliases"]!).Add(((JsonArray)x["aliases"]!)[0]!.DeepClone()));
        AssertError(duplicateAlias, "id.duplicate");
        var duplicateSlot = CompileCharacter(x => ((JsonArray)x["slots"]!).Add(((JsonArray)x["slots"]!)[0]!.DeepClone()));
        AssertError(duplicateSlot, "id.duplicate");
    }

    [Fact]
    public void CapabilityAdmissionIsExact()
    {
        AssertError(CompileCharacter(x => x["capabilityRequirements"]![0]!["capabilityId"] = "slop.internal.fightguy.unknown.v1"), "capability.unknown");
        AssertError(CompileCharacter(x => x["capabilityRequirements"]![0]!["capabilityVersion"] = "2"), "capability.unknown");
        AssertError(CompileCharacter(x => x["slots"]![8]!["timeline"]!["stages"]![0]!["operations"]![0]!["capabilityVersion"] = "2"), "capability.version-mismatch");
    }

    [Fact]
    public void PresentationWarningsDoNotChangeCookedBytes()
    {
        var baseline = CompileCharacter();
        var warning = CompileCharacter(x => ((JsonArray)x["presentationIds"]!).Add("presentation.unused"));
        var warningRepeat = CompileCharacter(x => ((JsonArray)x["presentationIds"]!).Add("presentation.unused"));
        Assert.NotNull(warning.CookedPackage);
        Assert.Contains(warning.Diagnostics, x => x.Code == "presentation.unused-id" && x.Severity == CharacterDiagnosticSeverity.Warning);
        Assert.Equal(warning.CookedPackage!.CanonicalBytes, warningRepeat.CookedPackage!.CanonicalBytes);
    }
    
    [Fact]
    public void CanonicalBytesIgnoreWhitespaceAndPropertyOrderButPreserveOperationOrder()
    {
        var baseline = CompileCharacter();
        var reordered = JsonNode.Parse(Fixture("character.json"))!.AsObject();
        var compact = reordered.ToJsonString();
        var same = CharacterPackageCompiler.Compile(Fixture("package.json"), compact, CharacterCookProfile.TrustedBuiltIn);
        Assert.Equal(baseline.CookedPackage!.CanonicalBytes, same.CookedPackage!.CanonicalBytes);

        var swapped = CompileCharacter(x =>
        {
            var operations = (JsonArray)x["slots"]![1]!["timeline"]!["stages"]![0]!["operations"]!;
            operations[1]!["tick"] = operations[0]! ["tick"]!.GetValue<int>();
        });
        Assert.NotEqual(Convert.ToHexString(baseline.CookedPackage.CanonicalBytes), Convert.ToHexString(swapped.CookedPackage!.CanonicalBytes));
    }

    [Fact]
    public void ReferencesAndIdsAreValidated()
    {
        AssertError(CompileCharacter(x => x["hurtboxBoneDefs"]![0]!["boneId"] = "Bad Bone"), "id.invalid");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["animationIds"]![0] = "missing"), "reference.unresolved");
        AssertError(CompileCharacter(x => x["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]![0]!["hitbox"]!["startBoneId"] = "bone.missing"), "reference.unresolved");
    }

    [Fact]
    public void NullInputAndTrailingDataFailAsDiagnostics()
    {
        var nullResult = CharacterPackageCompiler.Compile(null!, Fixture("character.json"));
        AssertError(nullResult, "schema.missing");
        var trailing = CharacterPackageCompiler.Compile(Fixture("package.json"), Fixture("character.json") + " {}", CharacterCookProfile.TrustedBuiltIn);
        AssertError(trailing, "schema.invalid-json");
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
