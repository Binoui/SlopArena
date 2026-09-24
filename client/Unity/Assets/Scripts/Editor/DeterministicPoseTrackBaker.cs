using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Entities;

internal static class DeterministicPoseTrackBaker
{
    internal static readonly HumanBodyBones[] RequiredBones =
    {
        HumanBodyBones.Head,
        HumanBodyBones.UpperChest,
        HumanBodyBones.Hips,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightFoot,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightToes,
        HumanBodyBones.LeftToes,
    };

    internal sealed class SampledAnimation
    {
        public string SemanticId = "";
        public string PoseTrackId = "";
        public AnimationClip Clip = null!;
        public int FrameCount;
        public byte[] Bytes = Array.Empty<byte>();
        // Low posture clips are authored in rig-root local space rather than
        // hips-relative space so the baked pelvis translation survives runtime
        // BoneYToWorldY reconstruction.
        public bool IsLowPosture;
    }

    internal static byte[] Bake(
        GameObject rig,
        IReadOnlyList<SampledAnimation> animations,
        int sampleRate,
        WeaponAttachConfig weaponConfig = null,
        float visualScale = 1f,
        float hurtboxBoneScale = 1f,
        float modelYOffset = 0f,
        float capsuleHeight = 0f,
        float hipHeight = 0f)
    {
        if (sampleRate != 60) throw new InvalidOperationException("Sample rate must be exactly 60 Hz.");
        if (rig == null) throw new InvalidOperationException("Rig is missing.");
        if (animations == null || animations.Count == 0) throw new InvalidOperationException("No animations to bake.");

        bool hasLowPosture = animations.Any(x => x != null && x.IsLowPosture);
        if (hasLowPosture)
        {
            if (!IsPositiveFinite(visualScale) || !IsPositiveFinite(hurtboxBoneScale))
                throw new InvalidOperationException("Low-posture bake scales must be finite and positive.");
            if (!IsFinite(modelYOffset) || !IsFinite(capsuleHeight) || !IsFinite(hipHeight))
                throw new InvalidOperationException("Low-posture bake alignment values must be finite.");
        }

        var sourceAnimator = rig.GetComponent<Animator>();
        if (sourceAnimator == null) throw new InvalidOperationException("Rig has no Animator.");
        var temp = UnityEngine.Object.Instantiate(rig);
        temp.name = $"{rig.name}_CharacterCookTemp";
        temp.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var animator = temp.GetComponent<Animator>();
            if (animator == null) throw new InvalidOperationException("Cloned rig has no Animator.");
            var transforms = new Transform[RequiredBones.Length];
            var names = new string[RequiredBones.Length + (weaponConfig == null ? 0 : 2)];
            for (int i = 0; i < RequiredBones.Length; i++)
            {
                transforms[i] = animator.GetBoneTransform(RequiredBones[i]);
                if (transforms[i] == null)
                    throw new InvalidOperationException($"Required humanoid bone is missing: {RequiredBones[i]}.");
                names[i] = transforms[i].name;
            }
            WeaponEntry weaponEntry = null;
            Transform weaponBone = null;
            Vector3 tipLocal = Vector3.zero;
            Vector3 hiltLocal = Vector3.zero;
            if (weaponConfig != null)
            {
                weaponEntry = Array.Find(weaponConfig.Entries ?? Array.Empty<WeaponEntry>(), x => x != null);
                if (weaponEntry == null) throw new InvalidOperationException("Weapon config has no entries.");
                weaponBone = Array.Find(temp.GetComponentsInChildren<Transform>(true),
                    x => x.name == weaponEntry.BoneName);
                if (weaponBone == null) throw new InvalidOperationException($"Weapon config bone is missing: {weaponEntry.BoneName}.");
                if (weaponEntry.Prefab == null) throw new InvalidOperationException($"Weapon prefab is missing for bone {weaponEntry.BoneName}.");
                Transform root = weaponEntry.Prefab.transform;
                var weaponTransforms = weaponEntry.Prefab.GetComponentsInChildren<Transform>(true);
                Transform hiltMarker = weaponTransforms.SingleOrDefault(x => x.name == "bladeHilt");
                Transform tipMarker = weaponTransforms.SingleOrDefault(x => x.name == "bladeEnd");
                if (hiltMarker != null || tipMarker != null)
                {
                    if (hiltMarker == null || tipMarker == null)
                        throw new InvalidOperationException($"Weapon '{root.name}' requires both bladeHilt and bladeEnd markers.");
                    hiltLocal = Vector3.Scale(root.localScale, root.InverseTransformPoint(hiltMarker.position));
                    tipLocal = Vector3.Scale(root.localScale, root.InverseTransformPoint(tipMarker.position));
                }
                else
                {
                    var vertices = new List<Vector3>();
                    foreach (var meshFilter in weaponEntry.Prefab.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh = meshFilter.sharedMesh;
                        if (mesh == null || !mesh.isReadable) continue;
                        Matrix4x4 toPrefab = root.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
                        foreach (var vertex in mesh.vertices)
                            vertices.Add(toPrefab.MultiplyPoint3x4(vertex));
                    }
                    if (vertices.Count > 0)
                    {
                        Vector3 min = vertices[0], max = vertices[0];
                        foreach (var vertex in vertices) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
                        Vector3 extent = max - min;
                        Vector3 axis = extent.x >= extent.y && extent.x >= extent.z ? Vector3.right
                            : extent.y >= extent.z ? Vector3.up : Vector3.forward;
                        float tipProjection = float.MinValue, hiltProjection = float.MaxValue;
                        foreach (var vertex in vertices)
                        {
                            float projection = Vector3.Dot(vertex, axis);
                            if (projection > tipProjection) { tipProjection = projection; tipLocal = vertex; }
                            if (projection < hiltProjection) { hiltProjection = projection; hiltLocal = vertex; }
                        }
                        hiltLocal = Vector3.Scale(root.localScale, hiltLocal);
                        tipLocal = Vector3.Scale(root.localScale, tipLocal);
                    }
                    else
                    {
                        // Readability is optional for presentation prefabs (e.g. non-blade props like bombs).
                        // Keep deterministic fallback points when an imported mesh does not expose CPU vertices.
                        tipLocal = new Vector3(0f, 0f, 1.5f);
                        hiltLocal = Vector3.zero;
                    }
                }
                names[RequiredBones.Length] = "_weapon_tip";
                names[RequiredBones.Length + 1] = "_weapon_hilt";
            }
            var hips = transforms[2];
            using var stream = new MemoryStream();
            WriteUInt32(stream, 0x4C454B53u);
            WriteUInt32(stream, 1u);
            WriteUInt32(stream, (uint)names.Length);
            WriteUInt32(stream, (uint)animations.Count);
            foreach (string name in names) WriteString(stream, name);

            foreach (var animation in animations.OrderBy(x => x.PoseTrackId, StringComparer.Ordinal))
            {
                if (animation.Clip == null || animation.FrameCount <= 0)
                    throw new InvalidOperationException($"Animation '{animation.SemanticId}' has no valid frames.");
                WriteString(stream, animation.PoseTrackId);
                WriteUInt32(stream, (uint)animation.FrameCount);
                for (int frame = 0; frame < animation.FrameCount; frame++)
                {
                    animation.Clip.SampleAnimation(temp, frame / 60f);
                    Vector3 hipsPosition = hips.position;
                    for (int bone = 0; bone < transforms.Length; bone++)
                    {
                        Vector3 position = animation.IsLowPosture
                            ? LowPoseBonePosition(temp.transform.InverseTransformPoint(transforms[bone].position),
                                visualScale, modelYOffset, capsuleHeight, hipHeight, hurtboxBoneScale)
                            : transforms[bone].position - hipsPosition;
                        WriteFiniteVector(stream, position, animation.SemanticId, frame, names[bone]);
                    }
                    if (weaponEntry != null)
                    {
                        Vector3 bladePosition = weaponBone.TransformPoint(weaponEntry.PositionOffset);
                        Quaternion bladeRotation = weaponBone.rotation * Quaternion.Euler(weaponEntry.RotationOffset);
                        Vector3 tip = bladePosition + bladeRotation * tipLocal;
                        Vector3 hilt = bladePosition + bladeRotation * hiltLocal;
                        if (animation.IsLowPosture)
                        {
                            tip = LowPoseBonePosition(temp.transform.InverseTransformPoint(tip),
                                visualScale, modelYOffset, capsuleHeight, hipHeight, hurtboxBoneScale);
                            hilt = LowPoseBonePosition(temp.transform.InverseTransformPoint(hilt),
                                visualScale, modelYOffset, capsuleHeight, hipHeight, hurtboxBoneScale);
                        }
                        else
                        {
                            tip -= hipsPosition;
                            hilt -= hipsPosition;
                        }
                        WriteFiniteVector(stream, tip, animation.SemanticId, frame, "_weapon_tip");
                        WriteFiniteVector(stream, hilt, animation.SemanticId, frame, "_weapon_hilt");
                    }
                }
            }
            return stream.ToArray();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temp);
        }
    }

    private static Vector3 LowPoseBonePosition(
        Vector3 rootLocalPosition,
        float visualScale,
        float modelYOffset,
        float capsuleHeight,
        float hipHeight,
        float hurtboxBoneScale)
        => new(
            rootLocalPosition.x * visualScale / hurtboxBoneScale,
            (rootLocalPosition.y * visualScale + modelYOffset + capsuleHeight * 0.5f - hipHeight) / hurtboxBoneScale,
            rootLocalPosition.z * visualScale / hurtboxBoneScale);

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsPositiveFinite(float value)
        => IsFinite(value) && value > 0f;


    private static void WriteFiniteVector(Stream stream, Vector3 value, string semanticId, int frame, string bone)
    {
        if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
            float.IsNaN(value.y) || float.IsInfinity(value.y) ||
            float.IsNaN(value.z) || float.IsInfinity(value.z))
            throw new InvalidOperationException($"Sampled non-finite pose for '{semanticId}', frame {frame}, bone '{bone}'.");
        WriteSingle(stream, value.x);
        WriteSingle(stream, value.y);
        WriteSingle(stream, value.z);
    }

    private static Transform FindMarker(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(x => x.name == name);
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteSingle(Stream stream, float value)
        => WriteUInt32(stream, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }
}
