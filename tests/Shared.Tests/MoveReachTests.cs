using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Issue #151 — deterministic authored hitbox reach chart geometry (<see cref="MoveReach"/>),
/// validated against <see cref="HitboxGeometry.ResolvePositions"/> — the exact function
/// ServerAbility.SpawnHitbox uses. All hitboxes are entity-relative unless noted; the
/// character stands at the grounded origin frame (PY = CapsuleHeight/2, yaw 0 → forward = +Z).
/// </summary>
public class MoveReachTests
{
    private const float Tol = 0.05f; // ±5 cm reach tolerance

    /// <summary>Entity-relative sphere hitbox (Custom knockback profile, default radius 0.3).</summary>
    private static HitboxEvent Sphere(float offZ, float offY = 0f, float radius = 0.3f,
        ushort trigger = 0, ushort duration = 1, string? bone = null)
        => new()
        {
            TriggerTick = trigger, DurationTicks = duration, Shape = HitboxShape.Sphere,
            Radius = radius, OffX = 0f, OffY = offY, OffZ = offZ, BoneName = bone,
            Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 1f, KnockbackGrowth = 1f },
            StunTicks = 10, Interruptible = true,
        };

    private static AbilitySpec MoveSpec(HitboxEvent hit) => new()
    {
        Name = "Synthetic",
        CooldownTicks = 0,
        Stages = new[] { new AttackStage { DurationTicks = 20, HitboxEvents = new[] { hit } } },
        AnimationNames = new[] { "idle" },
    };

    /// <summary>Clone FightGuy and install synthetic abilities at GetSlotAbility slot indices (2 = Slot1, 6 = Slot2).</summary>
    private static CharacterDefinition DefWithSlots(params (int SlotIndex, AbilitySpec Spec)[] slots)
    {
        var def = TestHelpers.CloneDef(TestHelpers.FightGuyDef);
        foreach (var (idx, spec) in slots)
        {
            switch (idx)
            {
                case 2: def.Slot1 = spec; break;
                case 6: def.Slot2 = spec; break;
                default: throw new ArgumentException($"unsupported slot index {idx}");
            }
        }
        return def;
    }

    [Fact]
    public void SampleHit_EntityRelativeSphere_ReachMatchesAuthoredExtent()
    {
        var def = TestHelpers.FightGuyDef;
        var samples = MoveReach.SampleHit(def, Sphere(offZ: 0.5f), slot: 2, airborne: false, null, 0, baked: null);

        Assert.Single(samples);
        float y = def.CapsuleHeight / 2f; // 0.85 = sphere center height
        var ext = MoveReach.ExtentAt(samples, y);
        Assert.NotNull(ext);
        TestHelpers.AssertNear(0.2f, ext.Value.MinZ, Tol); // 0.5 − 0.3
        TestHelpers.AssertNear(0.8f, ext.Value.MaxZ, Tol); // 0.5 + 0.3
    }

    [Fact]
    public void SampleHit_ActiveWindow_ResolvesEveryTick_WithAuthoredTicks()
    {
        var def = TestHelpers.FightGuyDef;
        var samples = MoveReach.SampleHit(def, Sphere(offZ: 0.5f, trigger: 2, duration: 8),
            slot: 2, airborne: false, null, 0, baked: null);

        Assert.Equal(8, samples.Length);
        for (int i = 0; i < samples.Length; i++)
            Assert.Equal((ushort)(2 + i), samples[i].Tick); // authored tick offsets, not 0-based

        float y = def.CapsuleHeight / 2f;
        var ext = MoveReach.ExtentAt(samples, y);
        Assert.NotNull(ext);
        TestHelpers.AssertNear(0.2f, ext.Value.MinZ, Tol);
        TestHelpers.AssertNear(0.8f, ext.Value.MaxZ, Tol);
    }

    [Fact]
    public void SampleHit_CapsuleSweep_SpansEndOff()
    {
        var def = TestHelpers.FightGuyDef;
        // Horizontal capsule from z 0.3 to z 0.3 + 1.2 = 1.5 at sphere-center height.
        var evt = new HitboxEvent
        {
            TriggerTick = 0, DurationTicks = 1, Shape = HitboxShape.Capsule, Radius = 0.25f,
            OffX = 0f, OffY = 0f, OffZ = 0.3f, EndOffX = 0f, EndOffY = 0f, EndOffZ = 1.2f,
            Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 1f, KnockbackGrowth = 1f },
            StunTicks = 10, Interruptible = true,
        };
        var samples = MoveReach.SampleHit(def, evt, slot: 2, airborne: false, null, 0, baked: null);

        float y = def.CapsuleHeight / 2f; // the axis sits exactly at this height
        var ext = MoveReach.ExtentAt(samples, y);
        Assert.NotNull(ext);
        TestHelpers.AssertNear(0.05f, ext.Value.MinZ, Tol); // 0.3 − 0.25
        TestHelpers.AssertNear(1.75f, ext.Value.MaxZ, Tol); // 1.5 + 0.25 (spherical end cap)
    }

    [Fact]
    public void ExtentAt_HeightOutsideVolume_ReturnsNull()
    {
        var def = TestHelpers.FightGuyDef;
        // Sphere center at world y 1.2 (OffY 0.35 above PY 0.85), radius 0.3 → volume 0.9–1.5.
        var samples = MoveReach.SampleHit(def, Sphere(offZ: 0.5f, offY: 0.35f, radius: 0.3f),
            slot: 2, airborne: false, null, 0, baked: null);

        Assert.Null(MoveReach.ExtentAt(samples, 0.85f)); // below the volume
        var ext = MoveReach.ExtentAt(samples, 1.2f);     // center height
        Assert.NotNull(ext);
        TestHelpers.AssertNear(0.2f, ext.Value.MinZ, Tol);
        TestHelpers.AssertNear(0.8f, ext.Value.MaxZ, Tol);
    }

    [Fact]
    public void BandExtent_SphereOnlyInHighBand_LowAndMidNull()
    {
        var def = TestHelpers.FightGuyDef; // CapsuleHeight 1.7
        float h = def.CapsuleHeight;
        // Sphere center at world y 1.55 (OffY 0.7), radius 0.3 → volume 1.25–1.85 ⊆ high band [1.133, 2.2].
        var samples = MoveReach.SampleHit(def, Sphere(offZ: 0.5f, offY: 0.7f, radius: 0.3f),
            slot: 2, airborne: false, null, 0, baked: null);

        Assert.Null(MoveReach.BandExtent(samples, 0f, h / 3f));
        Assert.Null(MoveReach.BandExtent(samples, h / 3f, 2f * h / 3f));
        var high = MoveReach.BandExtent(samples, 2f * h / 3f, h + 0.5f);
        Assert.NotNull(high);
        TestHelpers.AssertNear(0.2f, high.Value.MinZ, Tol);
        TestHelpers.AssertNear(0.8f, high.Value.MaxZ, Tol);
    }

    [Fact]
    public void ReachOrdering_ShorterMoveReachesLess_AtMidHeight()
    {
        var def = DefWithSlots(
            (2, MoveSpec(Sphere(offZ: 0.6f))),  // reach 0.9
            (6, MoveSpec(Sphere(offZ: 1.2f)))); // reach 1.5
        float y = def.CapsuleHeight / 2f;

        var shortSamples = MoveReach.SampleHit(def, def.Slot1!.Stages[0].HitboxEvents[0], slot: 2, airborne: false, null, 0, baked: null);
        var longSamples = MoveReach.SampleHit(def, def.Slot2!.Stages[0].HitboxEvents[0], slot: 6, airborne: false, null, 0, baked: null);

        var shortExt = MoveReach.ExtentAt(shortSamples, y);
        var longExt = MoveReach.ExtentAt(longSamples, y);
        Assert.NotNull(shortExt);
        Assert.NotNull(longExt);
        TestHelpers.AssertNear(0.9f, shortExt.Value.MaxZ, Tol);
        TestHelpers.AssertNear(1.5f, longExt.Value.MaxZ, Tol);
        Assert.True(shortExt.Value.MaxZ < longExt.Value.MaxZ,
            $"shorter move ({shortExt.Value.MaxZ:F2}) must reach less than the longer one ({longExt.Value.MaxZ:F2})");
    }

    [Fact]
    public void ReachOrdering_KistuWeaponCapsule_ExtendsBeyondEntityFallback()
    {
        var def = TestHelpers.KistuDef;
        var baked = TestHelpers.LoadBakedData(def);
        Assert.NotNull(baked); // data/kistu_skeleton.bin is committed — the bake is the geometry source for bone-anchored moves

        // g1 Quick Slash: blade-anchored capsule _weapon_hilt → _weapon_tip, r 0.25, offZ 0.
        var evt = def.Slot1!.Stages[0].HitboxEvents[0];
        float midMin = def.CapsuleHeight / 3f, midMax = 2f * def.CapsuleHeight / 3f;

        var bakedSamples = MoveReach.SampleHit(def, evt, slot: 2, airborne: false, def.Slot1.AnimationNames, 0, baked);
        var bakedMid = MoveReach.BandExtent(bakedSamples, midMin, midMax);
        Assert.NotNull(bakedMid);

        // Entity fallback (no bake): the bone-anchored hitbox collapses to the entity origin + radius.
        var fallbackSamples = MoveReach.SampleHit(def, evt, slot: 2, airborne: false, def.Slot1.AnimationNames, 0, baked: null);
        var fallbackMid = MoveReach.BandExtent(fallbackSamples, midMin, midMax);
        Assert.NotNull(fallbackMid);
        TestHelpers.AssertNear(0.25f, fallbackMid.Value.MaxZ, Tol);
        Assert.True(bakedMid.Value.MaxZ > fallbackMid.Value.MaxZ,
            $"baked reach ({bakedMid.Value.MaxZ:F2}) must exceed the fallback ({fallbackMid.Value.MaxZ:F2})");
    }
}
