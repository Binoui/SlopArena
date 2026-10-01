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
        var second = CharacterPackageCompiler.Compile(Fixture("package.json"), Fixture("character.json"), CharacterCookProfile.TrustedBuiltIn);
        Assert.Equal(result.CookedPackage.CanonicalBytes, second.CookedPackage!.CanonicalBytes);
    }

    [Fact]
    public void AirDodgeSpeed_CooksAndAdaptsIndependentlyFromDashSpeed()
    {
        var result = CompileCharacter(character => character["movement"]!["airDodgeSpeed"] = 12.5f);
        Assert.NotNull(result.CookedPackage);
        Assert.Equal(20f, result.CookedPackage!.Definition.Movement.DashSpeed);
        Assert.Equal(12.5f, result.CookedPackage.Definition.Movement.AirDodgeSpeed);
        var runtime = CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage);
        Assert.Equal(12.5f, runtime.Movement.AirDodgeSpeed);
        Assert.Contains("\"airDodgeSpeed\":12.5", System.Text.Encoding.UTF8.GetString(result.CookedPackage.CanonicalBytes));

        AssertError(CompileCharacter(character => character["movement"]!["airDodgeSpeed"] = 0f), "value.out-of-range");
        AssertError(CompileCharacter(character => character["movement"]!["airDodgeSpeed"] = -1f), "value.out-of-range");
    }

    [Fact]
    public void ShieldRadius_CooksAndAdaptsExactly_AndRejectsInvalidGeometry()
    {
        var compiled = CompileCharacter();
        Assert.NotNull(compiled.CookedPackage);
        Assert.Equal(1.05f, compiled.CookedPackage!.Definition.ShieldRadius);
        var runtime = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage);
        Assert.Equal(1.05f, runtime.ShieldRadius);
        Assert.Contains("\"shieldRadius\":1.05", System.Text.Encoding.UTF8.GetString(compiled.CookedPackage.CanonicalBytes));

        AssertError(CompileCharacter(character => character["shieldRadius"] = 0f), "value.out-of-range");
        AssertError(CompileCharacter(character => character["shieldRadius"] = -1f), "value.out-of-range");
        AssertError(CompileCharacter(character => character["shieldRadius"] = 0.85f), "value.out-of-range");
        AssertError(CompileCharacter(character => character.Remove("shieldRadius")), "schema.missing");
        var source = CharacterPackageSourceCodec.Load(Fixture("package.json"), Fixture("character.json")).Source!;
        var nonFinite = source with { Character = source.Character with { ShieldRadius = float.PositiveInfinity } };
        AssertError(CharacterPackageCompiler.Compile(nonFinite, CharacterCookProfile.TrustedBuiltIn), "value.non-finite");
    }
    [Fact]
    public void ShieldRadius_IsPreservedForEveryPackageFighter()
    {
        foreach (var (packageId, expected) in new[]
        {
            ("fightguy", 1.05f), ("wibou", 1.05f), ("bonk", 1.05f), ("manki", 0.95f),
        })
        {
            var result = CharacterPackageCompiler.Compile(
                File.ReadAllText(FindRepoFile($"client/Unity/Assets/CharacterPackages/{packageId}/package.json")),
                File.ReadAllText(FindRepoFile($"client/Unity/Assets/CharacterPackages/{packageId}/character.json")),
                CharacterCookProfile.TrustedBuiltIn);
            Assert.NotNull(result.CookedPackage);
            Assert.Equal(expected, result.CookedPackage!.Definition.ShieldRadius);
            Assert.Equal(expected, CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage).ShieldRadius);
        }
    }
    [Fact]
    public void CaptureGeometryAndDefenseRoles_CookDeterministicallyIntoContent()
    {
        var baseline = CompileCharacter();
        var configured = CompileCharacter(character =>
        {
            character["captureGeometry"]!["reach"] = 1.1f;
            character["captureGeometry"]!["attackerAnchor"]!["z"] = 0.22f;
            character["presentation"]!["shield"] = "anim.fightguy.shield";
            character["presentation"]!["grab"] = "anim.fightguy.grab";
            character["presentation"]!["grabbed"] = "anim.fightguy.grabbed";
            character["presentation"]!["throwForward"] = "anim.fightguy.throw-forward";
            character["presentation"]!["airDodge"] = "anim.fightguy.air-dodge";
        });

        Assert.NotNull(baseline.CookedPackage);
        Assert.NotNull(configured.CookedPackage);
        var definition = configured.CookedPackage!.Definition;
        Assert.Equal(1.1f, definition.CaptureGeometry.Reach);
        Assert.Equal(0.22f, definition.CaptureGeometry.AttackerAnchor.Z);
        Assert.Equal("anim.fightguy.shield", definition.Presentation.Shield);
        Assert.Equal("anim.fightguy.grab", definition.Presentation.Grab);
        Assert.Equal("anim.fightguy.grabbed", definition.Presentation.Grabbed);
        Assert.Equal("anim.fightguy.throw-forward", definition.Presentation.ThrowForward);
        Assert.Equal("anim.fightguy.air-dodge", definition.Presentation.AirDodge);
        Assert.False(baseline.CookedPackage!.CanonicalBytes.SequenceEqual(configured.CookedPackage.CanonicalBytes));

        var repeated = CompileCharacter(character =>
        {
            character["captureGeometry"]!["reach"] = 1.1f;
            character["captureGeometry"]!["attackerAnchor"]!["z"] = 0.22f;
            character["presentation"]!["shield"] = "anim.fightguy.shield";
            character["presentation"]!["grab"] = "anim.fightguy.grab";
            character["presentation"]!["grabbed"] = "anim.fightguy.grabbed";
            character["presentation"]!["throwForward"] = "anim.fightguy.throw-forward";
            character["presentation"]!["airDodge"] = "anim.fightguy.air-dodge";
        });
        Assert.Equal(configured.CookedPackage.CanonicalBytes, repeated.CookedPackage!.CanonicalBytes);

        AssertError(CompileCharacter(character => character["captureGeometry"]!["reach"] = 0), "value.out-of-range");
        AssertError(CompileCharacter(character => character["presentation"]!["shield"] = "anim.idle"), "id.duplicate");
    }

    [Fact]
    public void SlideCarry_CompilesGroundNormalsAndDefaultsFalseElsewhere()
    {
        var result = CompileCharacter();
        Assert.NotNull(result.CookedPackage);
        Assert.DoesNotContain(result.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error);
        foreach (var id in new[] { "ground.1", "ground.2", "ground.3", "ground.4" })
            Assert.True(result.CookedPackage!.Definition.Slots.Single(x => x.Id == id).AllowSlideCarry);
        Assert.False(result.CookedPackage!.Definition.Slots.Single(x => x.Id == "ground.A").AllowSlideCarry);
        Assert.False(result.CookedPackage.Definition.Slots.Single(x => x.Id == "air.1").AllowSlideCarry);
    }

    [Fact]
    public void SlideCarry_AliasProjectsValueAndRejectsMotionConflict()
    {
        var aliased = CompileCharacter(character =>
        {
            var slots = (JsonArray)character["slots"]!;
            slots.Remove(slots.Single(slot => slot!["id"]!.GetValue<string>() == "ground.2"));
            ((JsonArray)character["aliases"]!).Add(new JsonObject { ["from"] = "ground.2", ["to"] = "ground.1" });
        });
        Assert.NotNull(aliased.CookedPackage);
        Assert.True(aliased.CookedPackage!.Definition.Slots.Single(x => x.Id == "ground.2").AllowSlideCarry);

        var conflict = CompileCharacter(character =>
        {
            var operations = (JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!;
            operations.Add(new JsonObject
            {
                ["kind"] = "setVelocity",
                ["tick"] = 1,
                ["unit"] = "metersPerSecond",
                ["velocityMode"] = "additive",
                ["x"] = 1,
                ["y"] = 0,
                ["z"] = 0,
            });
        });
        AssertError(conflict, "slot.slide-carry.motion-conflict");
    }

    [Fact]
    public void TumbleAndLowPoses_CookAndAdaptWithoutChangingTimelineData()
    {
        var result = CompileCharacter(character =>
        {
            var presentation = (JsonObject)character["presentation"]!;
            presentation["tumble"] = "anim.tumble";
            presentation["crouch"] = "anim.fightguy.crouch";
            presentation["slide"] = "anim.fightguy.slide";
        });
        Assert.NotNull(result.CookedPackage);
        Assert.DoesNotContain(result.Diagnostics, x => x.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal("anim.tumble", result.CookedPackage!.Definition.Presentation.Tumble);
        Assert.Equal("anim.fightguy.crouch", result.CookedPackage.Definition.Presentation.Crouch);
        Assert.Equal("anim.fightguy.slide", result.CookedPackage.Definition.Presentation.Slide);
        string canonical = System.Text.Encoding.UTF8.GetString(result.CookedPackage.CanonicalBytes);
        Assert.Contains("\"crouch\":\"anim.fightguy.crouch\"", canonical);
        Assert.Contains("\"slide\":\"anim.fightguy.slide\"", canonical);
        var runtime = CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage);
        Assert.Equal("anim.tumble", runtime.TumbleAnim);
        Assert.Equal("anim.fightguy.crouch", runtime.CrouchAnim);
        Assert.Equal("anim.fightguy.slide", runtime.SlideAnim);
    }
    [Fact]
    public void StageTargetingMetadata_SurvivesCompileAndRemainsDeterministic()
    {
        CharacterCompileResult result = CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            var stage = (JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
            stage["attackRange"] = 3.5;
            stage["warpRange"] = 4.25;
            stage["useTargetLock"] = false;
            stage["rotateTowardTarget"] = true;
            stage["trackingStrength"] = 0.4;
        });
        Assert.NotNull(result.CookedPackage);
        var stageResult = result.CookedPackage!.Definition.Slots.Single(x => x.Id == "ground.1").Timeline.Stages.Single();
        Assert.Equal(3.5f, stageResult.AttackRange);
        Assert.Equal(4.25f, stageResult.WarpRange);
        Assert.False(stageResult.UseTargetLock);
        Assert.True(stageResult.RotateTowardTarget);
        Assert.Equal(0.4f, stageResult.TrackingStrength);

        CharacterCompileResult repeated = CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            var stage = (JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
            stage["attackRange"] = 3.5;
            stage["warpRange"] = 4.25;
            stage["useTargetLock"] = false;
            stage["rotateTowardTarget"] = true;
            stage["trackingStrength"] = 0.4;
        });
        Assert.Equal(result.CookedPackage.CanonicalBytes, repeated.CookedPackage!.CanonicalBytes);
    }
    [Fact]
    public void Wibou_Package_CooksCanonicalSlotsAttachmentsSpreadAndChargePool()
    {
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(FindRepoFile("client/Unity/Assets/CharacterPackages/wibou/package.json")),
            File.ReadAllText(FindRepoFile("client/Unity/Assets/CharacterPackages/wibou/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(result.CookedPackage);
        var package = result.CookedPackage!;
        Assert.Equal(16, package.Definition.Slots.Count);
        Assert.Equal(new[] { "_weapon_hilt", "_weapon_tip" }, package.Definition.AttachmentBoneIds.OrderBy(x => x).ToArray());
        var shuriken = package.Definition.Slots.Single(x => x.Id == "ground.A").Timeline.Stages.Single().Operations.OfType<CookedSpawnProjectileOperation>().ToArray();
        Assert.Equal(new[] { -15f, 0f, 15f }, shuriken.Select(x => x.Projectile.YawOffsetDegrees).OrderBy(x => x).ToArray());
        Assert.Equal((2, (ushort)240), (package.Definition.Slots.Single(x => x.Id == "ground.E").ChargePool!.MaxCharges, package.Definition.Slots.Single(x => x.Id == "ground.E").ChargePool!.RegenTicks));
    }

    [Fact]
    public void ForwardLungeRequiresPositiveSpeedAndStageBoundedDuration()
    {
        CharacterCompileResult zeroSpeed = CompileCharacter(character =>
        {
            var operations = (JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!;
            operations.Add(new JsonObject
            {
                ["kind"] = "forwardLunge",
                ["tick"] = 1,
                ["unit"] = "metersPerSecond",
                ["speed"] = 0,
                ["durationTicks"] = 1,
            });
        });
        AssertError(zeroSpeed, "value.out-of-range");

        CharacterCompileResult pastStage = CompileCharacter(character =>
        {
            var stage = (JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
            var operations = (JsonArray)stage["operations"]!;
            operations.Add(new JsonObject
            {
                ["kind"] = "forwardLunge",
                ["tick"] = (int)stage["durationTicks"]! - 1,
                ["unit"] = "metersPerSecond",
                ["speed"] = 1,
                ["durationTicks"] = 2,
            });
        });
        AssertError(pastStage, "value.out-of-range");
    }

    [Fact]
    public void GravityWindowCompilesAsNormalizedTimedGravityAndValidatesBounds()
    {
        CharacterCompileResult compiled = CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            ((JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!).Add(new JsonObject
            {
                ["kind"] = "gravityWindow",
                ["tick"] = 2,
                ["unit"] = "normalized",
                ["gravityScale"] = 0.5,
                ["durationTicks"] = 4,
            });
        });

        Assert.True(compiled.CookedPackage != null,
            string.Join("; ", compiled.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        Assert.Equal("1.2.0", compiled.CookedPackage!.Metadata.RuntimeApiMin);
        var operation = Assert.Single(compiled.CookedPackage.Definition.Slots
            .Single(slot => slot.Id == "ground.1").Timeline.Stages[0].Operations
            .OfType<CookedGravityWindowOperation>());
        Assert.Equal((AuthoringUnit.Normalized, 2f, 0.5f, (ushort)4),
            (operation.Unit, (float)operation.Tick, operation.GravityScale, operation.DurationTicks));

        AssertError(CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            ((JsonArray)character["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!).Add(new JsonObject
            {
                ["kind"] = "gravityWindow",
                ["tick"] = 2,
                ["unit"] = "normalized",
                ["gravityScale"] = 1.01,
                ["durationTicks"] = 4,
            });
        }), "value.out-of-range");

        AssertError(CompileCharacter(character =>
        {
            character["slots"]![0]!["allowSlideCarry"] = false;
            var stage = (JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
            ((JsonArray)stage["operations"]!).Add(new JsonObject
            {
                ["kind"] = "gravityWindow",
                ["tick"] = (int)stage["durationTicks"]! - 1,
                ["unit"] = "normalized",
                ["gravityScale"] = 0.5,
                ["durationTicks"] = 2,
            });
        }), "value.out-of-range");
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
        AssertError(CompileCharacter(x => x["authoringSchemaVersion"] = 1), "schema.unsupported");
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
