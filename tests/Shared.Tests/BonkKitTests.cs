using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using System.Text.Json.Nodes;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BonkKitTests
{
    private const string CapabilityId = "slop.internal.bonk.targeted-jump-slam.v1";

    [Fact]
    public void Bonk_TrustedPackage_CooksCanonicalKit()
    {
        var first = CompileBonk();
        var second = CompileBonk();
        Assert.NotNull(first.CookedPackage);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(first.CookedPackage!.CanonicalBytes, second.CookedPackage!.CanonicalBytes);

        var package = first.CookedPackage;
        Assert.Equal(16, package.Definition.Slots.Count);
        Assert.Equal(16, package.Budget.SlotCount);
        Assert.Single(package.Definition.CapabilityRequirements);
        Assert.Equal(CapabilityId, package.Definition.CapabilityRequirements[0].CapabilityId);
        Assert.Equal("1", package.Definition.CapabilityRequirements[0].CapabilityVersion);

        var expectedIds = new[]
        {
            "ground.1", "ground.2", "ground.3", "ground.4",
            "ground.A", "ground.E", "ground.R", "ground.F",
            "air.1", "air.2", "air.3", "air.4",
            "air.A", "air.E", "air.R", "air.F"
        };
        Assert.Equal(expectedIds.OrderBy(x => x), package.Definition.Slots.Select(x => x.Id).OrderBy(x => x));
    }


    [Fact]
    public void BonkCookedArtifact_LoadsTypedCapabilityParameters()
    {
        var root = Path.GetDirectoryName(RepoFile("content-cooked/bonk/manifest.json"))!;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CharacterPackageAssembler.ManifestPath] = File.ReadAllBytes(Path.Combine(root, "manifest.json")),
            [CharacterPackageAssembler.RuntimePath] = File.ReadAllBytes(Path.Combine(root, "character.runtime.json")),
            [CharacterPackageAssembler.PosePath] = File.ReadAllBytes(Path.Combine(root, "poses.bin")),
            [CharacterPackageAssembler.BindingPath] = File.ReadAllBytes(Path.Combine(root, "client.bindings")),
        };
        var roster = BuiltInRosterManifestCodec.Load(RepoFile("content-cooked/roster/manifest.json"));
        var rosterEntry = roster.Resolve(CharacterClass.Bonk);
        Assert.NotNull(rosterEntry);
        Assert.Equal("bonk", rosterEntry!.PackageId);
        var loaded = CookedCharacterPackageLoader.LoadFiles(files, rosterEntry.Requirement);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        var stale = CookedCharacterPackageLoader.LoadFiles(
            files,
            rosterEntry.Requirement with { PackageHash = new string('0', 64) });
        Assert.False(stale.IsValid);
        Assert.Contains(stale.Diagnostics, x => x.Code == "package.identity.mismatch");
        var operation = Assert.IsType<CookedStartCapabilityOperation>(
            Assert.Single(loaded.Package!.Definition.Slots.Single(x => x.Id == "ground.E").Timeline.Stages.Single().Operations));
        var parameters = Assert.IsType<CookedBonkTargetedJumpSlamCapabilityParameters>(operation.Parameters);
        Assert.Equal((ushort)0, parameters.MaxAimTicks);
        Assert.Equal((ushort)72, parameters.MaxFlightTicks);
        Assert.Equal(1f, parameters.MinRange);
        Assert.Equal(12f, parameters.MaxRange);
        Assert.Equal(16f, parameters.LaunchVerticalSpeed);
        Assert.Equal(.42f, parameters.SlamRadius);
        Assert.Equal(13f, parameters.SlamDamage);
        Assert.Equal(55f, parameters.SlamAngle);
        Assert.Equal(9f, parameters.SlamBaseKnockback);
        Assert.Equal(32f, parameters.SlamKnockbackGrowth);
        Assert.Equal((ushort)20, parameters.SlamStunTicks);
        Assert.Equal((ushort)6, parameters.SlamDurationTicks);
    }

    [Fact]
    public void BonkCapabilityParametersRejectUnknownAndMissingFields()
    {
        var unknown = JsonNode.Parse(File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")))!.AsObject();
        var unknownParameters = (JsonObject)unknown["slots"]![5]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!;
        unknownParameters["extra"] = 1;
        var unknownResult = CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            unknown.ToJsonString(),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.Contains(unknownResult.Diagnostics, x => x.Code == "operation.parameter-unknown");
        Assert.Null(unknownResult.CookedPackage);

        var missing = JsonNode.Parse(File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")))!.AsObject();
        var missingParameters = (JsonObject)missing["slots"]![5]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!;
        missingParameters.Remove("slamDamage");
        var missingResult = CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            missing.ToJsonString(),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.Contains(missingResult.Diagnostics, x => x.Code == "operation.parameter-missing");
        Assert.Null(missingResult.CookedPackage);
    }

    [Fact]
    public void BonkCapabilityRegistryRequiresExactVersionAndType()
    {
        Assert.True(InternalCapabilityRegistry.TryCreate(
            CapabilityId,
            "1",
            new CookedBonkTargetedJumpSlamCapabilityParameters(120, 72, 1, 12, 16, .42f, 13, 55, 9, 32, 20, 6),
            out var capability));
        Assert.IsType<BonkTargetedJumpSlam>(capability);
        Assert.False(InternalCapabilityRegistry.TryCreate(CapabilityId, "2", new CookedBonkTargetedJumpSlamCapabilityParameters(120, 72, 1, 12, 16, .42f, 13, 55, 9, 32, 20, 6), out _));
        Assert.False(InternalCapabilityRegistry.TryCreate(CapabilityId, "1", new CookedRisingDragonCapabilityParameters(1, 1, 1), out _));
    }

    [Fact]
    public void BonkE_HoldsCachesReleaseYawAndSlamsOnLanding()
    {
        var east = RunE(9000, grounded: true, out var eastHitbox);
        Assert.Equal(ActionState.Attacking, east.State);
        Assert.True(eastHitbox);
        Assert.True(east.PX > 20f, "positive yaw must travel in positive world-space direction");

        var opposite = RunE(-9000, grounded: true, out var oppositeHitbox);
        Assert.True(oppositeHitbox);
        Assert.True(opposite.PX < 20f, "opposite yaw must travel in the opposite world-space direction");
    }
    [Fact]
    public void BonkE_HoldsPastAimCap_AllowsMovementAndReleasesCachedTarget()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);

        float startZ = state.PZ;
        var held = new InputState { ActiveSlot = 4, AimYaw = 9000, AimDistance = 600, MoveY = 0.25f, IsAiming = true };
        for (var i = 0; i < 300; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = held });
            Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
            Assert.NotNull(sim.GetActiveAbility(1));
            Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), x => x.OwnerId == 1 && x.Damage == 13f);
        }

        var heldState = sim.GetState(1);
        Assert.True(heldState.PZ > startZ, "mobile aim must preserve normal movement");

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new InputState { ActiveSlot = 4, AimYaw = -9000, AimDistance = 1200, IsAiming = false },
        });
        var released = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, released.State);
        Assert.True(released.VX > 0f, "release must use the cached held yaw");

        var slamWindows = 0;
        var wasActive = false;
        for (var i = 0; i < 120; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            var active = sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 13f);
            if (active && !wasActive) slamWindows++;
            wasActive = active;
        }
        Assert.Equal(1, slamWindows);
    }


    [Fact]
    public void BonkE_ExpiresOffstageWithoutPhantomSlam()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = 100f;
        state.IsGrounded = false;
        sim.RegisterEntity(1, def, state);
        var held = AimInput(9000, 600, 4, true);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = held });
        for (var i = 0; i < 9; i++) sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(9000, 600, 0, true) });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(9000, 600, 0, false) });
        for (var i = 0; i < 80; i++) sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Null(sim.GetActiveAbility(1));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), x => x.OwnerId == 1 && x.Damage == 13f);
    }

    [Fact]
    public void BonkF_ProducesFourLightAndOneHeavyIndependentContacts()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);
        var lightWindows = 0;
        var wasLight = false;
        var heavySeen = false;
        for (var i = 0; i < 60; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = i == 0 ? TestHelpers.Input(activeSlot: 6) : default });
            var active = sim.Resolver.GetActiveHitboxes();
            var light = active.Any(x => x.OwnerId == 1 && x.Damage == 2.5f);
            if (light && !wasLight) lightWindows++;
            wasLight = light;
            heavySeen |= active.Any(x => x.OwnerId == 1 && x.Damage == 12f);
            Assert.All(active.Where(x => x.OwnerId == 1), x =>
            {
                Assert.Equal(HitboxShape.Capsule, x.Shape);
                Assert.Equal(0f, x.VX);
                Assert.Equal(0f, x.VZ);
            });
        }
        Assert.Equal(4, lightWindows);
        Assert.True(heavySeen);
    }

    private static CharacterCompileResult CompileBonk()
    {
        return CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
    }

    private static CharacterDefinition BonkDefinition()
        => CookedCharacterRuntimeAdapter.ToCharacterDefinition(CompileBonk().CookedPackage!);

    private static CharacterState RunE(short yaw, bool grounded, out bool hitboxSeen)
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = grounded ? TestHelpers.GroundPY(def) : 100f;
        state.IsGrounded = grounded;
        sim.RegisterEntity(1, def, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(yaw, 600, 4, true) });
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        Assert.True(sim.GetState(1).IsAiming);
        for (var i = 0; i < 9; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = AimInput(yaw, 600, 0, true) });
            Assert.NotNull(sim.GetActiveAbility(1));
        }
        Assert.Equal((byte)4, sim.GetState(1).AttackSlot);
        var release = AimInput((short)-yaw, 600, 0, false);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = release });
        var afterRelease = sim.GetState(1);
        Assert.True(afterRelease.State == ActionState.Attacking, $"state={afterRelease.State} slot={afterRelease.AttackSlot} active={sim.GetActiveAbility(1)?.GetType().Name} py={afterRelease.PY} grounded={afterRelease.IsGrounded}");
        var flight = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, flight.State);
        Assert.False(flight.IsAiming);
        Assert.True(Math.Abs(flight.FacingYaw - yaw * .01f * MathF.PI / 180f) < .001f);
        Assert.True(Math.Sign(flight.VX) == Math.Sign(yaw), "release camera yaw must not replace cached held yaw");
        hitboxSeen = false;
        for (var i = 0; i < 80; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            if (sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 13f))
            {
                hitboxSeen = true;
                break;
            }
        }
        Assert.True(hitboxSeen, $"final state={sim.GetState(1).State} slot={sim.GetState(1).AttackSlot} px={sim.GetState(1).PX} pz={sim.GetState(1).PZ} py={sim.GetState(1).PY} grounded={sim.GetState(1).IsGrounded} active={sim.Resolver.GetActiveHitboxes().Count}");
        return sim.GetState(1);
    }

    private static InputState AimInput(short yaw, ushort distance, byte slot, bool aiming)
        => new() { AimYaw = yaw, AimDistance = distance, ActiveSlot = slot, IsAiming = aiming };

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
