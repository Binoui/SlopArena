using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Bakes skeleton bone positions per animation frame into a .bin file.
/// Replaces the Godot headless_bake.gd script.
/// 
/// Usage: Tools → SlopArena → Bake Skeleton...
/// Select a GLB model with Mixamo rig, this will sample all animations
/// at each frame and write bone world positions to a .bin file.
/// 
/// Output format matches what ServerSimulation.BuildEntitiesFromState() expects.
/// </summary>
public class SlopArenaBaker : EditorWindow
{
    private GameObject _model;
    private string _outputPath = "Assets/Data/";
    private float _sampleRate = 60f;

    [MenuItem("Tools/SlopArena/Bake Skeleton...")]
    public static void ShowWindow()
    {
        GetWindow<SlopArenaBaker>("Bake Skeleton");
    }

    private void OnGUI()
    {
        _model = (GameObject)EditorGUILayout.ObjectField("Model (GLB)", _model, typeof(GameObject), false);
        _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);
        _sampleRate = EditorGUILayout.FloatField("Sample Rate (fps)", _sampleRate);

        if (GUILayout.Button("Bake") && _model != null)
        {
            BakeSkeleton(_model);
        }
    }

    private void BakeSkeleton(GameObject model)
    {
        var animator = model.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("Model has no Animator component");
            return;
        }

        var avatar = animator.avatar;
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError("Model needs a valid Humanoid Avatar");
            return;
        }

        // Get all animation clips
        var clips = new List<AnimationClip>();
        foreach (var clip in AnimationUtility.GetAnimationClips(model))
        {
            if (clip != null && clip.isLooping)
                clips.Add(clip);
        }

        if (clips.Count == 0)
        {
            Debug.LogError("No animation clips found on the model");
            return;
        }

        Debug.Log($"Found {clips.Count} animation clips");

        // Human bone mapping (mixamorig bones)
        var humanBones = new[]
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine,
            HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
        };

        var boneNames = new List<string>();
        var transforms = new List<Transform>();
        foreach (var bone in humanBones)
        {
            var t = animator.GetBoneTransform(bone);
            if (t != null)
            {
                boneNames.Add(t.name);
                transforms.Add(t);
            }
        }

        Debug.Log($"Found {transforms.Count} bones from humanoid avatar");

        // Create a copy to sample without affecting the original
        var tempGO = Instantiate(model);
        var tempAnimator = tempGO.GetComponent<Animator>();
        var tempTransform = tempGO.transform;

        using (var writer = new BinaryWriter(File.Open(_outputPath + model.name + "_skeleton.bin", FileMode.Create)))
        {
            // Write header: bone count
            writer.Write((ushort)transforms.Count);

            // Write bone names
            foreach (var name in boneNames)
                writer.Write(name);

            // Sample each clip
            writer.Write((ushort)clips.Count);
            foreach (var clip in clips)
            {
                float frameTime = 1f / _sampleRate;
                float duration = clip.length;
                int frameCount = Mathf.CeilToInt(duration * _sampleRate);

                writer.Write(clip.name);
                writer.Write((ushort)frameCount);

                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = frame * frameTime;
                    clip.SampleAnimation(tempGO, time);

                    // Write each bone's world position + rotation
                    foreach (var t in transforms)
                    {
                        Vector3 pos = t.position;
                        Quaternion rot = t.rotation;
                        writer.Write(pos.x);
                        writer.Write(pos.y);
                        writer.Write(pos.z);
                        writer.Write(rot.x);
                        writer.Write(rot.y);
                        writer.Write(rot.z);
                        writer.Write(rot.w);
                    }
                }
            }

            DestroyImmediate(tempGO);
        }

        AssetDatabase.Refresh();
        Debug.Log($"Baked {transforms.Count} bones × {clips.Count} clips → {_outputPath}{model.name}_skeleton.bin");
    }
}
