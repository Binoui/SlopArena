using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Package-native Manki coverage: canonical kit validation, cooked capability loading,
/// real normal contacts, and special regression scenarios.
/// </summary>
public sealed class MankiKitScenarioTests : KitScenarioTests
{
    private const string RoundBombCapabilityId = "slop.internal.manki.round-bomb.v1";
    private const string JetpackCapabilityId = "slop.internal.manki.jetpack-boost.v1";
    private const string BazookaCapabilityId = "slop.internal.manki.bazooka.v1";
    private const string AerosolCapabilityId = "slop.internal.manki.aerosol-inferno.v1";

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
    public void Manki_TrustedPackage_CooksValidCanonicalKit()
    {
        var first = CompileManki();
        var second = CompileManki();
        Assert.True(first.CookedPackage != null, string.Join("; ", first.Diagnostics));
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(first.CookedPackage!.CanonicalBytes, second.CookedPackage!.CanonicalBytes);

        var package = first.CookedPackage;
        Assert.Equal(4, package.Definition.CapabilityRequirements.Count);
        Assert.Equal(new[] { AerosolCapabilityId, BazookaCapabilityId, JetpackCapabilityId, RoundBombCapabilityId },
            package.Definition.CapabilityRequirements.Select(x => x.CapabilityId).OrderBy(x => x).ToArray());
        Assert.Equal(16, package.Definition.Slots.Count);
        foreach (var slot in package.Definition.Slots.Where(x => x.Ordinal % 8 < 4))
        {
            var stage = Assert.Single(slot.Timeline.Stages);
            var hitboxes = stage.Operations.OfType<CookedSpawnHitboxOperation>().ToArray();
            Assert.NotEmpty(hitboxes);
            Assert.All(hitboxes, operation =>
            {
                Assert.InRange(operation.Tick + operation.Hitbox.DurationTicks, 1, stage.DurationTicks);
                Assert.True(operation.Hitbox.Damage > 0f);
            });
        }

        // Air Swing's sweet/sour windows must share hit history and the same limb sweep.
        var airSwing = package.Definition.Slots.Single(x => x.Id == "air.2").Timeline.Stages
            .SelectMany(stage => stage.Operations).OfType<CookedSpawnHitboxOperation>().ToArray();
        Assert.Equal(2, airSwing.Length);
        Assert.NotEqual((byte)0, airSwing[0].Hitbox.HitGroup);
        Assert.All(airSwing, operation =>
        {
            Assert.Equal(airSwing[0].Hitbox.HitGroup, operation.Hitbox.HitGroup);
            Assert.Equal(AuthoringHitboxShape.Capsule, operation.Hitbox.Shape);
            Assert.Equal("bone.left-foot", operation.Hitbox.StartBoneId);
            Assert.Equal("bone.hips", operation.Hitbox.EndBoneId);
        });

        AssertSpecial(package, "ground.A", RoundBombCapabilityId, AuthoringAbilityBehavior.AimedProjectile, AuthoringAimMode.GroundCursor,
            p => Assert.IsType<CookedMankiRoundBombCapabilityParameters>(p));
        AssertSpecial(package, "ground.E", JetpackCapabilityId, AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            p => Assert.IsType<CookedMankiJetpackBoostCapabilityParameters>(p));
        AssertSpecial(package, "ground.R", BazookaCapabilityId, AuthoringAbilityBehavior.Projectile, AuthoringAimMode.CameraForward3D,
            p => Assert.IsType<CookedMankiBazookaCapabilityParameters>(p));
        var jetpackSlot = package.Definition.Slots.Single(x => x.Id == "ground.E");
        Assert.True(jetpackSlot.IsRecoveryMove);
        Assert.False(jetpackSlot.PreserveMomentumOnStart);
        var airJetpack = package.Definition.Slots.Single(x => x.Id == "air.E");
        Assert.Equal(jetpackSlot.Name, airJetpack.Name);
        var aerosol = package.Definition.Slots.Single(x => x.Id == "ground.F");
        Assert.Equal(AuthoringAbilityBehavior.AreaDenial, aerosol.Behavior);
        Assert.Equal(AuthoringAimMode.CameraForward3D, aerosol.AimMode);
        Assert.Equal("anim.manki.gf-loop", aerosol.AimAnimationId);
        var aerosolStage = Assert.Single(aerosol.Timeline.Stages);
        Assert.Equal((ushort)0, aerosolStage.IasaTicks);
        Assert.Equal(2, aerosolStage.Operations.Count);
        var start = Assert.IsType<CookedStartCapabilityOperation>(aerosolStage.Operations[0]);
        Assert.Equal((ushort)0, start.Tick);
        var aerosolParameters = Assert.IsType<CookedMankiAerosolInfernoCapabilityParameters>(start.Parameters);
        Assert.InRange(aerosolParameters.FireTriggerTick + aerosolParameters.HitboxDurationTicks,
            1, aerosolStage.DurationTicks);
        Assert.True(aerosolParameters.Damage > 0f);
        var presentation = Assert.IsType<CookedEmitPresentationOperation>(aerosolStage.Operations[1]);
        Assert.Equal("presentation.manki.aerosol-inferno.start", presentation.PresentationId);
        Assert.Empty(package.Definition.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<CookedSetVelocityOperation>());
    }

    private static void AssertSpecial(CookedCharacterPackage package, string slotId, string capabilityId,
        AuthoringAbilityBehavior behavior, AuthoringAimMode aimMode,
        Action<CookedCapabilityParameters> parameterAssert)
    {
        var slot = package.Definition.Slots.Single(x => x.Id == slotId);
        Assert.Equal(behavior, slot.Behavior);
        Assert.Equal(aimMode, slot.AimMode);
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
        Assert.Equal("presentation.manki.round-bomb.explosion", bomb.ExplosionPresentationId);

        var jetpack = Assert.IsType<CookedMankiJetpackBoostCapabilityParameters>(
            Assert.IsType<CookedStartCapabilityOperation>(Assert.Single(package.Definition.Slots.Single(x => x.Id == "ground.E").Timeline.Stages.Single().Operations)).Parameters);
        Assert.Equal((ushort)3, jetpack.StartupTicks);
        Assert.Equal(15f, jetpack.VerticalSpeed);
        Assert.Equal(3.5f, jetpack.HorizontalSpeed);
        Assert.Equal(1.5f, jetpack.ExplosionRadius);
        Assert.Equal(10f, jetpack.ExplosionDamage);
        Assert.Equal(30f, jetpack.ExplosionKbAngle);
        Assert.Equal(2.4f, jetpack.ExplosionKbBase);
        Assert.Equal(24f, jetpack.ExplosionKbGrowth);
        Assert.Equal((ushort)18, jetpack.ExplosionStunTicks);
        Assert.Equal((ushort)8, jetpack.ExplosionDurationTicks);
        Assert.Equal("presentation.manki.jetpack-boost.ignition", jetpack.ExplosionPresentationId);

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
        Assert.Equal("presentation.manki.bazooka.explosion", bazooka.ExplosionPresentationId);
        var aerosol = package.Definition.Slots.Single(x => x.Id == "ground.F");
        Assert.Equal((ushort)52, aerosol.Timeline.Stages.Single().DurationTicks);
        var start = Assert.IsType<CookedStartCapabilityOperation>(aerosol.Timeline.Stages.Single().Operations[0]);
        var aerosolParameters = Assert.IsType<CookedMankiAerosolInfernoCapabilityParameters>(start.Parameters);
        Assert.Equal((ushort)18, aerosolParameters.FireTriggerTick);
        Assert.Equal((ushort)52, aerosolParameters.FireDurationTicks);
        Assert.Equal((ushort)28, aerosolParameters.HitboxDurationTicks);
        var emit = Assert.IsType<CookedEmitPresentationOperation>(aerosol.Timeline.Stages.Single().Operations[1]);
        Assert.Equal("presentation.manki.aerosol-inferno.start", emit.PresentationId);
    }
    [Fact]
    public void G1_MonkeyPunch_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "Manki G1 Monkey Punch Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot1, false), (float)npc.DamagePercent),
            TotalTicks = 80,
        });
    }

    [Fact]
    public void G2_StraightPunch_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "Manki G2 Straight Punch Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot2),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot2, false), (float)npc.DamagePercent),
            TotalTicks = 80,
        });
    }

    [Fact]
    public void G4_DoubleKick_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "Manki G4 Double Kick Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot4),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => TestHelpers.NpcState(0f, 0.8f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot4, false), (float)npc.DamagePercent),
            TotalTicks = 100,
        });
    }

    [Fact]
    public void A1_AirKick_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "Manki A1 Air Kick Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot1),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => AirborneNpc(0f) with { PY = 2f },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot1, true), (float)npc.DamagePercent),
            TotalTicks = 90,
        });
    }

    [Fact]
    public void A4_AirSmash_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "Manki A4 Air Smash Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.Slot4),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => AirborneNpc(0f) with { PY = 2f },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot4, true), (float)npc.DamagePercent),
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
                Assert.Equal((ushort)10, npc.DamagePercent);
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

    private static float NormalDamage(byte slot, bool airborne)
    {
        var damage = Def.GetCookedSlotAbility(slot, airborne)!.Timeline.Stages
            .SelectMany(stage => stage.Operations).OfType<CookedSpawnHitboxOperation>()
            .Sum(operation => operation.Hitbox.Damage);
        Assert.True(damage > 0f, "Hit-confirm scenarios require a damaging authored normal.");
        return damage;
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
