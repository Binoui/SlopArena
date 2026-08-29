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
    public void Bonk_TrustedPackage_CooksExactCanonicalKit()
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

        var expected = new Dictionary<string, (ushort duration, ushort iasa, ushort trigger, ushort active, float radius, float damage, float angle, float @base, float growth, ushort stun, ushort landing, ushort before, ushort after)>
        {
            ["ground.1"] = (35, 30, 13, 11, .28f, 6, 30, 4, 20, 12, 0, 0, 0),
            ["ground.2"] = (42, 37, 16, 12, .33f, 10, 35, 7, 30, 16, 0, 0, 0),
            ["ground.3"] = (38, 33, 13, 12, .30f, 9, 78, 6, 26, 16, 0, 0, 0),
            ["ground.4"] = (58, 52, 20, 13, .38f, 15, 25, 10, 42, 22, 0, 0, 0),
            ["air.1"] = (46, 41, 18, 16, .28f, 8, 35, 5, 24, 14, 20, 15, 34),
            ["air.3"] = (54, 48, 23, 17, .32f, 11, -45, 7, 30, 20, 22, 16, 40),
            ["air.4"] = (66, 59, 28, 18, .38f, 14, 25, 9, 40, 22, 24, 16, 49),
        };
        foreach (var pair in expected)
        {
            var stage = package.Definition.Slots.Single(x => x.Id == pair.Key).Timeline.Stages.Single();
            var operation = Assert.IsType<CookedSpawnHitboxOperation>(Assert.Single(stage.Operations));
            var hitbox = operation.Hitbox;
            Assert.Equal(pair.Value.duration, stage.DurationTicks);
            Assert.Equal(pair.Value.iasa, stage.IasaTicks);
            Assert.Equal(pair.Value.landing, stage.LandingLagTicks);
            Assert.Equal(pair.Value.before, stage.AutoCancelBeforeTicks);
            Assert.Equal(pair.Value.after, stage.AutoCancelAfterTicks);
            Assert.Equal(pair.Value.trigger, operation.Tick);
            Assert.Equal(pair.Value.active, hitbox.DurationTicks);
            Assert.Equal(AuthoringHitboxShape.Capsule, hitbox.Shape);
            Assert.Equal(pair.Value.radius, hitbox.Radius);
            Assert.Equal(pair.Value.damage, hitbox.Damage);
            Assert.Equal(pair.Value.angle, hitbox.Angle);
            Assert.Equal(pair.Value.@base, hitbox.BaseKnockback);
            Assert.Equal(pair.Value.growth, hitbox.KnockbackGrowth);
            Assert.Equal(pair.Value.stun, hitbox.StunTicks);
            Assert.True(hitbox.Interruptible);
            Assert.Equal((byte)0, hitbox.HitGroup);
            Assert.Equal("_weapon_hilt", hitbox.StartBoneId);
            Assert.Equal("_weapon_tip", hitbox.EndBoneId);
        }

        foreach (var id in new[] { "ground.E", "air.E" })
        {
            var slot = package.Definition.Slots.Single(x => x.Id == id);
            Assert.Equal(AuthoringAbilityBehavior.AimedProjectile, slot.Behavior);
            Assert.Equal(AuthoringAimMode.GroundCursor, slot.AimMode);
            Assert.Equal((ushort)240, slot.CooldownTicks);
            Assert.True(slot.IsRecoveryMove);
            var stage = Assert.Single(slot.Timeline.Stages);
            Assert.Equal((ushort)180, stage.DurationTicks);
            var operation = Assert.IsType<CookedStartCapabilityOperation>(Assert.Single(stage.Operations));
            Assert.Equal((ushort)0, operation.Tick);
            Assert.Equal(CapabilityId, operation.CapabilityId);
            Assert.IsType<CookedBonkTargetedJumpSlamCapabilityParameters>(operation.Parameters);
        }

        var f = package.Definition.Slots.Single(x => x.Id == "ground.F");
        Assert.Equal((ushort)900, f.CooldownTicks);
        Assert.Equal((ushort)56, f.Timeline.Stages.Single().DurationTicks);
        var storm = f.Timeline.Stages.Single().Operations.OfType<CookedSpawnHitboxOperation>().ToArray();
        Assert.Equal(new ushort[] { 8, 16, 24, 32, 44 }, storm.Select(x => x.Tick).ToArray());
        Assert.Equal(new[] { 2.5f, 2.5f, 2.5f, 2.5f, 12f }, storm.Select(x => x.Hitbox.Damage).ToArray());
        Assert.All(storm, x =>
        {
            Assert.Equal(AuthoringHitboxShape.Capsule, x.Hitbox.Shape);
            Assert.Equal("_weapon_hilt", x.Hitbox.StartBoneId);
            Assert.Equal("_weapon_tip", x.Hitbox.EndBoneId);
            Assert.Equal((byte)0, x.Hitbox.HitGroup);
            Assert.True(x.Hitbox.Interruptible);
        });

        var unlisted = new[] { "air.2", "ground.A", "ground.R", "air.A", "air.R", "air.F" };
        Assert.All(unlisted, id => Assert.Empty(package.Definition.Slots.Single(x => x.Id == id).Timeline.Stages.Single().Operations));
        Assert.Empty(package.Definition.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<CookedSetVelocityOperation>());
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
