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
/// aerials, and the four specials (Round Bomb, Jetpack Boost, Bazooka, Aerosol Inferno).
/// </summary>
public sealed class MankiKitScenarioTests : KitScenarioTests
{
    private const string RoundBombCapabilityId = "slop.internal.manki.round-bomb.v1";
    private const string JetpackCapabilityId = "slop.internal.manki.jetpack-boost.v1";
    private const string BazookaCapabilityId = "slop.internal.manki.bazooka.v1";

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
        Assert.Equal(3, package.Definition.CapabilityRequirements.Count);
        Assert.Equal(new[] { BazookaCapabilityId, JetpackCapabilityId, RoundBombCapabilityId },
            package.Definition.CapabilityRequirements.Select(x => x.CapabilityId).OrderBy(x => x).ToArray());
        var expected = new Dictionary<string, (ushort duration, ushort iasa, ushort trigger, ushort active, float radius, float damage, float angle, float @base, float growth, ushort stun, ushort landing, ushort before, ushort after)>
        {
            ["ground.1"] = (17, 13, 4, 5, .35f, 4, 8, 4, 20, 14, 0, 0, 0),
            ["ground.2"] = (25, 22, 5, 5, .4f, 7, 25, 5, 26, 18, 0, 0, 0),
            ["ground.3"] = (29, 25, 7, 6, .4f, 7, 55, 5, 24, 18, 0, 0, 0),
            ["ground.4"] = (60, 56, 10, 7, .42f, 14, 28, 9, 42, 26, 0, 0, 0),
            ["air.1"] = (33, 29, 6, 5, .30f, 3, 55, 5, 24, 12, 9, 5, 23),
            ["air.3"] = (44, 41, 14, 6, .35f, 8, 65, 5, 26, 20, 9, 5, 30),
            ["air.4"] = (54, 50, 20, 7, .4f, 13, 25, 8, 42, 26, 12, 5, 38),
        };
        foreach (var pair in expected)
        {
            var stage = package.Definition.Slots.Single(x => x.Id == pair.Key).Timeline.Stages.Single();
            var operation = Assert.IsType<CookedSpawnHitboxOperation>(stage.Operations.First());
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
            Assert.Equal("bone.left-foot", op.Hitbox.StartBoneId);
            Assert.Equal("bone.hips", op.Hitbox.EndBoneId);
            Assert.Equal((byte)1, op.Hitbox.HitGroup);
        });

        AssertSpecial(package, "ground.A", RoundBombCapabilityId, AuthoringAbilityBehavior.AimedProjectile, AuthoringAimMode.GroundCursor, 300,
            p => Assert.IsType<CookedMankiRoundBombCapabilityParameters>(p));
        AssertSpecial(package, "ground.E", JetpackCapabilityId, AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 210,
            p => Assert.IsType<CookedMankiJetpackBoostCapabilityParameters>(p));
        AssertSpecial(package, "ground.R", BazookaCapabilityId, AuthoringAbilityBehavior.Projectile, AuthoringAimMode.CameraForward3D, 240,
            p => Assert.IsType<CookedMankiBazookaCapabilityParameters>(p));
        var jetpackSlot = package.Definition.Slots.Single(x => x.Id == "ground.E");
        Assert.True(jetpackSlot.IsRecoveryMove);
        Assert.False(jetpackSlot.PreserveMomentumOnStart);
        Assert.Equal((ushort)60, jetpackSlot.Timeline.Stages.Single().DurationTicks);
        var airJetpack = package.Definition.Slots.Single(x => x.Id == "air.E");
        Assert.Equal(jetpackSlot.Name, airJetpack.Name);
        Assert.Equal(jetpackSlot.Description, airJetpack.Description);
        var aerosol = package.Definition.Slots.Single(x => x.Id == "ground.F");
        Assert.Equal(AuthoringAbilityBehavior.AreaDenial, aerosol.Behavior);
        Assert.Equal(AuthoringAimMode.None, aerosol.AimMode);
        Assert.Equal((ushort)600, aerosol.CooldownTicks);
        var aerosolStage = Assert.Single(aerosol.Timeline.Stages);
        Assert.Equal((ushort)52, aerosolStage.DurationTicks);
        Assert.Equal((ushort)0, aerosolStage.IasaTicks);
        Assert.Equal(2, aerosolStage.Operations.Count);
        var presentation = Assert.IsType<CookedEmitPresentationOperation>(aerosolStage.Operations[0]);
        Assert.Equal((ushort)18, presentation.Tick);
        Assert.Equal("presentation.manki.aerosol-inferno.start", presentation.PresentationId);
        var flame = Assert.IsType<CookedSpawnHitboxOperation>(aerosolStage.Operations[1]);
        Assert.Equal((ushort)18, flame.Tick);
        Assert.Equal(AuthoringHitboxShape.Capsule, flame.Hitbox.Shape);
        Assert.Equal(1.25f, flame.Hitbox.Radius);
        Assert.Equal(0f, flame.Hitbox.OffsetX);
        Assert.Equal(0.25f, flame.Hitbox.OffsetY);
        Assert.Equal(1.25f, flame.Hitbox.OffsetZ);
        Assert.Equal(0f, flame.Hitbox.EndOffsetX);
        Assert.Equal(4.5f, flame.Hitbox.EndOffsetY);
        Assert.Equal(1.25f, flame.Hitbox.EndOffsetZ);
        Assert.Equal(15f, flame.Hitbox.Damage);
        Assert.Equal(55f, flame.Hitbox.Angle);
        Assert.Equal(12f, flame.Hitbox.BaseKnockback);
        Assert.Equal(20f, flame.Hitbox.KnockbackGrowth);
        Assert.Equal((ushort)30, flame.Hitbox.StunTicks);
        Assert.Equal((ushort)28, flame.Hitbox.DurationTicks);
        Assert.True(flame.Hitbox.Interruptible);
        Assert.Equal((byte)1, flame.Hitbox.HitGroup);

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
        Assert.Equal(24f, bomb.ExplosionKbGrowth);
        Assert.Equal((ushort)18, bomb.ExplosionStunTicks);
        Assert.Equal((ushort)8, bomb.ExplosionDurationTicks);
        Assert.Equal(30f, bomb.ExplosionKbAngle);

        var jetpack = Assert.IsType<CookedMankiJetpackBoostCapabilityParameters>(
            Assert.IsType<CookedStartCapabilityOperation>(Assert.Single(package.Definition.Slots.Single(x => x.Id == "ground.E").Timeline.Stages.Single().Operations)).Parameters);
        Assert.Equal((ushort)3, jetpack.StartupTicks);
        Assert.Equal(15f, jetpack.VerticalSpeed);
        Assert.Equal(3.5f, jetpack.HorizontalSpeed);
        Assert.Equal(1.25f, jetpack.ExplosionRadius);
        Assert.Equal(4f, jetpack.ExplosionDamage);
        Assert.Equal(75f, jetpack.ExplosionKbAngle);
        Assert.Equal(2f, jetpack.ExplosionKbBase);
        Assert.Equal(8f, jetpack.ExplosionKbGrowth);
        Assert.Equal((ushort)8, jetpack.ExplosionStunTicks);
        Assert.Equal((ushort)4, jetpack.ExplosionDurationTicks);

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
        Assert.Equal(42f, bazooka.ExplosionKbGrowth);
        Assert.Equal((ushort)22, bazooka.ExplosionStunTicks);
        Assert.Equal((ushort)6, bazooka.ExplosionDurationTicks);
        Assert.Equal(25f, bazooka.ExplosionKbAngle);
        Assert.Equal((ushort)20, bazooka.CastDuration);
        Assert.Equal((ushort)15, bazooka.RecoveryDuration);

        var aerosol = package.Definition.Slots.Single(x => x.Id == "ground.F");
        Assert.Equal((ushort)52, aerosol.Timeline.Stages.Single().DurationTicks);
        var emit = Assert.IsType<CookedEmitPresentationOperation>(aerosol.Timeline.Stages.Single().Operations[0]);
        Assert.Equal("presentation.manki.aerosol-inferno.start", emit.PresentationId);
        var flame = Assert.IsType<CookedSpawnHitboxOperation>(aerosol.Timeline.Stages.Single().Operations[1]);
        Assert.Equal(15f, flame.Hitbox.Damage);
        Assert.Equal((ushort)28, flame.Hitbox.DurationTicks);
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
            NpcSetup = () => AirborneNpc(0f) with { PY = 2f },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal((ushort)8, npc.DamagePercent), // Tracking connects both authored hits: t6 (3) + t16 (5).
            SnapshotTick = 8, // First hit's t6–10 active window; final assertion covers both hits.
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
            NpcSetup = () => AirborneNpc(0f) with { PY = 2f },
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
    public void E_JetpackBoost_Ignition_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki E Jetpack Boost Ignition",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence()
                .Set(0, new InputState { ActiveSlot = AbilitySlots.E, MoveX = 0.6f, MoveY = 0.8f })
                .Set(1, new InputState { MoveX = 0.6f, MoveY = 0.8f })
                .Set(2, new InputState { MoveX = 0.6f, MoveY = 0.8f })
                .Set(3, new InputState { MoveX = 0.6f, MoveY = 0.8f }),
            Assert = player =>
            {
                Assert.True(player.PY > GroundPy);
                Assert.True(player.VY > 0f);
                Assert.False(player.IsGrounded);
                Assert.Equal((byte)AbilitySlots.E, player.AttackSlot);
            },
            NpcSetup = () => TestHelpers.NpcState(0f, 0.75f) with { PY = GroundPy },
            NpcAssert = npc =>
            {
                Assert.Equal((ushort)4, npc.DamagePercent);
                Assert.True(npc.KVY > 0f, $"NPC should launch upward, got {npc.KVY}");
            },
            SnapshotTick = 10,
            TotalTicks = 11,
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
    public void F_AerosolInferno_HitsAfterTemporaryInvincibilityWithoutHittingBeyondEndpoint()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, GroundedPlayer(), TestHelpers.LoadBakedData(Def));
        sim.RegisterEntity(100, TestHelpers.CombatDef,
            TestHelpers.NpcState(0f, 1f) with { PY = GroundPy, FacingYaw = MathF.PI / 2f });
        sim.RegisterEntity(101, TestHelpers.CombatDef,
            TestHelpers.NpcState(8f, 8f) with { EntityId = 101, PY = GroundPy });

        for (var tick = 0; tick < 17; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.F } : default,
                [100] = default,
                [101] = default,
            });
            Assert.Equal(0f, sim.GetState(100).DamagePercent);
            Assert.Equal(0f, sim.GetState(101).DamagePercent);
        }

        var invincible = sim.GetState(100) with { InvincibilityTicks = 4, VX = 0f, VY = 0f, VZ = 0f };
        sim.SetState(100, invincible);
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = default,
            [100] = default,
            [101] = default,
        });
        Assert.Equal(0f, sim.GetState(100).DamagePercent);
        Assert.Equal(0f, sim.GetState(101).DamagePercent);

        for (var tick = 0; tick < 12; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = default,
                [100] = default,
                [101] = default,
            });
        }

        Assert.Equal(15f, sim.GetState(100).DamagePercent);
        Assert.Equal(0f, sim.GetState(101).DamagePercent);
    }

    [Fact]
    public void F_AerosolInferno_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki F Aerosol Inferno",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.F),
            Assert = _ => { },
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcAssert = npc => Assert.Equal(15f, npc.DamagePercent),
            SnapshotTick = 30,
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
