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
    }

    internal static byte[] Bake(GameObject rig, IReadOnlyList<SampledAnimation> animations, int sampleRate, WeaponAttachConfig weaponConfig = null)
    {
        if (sampleRate != 60) throw new InvalidOperationException("Sample rate must be exactly 60 Hz.");
        if (rig == null) throw new InvalidOperationException("Rig is missing.");
        if (animations == null || animations.Count == 0) throw new InvalidOperationException("No animations to bake.");

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
            Vector3 tipLocal = new Vector3(0f, 0f, 1.5f);
            Vector3 hiltLocal = Vector3.zero;
            if (weaponConfig != null)
            {
                weaponEntry = Array.Find(weaponConfig.Entries ?? Array.Empty<WeaponEntry>(), x => x != null && x.BoneName == "mixamorig:RightHand");
                if (weaponEntry == null) throw new InvalidOperationException("Weapon config has no mixamorig:RightHand entry.");
                if (weaponEntry.Prefab == null) throw new InvalidOperationException("Weapon config right-hand prefab is missing.");
                var vertices = new List<Vector3>();
                foreach (var meshFilter in weaponEntry.Prefab.GetComponentsInChildren<MeshFilter>())
                {
                    if (meshFilter.sharedMesh == null) continue;
                    Matrix4x4 toPrefab = weaponEntry.Prefab.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
                    foreach (var vertex in meshFilter.sharedMesh.vertices)
                        vertices.Add(toPrefab.MultiplyPoint3x4(vertex));
                }
                if (vertices.Count == 0) throw new InvalidOperationException("Weapon prefab has no mesh vertices.");
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
                names[RequiredBones.Length] = "_weapon_tip";
                names[RequiredBones.Length + 1] = "_weapon_hilt";
            }
            var hips = transforms[2];
            var rightHand = transforms[3];
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
                        Vector3 position = transforms[bone].position - hipsPosition;
                        WriteFiniteVector(stream, position, animation.SemanticId, frame, names[bone]);
                    }
                    if (weaponEntry != null)
                    {
                        Vector3 bladePosition = rightHand.TransformPoint(weaponEntry.PositionOffset);
                        Quaternion bladeRotation = rightHand.rotation * Quaternion.Euler(weaponEntry.RotationOffset);
                        WriteFiniteVector(stream, bladePosition + bladeRotation * tipLocal - hipsPosition, animation.SemanticId, frame, "_weapon_tip");
                        WriteFiniteVector(stream, bladePosition + bladeRotation * hiltLocal - hipsPosition, animation.SemanticId, frame, "_weapon_hilt");
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
