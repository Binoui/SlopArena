using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for the shared hitbox position resolver (spec #119): the same pure function
/// the server (ServerAbility.SpawnHitbox) and the Ability Lab preview both call.
/// Covers the entity-relative path, the bone-attached path, capsule ends, facing
/// rotation, and the tick→baked-frame projection.
/// </summary>
public class HitboxGeometryTests
{
    private static byte[] BuildTestBin(string[] boneNames, (string name, int frameCount)[] anims, Func<int, int, int, float> bonePos)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("SKEL"));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes((uint)boneNames.Length));
        bytes.AddRange(BitConverter.GetBytes((uint)anims.Length));
        foreach (var name in boneNames)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            bytes.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            bytes.AddRange(nameBytes);
        }
        foreach (var (animName, frameCount) in anims)
        {
            byte[] animNameBytes = Encoding.UTF8.GetBytes(animName);
            bytes.AddRange(BitConverter.GetBytes((uint)animNameBytes.Length));
            bytes.AddRange(animNameBytes);
            bytes.AddRange(BitConverter.GetBytes((uint)frameCount));
            for (int f = 0; f < frameCount; f++)
                for (int bone = 0; bone < boneNames.Length; bone++)
                    for (int axis = 0; axis < 3; axis++)
                        bytes.AddRange(BitConverter.GetBytes(bonePos(f, bone, axis)));
        }
        return bytes.ToArray();
    }

    /// <summary>Minimal def: 2 bones, scale 1.0, 60-tick LMB stage named "attack".</summary>
    private static CharacterDefinition BoneDef()
    {
        return new CharacterDefinition
        {
            CapsuleHeight = 1.5f,
            HipHeight = 0.5f,
            HurtboxBoneScale = 1.0f,
            HurtboxBoneDefs = new[]
            {
                new HurtboxBoneDef("mixamorig:Head", 0, 0, 0, 0.22f),
                new HurtboxBoneDef("mixamorig:Hips", 0, 0, 0, 0.26f),
            },
            LMB = new AbilitySpec
            {
                Stages = new[] { new AttackStage { DurationTicks = 60 } },
                AnimationNames = new[] { "attack" },
            },
        };
    }

    [Fact]
    public void EntityRelative_FacingZero_OffZIsFront()
    {
        var s = new CharacterState { PX = 1f, PY = 2f, PZ = 3f, FacingYaw = 0f };
        var evt = new HitboxEvent { OffX = 0f, OffY = 0.5f, OffZ = 1.5f };

        HitboxGeometry.ResolvePositions(s, evt, null, null, null, 0, 0,
            out float wx, out float wy, out float wz, out _, out _, out _);

        // Facing +Z (yaw 0): OffZ=front → wz, OffY=up → wy, OffX=0.
        Assert.Equal(1f, wx, 5);
        Assert.Equal(2.5f, wy, 5);
        Assert.Equal(4.5f, wz, 5);
    }

    [Fact]
    public void EntityRelative_FacingPiOver2_FrontRotatesToPlusX()
    {
        var s = new CharacterState { PX = 1f, PY = 2f, PZ = 3f, FacingYaw = MathF.PI / 2f };
        var evt = new HitboxEvent { OffZ = 1.5f };

        HitboxGeometry.ResolvePositions(s, evt, null, null, null, 0, 0,
            out float wx, out float wy, out float wz, out _, out _, out _);

        // Facing +X: local +Z (front) maps to world +X.
        Assert.Equal(2.5f, wx, 5);
        Assert.Equal(2f, wy, 5);
        Assert.Equal(3f, wz, 5);
    }

    [Fact]
    public void CapsuleEnd_ExtendsAlongFacing()
    {
        var s = new CharacterState { PX = 0f, PY = 1f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { OffZ = 0.5f, EndOffZ = 1.2f, EndOffY = 0.1f };

        HitboxGeometry.ResolvePositions(s, evt, null, null, null, 0, 0,
            out float wx, out float wy, out float wz,
            out float wex, out float wey, out float wez);

        // Start at (0, 1, 0.5); EndOffZ extends along facing (+Z).
        Assert.Equal(0f, wx, 5);
        Assert.Equal(1f, wy, 5);
        Assert.Equal(0.5f, wz, 5);
        Assert.Equal(0f, wex, 5);
        Assert.Equal(1.1f, wey, 5);
        Assert.Equal(1.7f, wez, 5);
    }

    [Fact]
    public void BoneAttached_PositionsAtBoneWorldPosition()
    {
        // 60-frame "attack": frame f puts Head at Y = 0.9 + f*0.01, X/Z = 0.
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips" },
            new[] { ("attack", 60) },
            (f, bone, axis) => (bone, axis) switch { (0, 1) => 0.9f + f * 0.01f, (1, 1) => 0.4f, _ => 0f }));
        var def = BoneDef();
        var s = new CharacterState { PX = 1f, PY = 0.75f, PZ = 2f, FacingYaw = 0f, AttackElapsedTicks = 30 };
        var evt = new HitboxEvent { BoneName = "mixamorig:Head" };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, new[] { "attack" }, 0, 0,
            out float wx, out float wy, out float wz, out _, out _, out _);

        // bakedFrame = min(30 * 60 / 60, 59) = 30 → Head Y = 0.9 + 0.3 = 1.2
        // world Y = py - h/2 + HipHeight + by = 0.75 - 0.75 + 0.5 + 1.2 = 1.7
        Assert.Equal(1f, wx, 5);
        Assert.Equal(1.7f, wy, 5);
        Assert.Equal(2f, wz, 5);
    }

    [Fact]
    public void BoneAttached_AppliesBoneOffsetRotated()
    {
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips" },
            new[] { ("attack", 1) },
            (f, bone, axis) => 0f));
        var def = BoneDef();
        var s = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = MathF.PI / 2f };
        var evt = new HitboxEvent { BoneName = "mixamorig:Hips", BoneOffZ = 0.2f, BoneOffY = 0.1f };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, new[] { "attack" }, 0, 0,
            out float wx, out float wy, out float wz, out _, out _, out _);

        // Hips at (0,0,0) baked → world (0, 0.5, 0); BoneOffZ=0.2 at yaw PI/2 → +X.
        Assert.Equal(0.2f, wx, 5);
        Assert.Equal(0.6f, wy, 5);
        Assert.Equal(0f, wz, 5);
    }

    [Fact]
    public void BoneNameInBakeButNotDefs_AttachesAtBone()
    {
        // The bake carries the full mixamorig skeleton (here incl. LeftToes), while the
        // defs only cover the hurtbox subset — a hitbox must still attach to any baked
        // bone (Ability Lab bone dropdown lists the full bake).
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips", "mixamorig:LeftToes" },
            new[] { ("attack", 1) },
            (f, bone, axis) => 0f));
        var def = BoneDef(); // defs: Head, Hips only
        var s = new CharacterState { PX = 0f, PY = 0.75f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:LeftToes" };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, new[] { "attack" }, 0, 0,
            out float wx, out float wy, out float wz, out _, out _, out _);

        // LeftToes at (0,0,0) baked → world (0, py - h/2 + HipHeight + 0, 0) = (0, 0.5, 0).
        Assert.Equal(0f, wx, 5);
        Assert.Equal(0.5f, wy, 5);
        Assert.Equal(0f, wz, 5);
    }

    [Fact]
    public void BoneNameWithoutBakedData_FallsBackToEntityOffset()
    {
        var def = BoneDef();
        var s = new CharacterState { PX = 1f, PY = 2f, PZ = 3f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:Head", OffZ = 1.5f };

        HitboxGeometry.ResolvePositions(s, evt, null, def, new[] { "attack" }, 0, 0,
            out float wx, out float wy, out float wz, out _, out _, out _);

        Assert.Equal(1f, wx, 5); // entity-relative: OffZ lands on wz at yaw 0
        Assert.Equal(2f, wy, 5);
        Assert.Equal(4.5f, wz, 5);
    }

    [Fact]
    public void BoneNameNotInDefs_FallsBackToEntityOffset()
    {
        var baked = BakedAnimationData.LoadFromBin(BuildTestBin(
            new[] { "mixamorig:Head", "mixamorig:Hips" },
            new[] { ("attack", 1) },
            (f, bone, axis) => 0f));
        var def = BoneDef();
        var s = new CharacterState { PX = 0f, PY = 2f, PZ = 0f, FacingYaw = 0f };
        var evt = new HitboxEvent { BoneName = "mixamorig:LeftFoot", OffZ = 0.5f };

        HitboxGeometry.ResolvePositions(s, evt, baked, def, new[] { "attack" }, 0, 0,
            out float wx, out float wy, out float wz, out _, out _, out _);

        Assert.Equal(0f, wx, 5);
        Assert.Equal(2f, wy, 5);
        Assert.Equal(0.5f, wz, 5);
    }
}
