using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BonkSpecialTests
{
    private const ulong BonkId = 1;
    private const ulong VictimId = 100;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BonkR_RealPoseStarterConvertsToFinisherAcrossPercentAndEscapeInputs(bool airborne)
    {
        var bonk = CompileBonk();
        var baked = LoadBonkPoses();
        var victimDef = CompileBonk();
        foreach (int percent in new[] { 0, 60, 120, 180 })
        foreach (var escape in new[] { (X: 0f, Z: 0f), (X: 0f, Z: 1f), (X: 1f, Z: 0f), (X: 0f, Z: -1f) })
        {
            var sim = TestHelpers.MakeSim();
            var attacker = TestHelpers.PlayerState(100f, 100f);
            attacker.PY = airborne ? 50f : TestHelpers.GroundPY(bonk);
            attacker.IsGrounded = !airborne;
            attacker.FacingYaw = 0f;
            var victim = TestHelpers.NpcState(100f, 100.9f);
            victim.PY = airborne ? 50f : TestHelpers.GroundPY(victimDef);
            victim.IsGrounded = !airborne;
            victim.DamagePercent = (ushort)percent;
            sim.RegisterEntity(BonkId, bonk, attacker, baked);
            sim.RegisterEntity(VictimId, victimDef, victim, baked);

            var contacts = new List<SpellResolver.HitResult>();
            bool linkedWhileStunned = false;
            for (int tick = 0; tick < 90 && contacts.Count < 2; tick++)
            {
                ushort stunBefore = sim.GetState(VictimId).HitstunTicks;
                sim.Tick(new Dictionary<ulong, InputState>
                {
                    [BonkId] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.R } : default,
                    [VictimId] = contacts.Count > 0
                        ? new InputState { MoveX = escape.X, MoveY = escape.Z }
                        : default,
                });
                var accepted = sim.LastTickHits.Where(hit =>
                    hit.OwnerEntityId == BonkId && hit.TargetEntityId == VictimId && !hit.Blocked).ToArray();
                if (contacts.Count == 1 && accepted.Length > 0)
                    linkedWhileStunned = stunBefore > 0;
                contacts.AddRange(accepted);
            }

            Assert.True(contacts.Count >= 2,
                $"R must link both authored contacts at {percent}% with escape ({escape.X},{escape.Z}); got {contacts.Count}");
            Assert.True(linkedWhileStunned,
                $"R second hit must connect before escape becomes actionable ({airborne}, {percent}%, {escape})");
            Assert.Equal(12f, contacts.Take(2).Sum(hit => hit.Damage));
            Assert.True(contacts[0].Damage < contacts[1].Damage,
                "R's starter must remain the lower-damage linking contact");
            Assert.True(contacts[1].ImpactForce > contacts[0].ImpactForce,
                "the second R contact must supply the stronger final launch");
            Assert.True(contacts[1].StunTicks > 0, "the R finisher must launch with ordinary hitstun");
            Assert.Equal(contacts[0].ActivationId, contacts[1].ActivationId);
        }
    }

    [Fact]
    public void BonkR_ShieldAndWhiffDoNotFabricateDamageOrConversion()
    {
        var bonk = CompileBonk();
        var baked = LoadBonkPoses();
        var victimDef = CompileBonk();

        var shielded = RunRDefenseCase(bonk, baked, victimDef, 100.9f, shield: true);
        Assert.Equal(0, shielded.FinalDamage);
        Assert.NotEmpty(shielded.BlockedContacts);
        Assert.Empty(shielded.DamagingContacts);

        var missed = RunRDefenseCase(bonk, baked, victimDef, 120f, shield: false);
        Assert.Equal(0, missed.FinalDamage);
        Assert.Empty(missed.BlockedContacts);
        Assert.Empty(missed.DamagingContacts);
    }

    private static (int FinalDamage, SpellResolver.HitResult[] BlockedContacts,
        SpellResolver.HitResult[] DamagingContacts) RunRDefenseCase(
        CharacterDefinition bonk, BakedAnimationData baked, CharacterDefinition victimDef,
        float victimZ, bool shield)
    {
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState(100f, 100f);
        attacker.PY = TestHelpers.GroundPY(bonk);
        sim.RegisterEntity(BonkId, bonk, attacker, baked);
        var victim = TestHelpers.NpcState(100f, victimZ);
        victim.PY = TestHelpers.GroundPY(victimDef);
        sim.RegisterEntity(VictimId, victimDef, victim);

        var blocked = new List<SpellResolver.HitResult>();
        var damaging = new List<SpellResolver.HitResult>();
        for (int tick = 0; tick < 90; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [BonkId] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.R } : default,
                [VictimId] = shield ? new InputState { ShieldHeld = true } : default,
            });
            blocked.AddRange(sim.LastTickHits.Where(hit =>
                hit.OwnerEntityId == BonkId && hit.TargetEntityId == VictimId && hit.Blocked));
            damaging.AddRange(sim.LastTickHits.Where(hit =>
                hit.OwnerEntityId == BonkId && hit.TargetEntityId == VictimId && !hit.Blocked));
        }
        return ((int)sim.GetState(VictimId).DamagePercent, blocked.ToArray(), damaging.ToArray());
    }

    [Fact]
    public void BonkQAndR_ArmorWindowsProtectOnlyTheirAuthoredTicksOnGroundAndAir()
    {
        var bonk = CompileBonk();
        var baked = LoadBonkPoses();
        var windows = new[]
        {
            (Slot: AbilitySlots.A, Airborne: false, LastArmoredTick: 36),
            (Slot: AbilitySlots.A, Airborne: true, LastArmoredTick: 36),
            (Slot: AbilitySlots.R, Airborne: false, LastArmoredTick: 28),
            (Slot: AbilitySlots.R, Airborne: true, LastArmoredTick: 28),
        };

        foreach (var window in windows)
        foreach (int challengeTick in new[] { 11, 12, window.LastArmoredTick, window.LastArmoredTick + 1 })
        {
            var result = RunArmorContact(
                bonk, baked, window.Slot, window.Airborne, challengeTick);
            bool protectedAtContact = challengeTick >= 12 && challengeTick <= window.LastArmoredTick;

            Assert.Equal(3, result.Damage);
            Assert.Equal(3f, result.HitDamage);
            Assert.Equal(protectedAtContact, result.AbilityContinues);
            Assert.Equal(protectedAtContact, result.ArmorActive);
            Assert.Equal(protectedAtContact, result.RemainsAttacking);
            Assert.Equal(protectedAtContact, result.NoLaunch);
        }
    }

    private static (int Damage, float HitDamage, bool AbilityContinues,
        bool RemainsAttacking, bool NoLaunch, bool ArmorActive)
        RunArmorContact(CharacterDefinition bonk, BakedAnimationData baked, byte slot,
            bool airborne, int challengeTick)
    {
        var sim = TestHelpers.MakeSim();
        var target = TestHelpers.PlayerState(100f, 100f);
        target.PY = airborne ? 50f : TestHelpers.GroundPY(bonk);
        target.IsGrounded = !airborne;
        target.AirTimeTicks = airborne ? (ushort)100 : (ushort)0;
        sim.RegisterEntity(BonkId, bonk, target, baked);
        var hitter = TestHelpers.PlayerState(150f, 150f);
        var hitterDef = TestHelpers.EngineDef;
        hitter.PY = TestHelpers.GroundPY(hitterDef);
        sim.RegisterEntity(VictimId, hitterDef, hitter);

        SpellResolver.HitResult? incoming = null;
        // Input activation advances the first authored tick in this same simulation step.
        for (int tick = 1; tick <= challengeTick; tick++)
        {
            if (tick == challengeTick)
            {
                var current = sim.GetState(BonkId);
                sim.Resolver.Spawn(new Hitbox
                {
                    X = current.PX,
                    Y = current.PY,
                    Z = current.PZ,
                    EndX = current.PX,
                    EndY = current.PY,
                    EndZ = current.PZ,
                    Radius = 0.45f,
                    Shape = HitboxShape.Sphere,
                    Damage = 3f,
                    BaseKnockback = 4f,
                    KnockbackGrowth = 4f,
                    KnockbackAngle = 35,
                    StunTicks = 10,
                    DurationTicks = 20,
                    OwnerId = VictimId,
                    ActivationId = 1,
                    AttackSlot = 1,
                });
            }

            sim.Tick(new Dictionary<ulong, InputState>
            {
                [BonkId] = tick == 1 ? new InputState { ActiveSlot = slot } : default,
            });
            var hit = sim.LastTickHits.FirstOrDefault(contact =>
                contact.OwnerEntityId == VictimId && contact.TargetEntityId == BonkId);
            if (hit.OwnerEntityId == VictimId)
            {
                incoming = hit;
                break;
            }
        }

        Assert.True(incoming.HasValue, $"synthetic challenge missed Bonk at tick {challengeTick}");
        bool armorAtContact = sim.GetActiveAbility(BonkId)?.HasArmor ?? false;
        for (int i = 0; i <= incoming.Value.HitstopTicks + 1; i++)
            sim.Tick(new Dictionary<ulong, InputState>());
        var state = sim.GetState(BonkId);
        return (
            (int)state.DamagePercent,
            incoming.Value.Damage,
            sim.GetActiveAbility(BonkId) != null,
            state.State == ActionState.Attacking,
            MathF.Abs(state.KVX) + MathF.Abs(state.KVY) + MathF.Abs(state.KVZ) < 0.0001f,
            armorAtContact);
    }


    private static CharacterDefinition CompileBonk()
    {
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.True(result.CookedPackage != null,
            string.Join("; ", result.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        return CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage!, CharacterClass.Bonk);
    }

    private static BakedAnimationData LoadBonkPoses()
    {
        var baked = BakedAnimationData.LoadFromBin(File.ReadAllBytes(RepoFile("content-cooked/bonk/poses.bin")));
        using var bindings = JsonDocument.Parse(File.ReadAllBytes(RepoFile("content-cooked/bonk/client.bindings")));
        var names = bindings.RootElement.GetProperty("animations").EnumerateArray()
            .ToDictionary(x => x.GetProperty("poseTrackId").GetString()!,
                x => x.GetProperty("semanticId").GetString()!, StringComparer.Ordinal);
        foreach (var animation in baked.Animations)
            animation.Name = names[animation.Name];
        return baked;
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
