using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Package-native Manki regression coverage: canonical kit compile, cooked artifact
/// loading with typed capability parameters, and golden snapshots for normals,
/// aerials, and the four specials (Round Bomb, Grapple, Bazooka, Overclock).
/// </summary>
public sealed class MankiKitScenarioTests : KitScenarioTests
{
    private const string RoundBombCapabilityId = "slop.internal.manki.round-bomb.v1";
    private const string GrappleCapabilityId = "slop.internal.manki.grapple.v1";
    private const string BazookaCapabilityId = "slop.internal.manki.bazooka.v1";
    private const string OverclockCapabilityId = "slop.internal.manki.overclock.v1";

    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static float GroundPy => TestHelpers.GroundPY(Def);

    private static CharacterState GroundedPlayer()
    {
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        return state;
    }

    private static CharacterState AirbornePlayer()
    {
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        return state;
    }

    private static CharacterState AirborneNpc(float z)
    {
        var state = TestHelpers.NpcState(0f, z);
        state.PY = 3f;
        state.IsGrounded = false;
        return state;
    }

    [Fact]
    public void Manki_TrustedPackage_CooksExactCanonicalKit()
    {
        var first = CompileManki();
        var second = CompileManki();
        Assert.NotNull(first.CookedPackage);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(first.CookedPackage!.CanonicalBytes, second.CookedPackage!.CanonicalBytes);

        var package = first.CookedPackage;
        Assert.Equal(16, package.Definition.Slots.Count);
        Assert.Equal(16, package.Budget.SlotCount);
        Assert.Equal(4, package.Definition.CapabilityRequirements.Count);
        Assert.Equal(new[] { BazookaCapabilityId, GrappleCapabilityId, OverclockCapabilityId, RoundBombCapabilityId },
            package.Definition.CapabilityRequirements.Select(x => x.CapabilityId).OrderBy(x => x).ToArray());

        var expected = new Dictionary<string, (ushort duration, ushort iasa, ushort trigger, ushort active, float radius, float damage, float angle, float @base, float growth, ushort stun, ushort landing, ushort before, ushort after)>
        {
            ["ground.1"] = (40, 36, 12, 8, .8f, 4, 15, 2, 1.5f, 20, 0, 0, 0),
            ["ground.2"] = (25, 22, 5, 5, .4f, 7, 25, 5, 26, 18, 0, 0, 0),
            ["ground.3"] = (29, 25, 7, 6, .4f, 7, 55, 5, 24, 18, 0, 0, 0),
            ["ground.4"] = (60, 56, 10, 7, .42f, 14, 28, 9, 42, 26, 0, 0, 0),
            ["air.1"] = (28, 24, 6, 6, .55f, 4, 15, 2, 1.5f, 18, 9, 5, 19),
            ["air.3"] = (44, 41, 14, 6, .35f, 8, 65, 5, 26, 20, 9, 5, 30),
            ["air.4"] = (54, 50, 20, 7, .4f, 13, 25, 8, 42, 26, 12, 5, 38),
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
            Assert.Equal(pair.Value.radius, hitbox.Radius);
            Assert.Equal(pair.Value.damage, hitbox.Damage);
            Assert.Equal(pair.Value.angle, hitbox.Angle);
            Assert.Equal(pair.Value.@base, hitbox.BaseKnockback);
            Assert.Equal(pair.Value.growth, hitbox.KnockbackGrowth);
            Assert.Equal(pair.Value.stun, hitbox.StunTicks);
            Assert.True(hitbox.Interruptible);
            Assert.Equal((byte)0, hitbox.HitGroup);
        }

        var air2 = package.Definition.Slots.Single(x => x.Id == "air.2").Timeline.Stages.Single();
        Assert.Equal(2, air2.Operations.Count);
        Assert.All(air2.Operations.OfType<CookedSpawnHitboxOperation>(), op =>
        {
            Assert.Equal(AuthoringHitboxShape.Capsule, op.Hitbox.Shape);
            Assert.Null(op.Hitbox.StartBoneId);
            Assert.Null(op.Hitbox.EndBoneId);
            Assert.Equal((byte)1, op.Hitbox.HitGroup);
        });
        Assert.All(package.Definition.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<CookedSpawnHitboxOperation>(),
            op => Assert.Null(op.Hitbox.StartBoneId));

        AssertSpecial(package, "ground.A", RoundBombCapabilityId, AuthoringAbilityBehavior.AimedProjectile, AuthoringAimMode.GroundCursor, 300,
            p => Assert.IsType<CookedMankiRoundBombCapabilityParameters>(p));
        AssertSpecial(package, "ground.E", GrappleCapabilityId, AuthoringAbilityBehavior.Projectile, AuthoringAimMode.CameraForward3D, 210,
            p => Assert.IsType<CookedMankiGrappleCapabilityParameters>(p));
        AssertSpecial(package, "ground.R", BazookaCapabilityId, AuthoringAbilityBehavior.Projectile, AuthoringAimMode.CameraForward3D, 240,
            p => Assert.IsType<CookedMankiBazookaCapabilityParameters>(p));
        AssertSpecial(package, "ground.F", OverclockCapabilityId, AuthoringAbilityBehavior.SelfBuff, AuthoringAimMode.None, 600,
            p => Assert.IsType<CookedMankiOverclockCapabilityParameters>(p));

        Assert.Empty(package.Definition.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<CookedSetVelocityOperation>());
    }

    private static void AssertSpecial(CookedCharacterPackage package, string slotId, string capabilityId,
        AuthoringAbilityBehavior behavior, AuthoringAimMode aimMode, ushort cooldown,
        Action<CookedCapabilityParameters> parameterAssert)
    {
        var slot = package.Definition.Slots.Single(x => x.Id == slotId);
        Assert.Equal(behavior, slot.Behavior);
        Assert.Equal(aimMode, slot.AimMode);
        Assert.Equal(cooldown, slot.CooldownTicks);
        var operation = Assert.IsType<CookedStartCapabilityOperation>(Assert.Single(slot.Timeline.Stages.Single().Operations));
        Assert.Equal((ushort)0, operation.Tick);
        Assert.Equal(capabilityId, operation.CapabilityId);
        Assert.Equal("1", operation.CapabilityVersion);
        parameterAssert(operation.Parameters);
    }

    [Fact]
    public void MankiCookedArtifact_LoadsTypedCapabilityParameters()
    {
        var root = Path.GetDirectoryName(RepoFile("content-cooked/manki/manifest.json"))!;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CharacterPackageAssembler.ManifestPath] = File.ReadAllBytes(Path.Combine(root, "manifest.json")),
            [CharacterPackageAssembler.RuntimePath] = File.ReadAllBytes(Path.Combine(root, "character.runtime.json")),
            [CharacterPackageAssembler.PosePath] = File.ReadAllBytes(Path.Combine(root, "poses.bin")),
            [CharacterPackageAssembler.BindingPath] = File.ReadAllBytes(Path.Combine(root, "client.bindings")),
        };
        var roster = BuiltInRosterManifestCodec.Load(RepoFile("content-cooked/roster/manifest.json"));
        var rosterEntry = roster.Resolve(CharacterClass.Manki);
        Assert.NotNull(rosterEntry);
        Assert.Equal("manki", rosterEntry!.PackageId);
        Assert.Equal("0.0.0-dev", rosterEntry.Requirement.Version);
        var loaded = CookedCharacterPackageLoader.LoadFiles(files, rosterEntry.Requirement);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        var stale = CookedCharacterPackageLoader.LoadFiles(
            files,
            rosterEntry.Requirement with { PackageHash = new string('0', 64) });
        Assert.False(stale.IsValid);
        Assert.Contains(stale.Diagnostics, x => x.Code == "package.identity.mismatch");

        var package = loaded.Package!;
        Assert.Equal("Manki", package.Definition.DisplayName);
        Assert.Equal(16, package.Definition.Slots.Count);

        var bomb = Assert.IsType<CookedMankiRoundBombCapabilityParameters>(
            Assert.IsType<CookedStartCapabilityOperation>(Assert.Single(package.Definition.Slots.Single(x => x.Id == "ground.A").Timeline.Stages.Single().Operations)).Parameters);
        Assert.Equal((ushort)10, bomb.ThrowTriggerTick);
        Assert.Equal(12f, bomb.MaxRange);
        Assert.Equal(30f, bomb.LaunchAngle);
        Assert.Equal(30f, bomb.Gravity);
        Assert.Equal(.6f, bomb.HitboxRadius);
        Assert.Equal(6f, bomb.Damage);
        Assert.Equal((ushort)22, bomb.StunTicks);
        Assert.Equal((ushort)90, bomb.MaxFlightTicks);
        Assert.Equal(30f, bomb.KbAngle);
        Assert.Equal(10f, bomb.ExplosionDamage);
        Assert.Equal(3f, bomb.ExplosionRadius);
        Assert.Equal(2.4f, bomb.ExplosionKbBase);
        Assert.Equal(3.6f, bomb.ExplosionKbGrowth);
        Assert.Equal((ushort)18, bomb.ExplosionStunTicks);
        Assert.Equal((ushort)8, bomb.ExplosionDurationTicks);
        Assert.Equal(30f, bomb.ExplosionKbAngle);

        var grapple = Assert.IsType<CookedMankiGrappleCapabilityParameters>(
            Assert.IsType<CookedStartCapabilityOperation>(Assert.Single(package.Definition.Slots.Single(x => x.Id == "ground.E").Timeline.Stages.Single().Operations)).Parameters);
        Assert.Equal((ushort)8, grapple.FireTriggerTick);
        Assert.Equal(40f, grapple.TetherSpeed);
        Assert.Equal(.3f, grapple.HitboxRadius);
        Assert.Equal((ushort)30, grapple.MaxFlightTicks);
        Assert.Equal(15f, grapple.MaxRange);
        Assert.Equal(25f, grapple.ReelSpeed);
        Assert.Equal(.5f, grapple.ArrivalThreshold);
        Assert.Equal(3f, grapple.Damage);
        Assert.Equal((ushort)0, grapple.StunTicks);
        Assert.Equal(0f, grapple.KbAngle);
        Assert.Equal((ushort)30, grapple.CastDuration);

        var bazooka = Assert.IsType<CookedMankiBazookaCapabilityParameters>(
            Assert.IsType<CookedStartCapabilityOperation>(Assert.Single(package.Definition.Slots.Single(x => x.Id == "ground.R").Timeline.Stages.Single().Operations)).Parameters);
        Assert.Equal((ushort)6, bazooka.FireTriggerTick);
        Assert.Equal(40f, bazooka.ProjectileSpeed);
        Assert.Equal(.6f, bazooka.HitboxRadius);
        Assert.Equal(15f, bazooka.Damage);
        Assert.Equal(15f, bazooka.Gravity);
        Assert.Equal((ushort)45, bazooka.MaxFlightTicks);
        Assert.Equal((ushort)24, bazooka.StunTicks);
        Assert.Equal(3f, bazooka.ExplosionRadius);
        Assert.Equal(25f, bazooka.KbAngle);
        Assert.Equal(6f, bazooka.ExplosionKbBase);
        Assert.Equal(9f, bazooka.ExplosionKbGrowth);
        Assert.Equal((ushort)22, bazooka.ExplosionStunTicks);
        Assert.Equal((ushort)6, bazooka.ExplosionDurationTicks);
        Assert.Equal(25f, bazooka.ExplosionKbAngle);
        Assert.Equal((ushort)20, bazooka.CastDuration);
        Assert.Equal((ushort)15, bazooka.RecoveryDuration);

        var overclock = Assert.IsType<CookedMankiOverclockCapabilityParameters>(
            Assert.IsType<CookedStartCapabilityOperation>(Assert.Single(package.Definition.Slots.Single(x => x.Id == "ground.F").Timeline.Stages.Single().Operations)).Parameters);
        Assert.Equal((ushort)480, overclock.DurationTicks);
    }

    // ── Golden scenarios: normals ──

    [Fact]
    public void G1_MonkeyPunch_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki G1 Monkey Punch Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)4, npc.DamagePercent),
            SnapshotTick = 14, // t12–19 active window.
            TotalTicks = 80,
        });
    }

    [Fact]
    public void G2_StraightPunch_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki G2 Straight Punch Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot2),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)7, npc.DamagePercent),
            SnapshotTick = 7, // t5–9 active window.
            TotalTicks = 80,
        });
    }

    [Fact]
    public void G4_DoubleKick_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki G4 Double Kick Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot4),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 0.8f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)14, npc.DamagePercent),
            SnapshotTick = 12, // t10–16 capsule active across both feet.
            TotalTicks = 100,
        });
    }

    [Fact]
    public void A1_AirKick_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki A1 Air Kick Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1),
            Assert = _ => { },
            NpcSetup = () => AirborneNpc(1f),
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)4, npc.DamagePercent),
            SnapshotTick = 8, // t6–11 active window.
            TotalTicks = 90,
        });
    }

    [Fact]
    public void A4_AirSmash_HitConfirm_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki A4 Air Smash Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot4),
            Assert = _ => { },
            NpcSetup = () => AirborneNpc(1f),
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)13, npc.DamagePercent),
            SnapshotTick = 22, // t20–26 active window.
            TotalTicks = 120,
        });
    }

    // ── Golden scenarios: specials ──

    [Fact]
    public void A_RoundBomb_HoldReleaseLobsBomb_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki A Round Bomb Hold Release Lob",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = HoldRelease(AbilitySlots.A, aimDistance: 600),
            Assert = _ => { },
            SnapshotTick = 34, // release t20 → fire t30; bomb in flight.
            TotalTicks = 120,
        });
    }

    [Fact]
    public void E_Grapple_HoldReleaseFiresTether_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki E Grapple Hold Release Fire",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = HoldRelease(AbilitySlots.E, aimDistance: 600),
            Assert = _ => { },
            SnapshotTick = 32, // release t20 → tether fires t28; tether in flight.
            TotalTicks = 110,
        });
    }

    [Fact]
    public void R_Bazooka_AimDownRocketJump_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki R Bazooka Rocket Jump",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = HoldRelease(AbilitySlots.R, aimDistance: 600, aimPitch: -9000), // -90°: straight down at the feet.
            Assert = _ => { },
            SnapshotTick = 30, // release t20 → rocket fires t26 → ground explosion near self.
            TotalTicks = 100,
        });
    }

    [Fact]
    public void F_Overclock_ActivatesBuff_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki F Overclock Buff",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.F),
            Assert = final => Assert.True(final.BuffRemainingTicks > 0, "buff must persist after the injection animation"),
            SnapshotTick = 15, // mid-injection; buff already active.
            TotalTicks = 60,
        });
    }

    private static InputSequence HoldRelease(byte slot, ushort aimDistance, short aimPitch = 0)
    {
        var held = new InputState { ActiveSlot = slot, IsAiming = true, AimDistance = aimDistance, AimPitch = aimPitch };
        var sequence = new InputSequence();
        for (var tick = 0; tick <= 19; tick++) sequence.Set(tick, held);
        sequence.Set(20, new InputState { IsAiming = false, AimPitch = aimPitch });
        return sequence;
    }

    private static CharacterCompileResult CompileManki()
    {
        return CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/manki/package.json")),
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/manki/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
    }

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
