#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Defines a hurtbox attached to a bone chain (capsule between start→end bone).
/// </summary>
public struct BoneHurtboxDef
{
    public string[] BoneNames;
    public string[]? EndBoneNames;
    public float Radius;

    public BoneHurtboxDef(string[] boneNames, string[]? endBoneNames, float radius)
    {
        BoneNames = boneNames;
        EndBoneNames = endBoneNames;
        Radius = radius;
    }
}

/// <summary>
/// Bone-attached capsule hurtboxes. Reads skeleton bone poses directly each frame.
/// </summary>
public partial class BoneHurtboxSetup : Node
{
    private Skeleton3D? _skeleton;
    private readonly List<(int startBone, int endBone, float radius)> _defs = new();

    public static BoneHurtboxDef[] DefaultHumanoid()
    {
        return new BoneHurtboxDef[]
        {
            // Head
            new(new[]{"mixamorig_Head", "Head"}, null, 0.25f),
            // Torso: Spine2 → Hips
            new(new[]{"mixamorig_Spine2", "Spine2"}, new[]{"mixamorig_Hips", "Hips"}, 0.30f),
            // Left arm: Shoulder → Hand (full arm length)
            new(new[]{"mixamorig_LeftShoulder", "LeftShoulder"}, new[]{"mixamorig_LeftHand", "LeftHand"}, 0.14f),
            // Right arm
            new(new[]{"mixamorig_RightShoulder", "RightShoulder"}, new[]{"mixamorig_RightHand", "RightHand"}, 0.14f),
            // Left leg: UpLeg → Foot (full leg length)
            new(new[]{"mixamorig_LeftUpLeg", "LeftUpLeg"}, new[]{"mixamorig_LeftFoot", "LeftFoot"}, 0.18f),
            // Right leg
            new(new[]{"mixamorig_RightUpLeg", "RightUpLeg"}, new[]{"mixamorig_RightFoot", "RightFoot"}, 0.18f),
        };
    }

    public void Build(Skeleton3D skeleton, BoneHurtboxDef[] defs)
    {
        _skeleton = skeleton;
        _defs.Clear();

        foreach (var def in defs)
        {
            int startIdx = FindBone(skeleton, def.BoneNames);
            if (startIdx < 0)
            {
                GD.Print($"[Hurtbox] No start bone for: {string.Join(", ", def.BoneNames)}");
                continue;
            }

            int endIdx = startIdx; // Default: same bone = sphere-like (degenerate capsule)
            if (def.EndBoneNames != null)
            {
                int found = FindBone(skeleton, def.EndBoneNames);
                if (found >= 0) endIdx = found;
            }

            _defs.Add((startIdx, endIdx, def.Radius));
            string startName = skeleton.GetBoneName(startIdx);
            string endName = skeleton.GetBoneName(endIdx);
            GD.Print($"[Hurtbox] Capsule {startName} → {endName} R={def.Radius}");
        }
    }

    /// <summary>Get world-space capsules (start, end, radius) for hit detection.</summary>
    public List<(Vector3 start, Vector3 end, float radius)> GetWorldCapsules()
    {
        var capsules = new List<(Vector3, Vector3, float)>();
        if (_skeleton == null || !_skeleton.IsInsideTree()) return capsules;

        foreach (var (startIdx, endIdx, radius) in _defs)
        {
            Vector3 start = BoneWorldPos(_skeleton, startIdx);
            Vector3 end = BoneWorldPos(_skeleton, endIdx);
            capsules.Add((start, end, radius));
        }
        return capsules;
    }

    public int Count => _defs.Count;

    private static Vector3 BoneWorldPos(Skeleton3D skel, int boneIdx)
    {
        return skel.GlobalTransform * skel.GetBoneGlobalPose(boneIdx).Origin;
    }

    private static int FindBone(Skeleton3D skeleton, string[] patterns)
    {
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            string name = skeleton.GetBoneName(i);
            foreach (var pattern in patterns)
            {
                if (name == pattern || name.EndsWith("_" + pattern) || name.EndsWith(":" + pattern))
                    return i;
            }
        }
        return -1;
    }
}
