using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System;
using System.Text;
using SlopArena.Client.Animation;

/// <summary>
/// Bakes skeleton bone positions per animation frame into a .bin file.
/// Replaces the Godot headless_bake.gd script.
///
/// Usage: Tools -> SlopArena -> Bake Skeleton...
/// Select a character prefab (FBX model with Animator), this will sample all animations
/// at each frame and write bone positions to a .bin file.
///
/// Output format (matches BakedAnimationData.LoadFromBin):
///   SKEL magic (4 bytes)
///   uint version = 1
///   uint boneCount
///   uint animCount
///   [boneNames]: uint nameLen + UTF-8 name
///   [anims]: uint nameLen + UTF-8 name + uint frameCount + float x/y/z per bone per frame
///
/// Positions are Hips-relative (subtract Hips, rotate by inverse Hips rotation)
/// so they stay attached to the entity regardless of animation root motion.
/// </summary>
public class SlopArenaBaker : EditorWindow
{
    private GameObject _model;
    private CharacterAnimationConfig _animConfig;
    private string _outputPath = "data/";
    private float _sampleRate = 60f;

    [MenuItem("Tools/SlopArena/Bake Skeleton...")]
    public static void ShowWindow()
    {
        GetWindow<SlopArenaBaker>("Bake Skeleton");
    }

    private void OnGUI()
    {
        _model = (GameObject)EditorGUILayout.ObjectField("Character Prefab", _model, typeof(GameObject), false);
        _animConfig = (CharacterAnimationConfig)EditorGUILayout.ObjectField("Anim Config", _animConfig, typeof(CharacterAnimationConfig), false);
        _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);
        _sampleRate = EditorGUILayout.FloatField("Sample Rate (fps)", _sampleRate);

        if (GUILayout.Button("Bake") && _model != null && _animConfig != null)
            BakeSkeleton(_model, _animConfig, _sampleRate, _outputPath);
        else if (GUILayout.Button("Bake") && (_model == null || _animConfig == null))
        {
            if (_model == null) Debug.LogError("Select a Character Prefab first");
            if (_animConfig == null) Debug.LogError("Select an Anim Config first");
        }
    }

    private void BakeSkeleton(GameObject model, CharacterAnimationConfig animConfig, float sampleRate = 60f, string outputPath = "data/")
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

        // Build clip list from CharacterAnimationConfig using server logical names
        var ServerAnimNames = new[]
        {
            "idle", "run", "jump", "fall", "dash",
            "small_hit", "medium_hit", "hard_hit", "melee",
            "spell_lmb_1", "spell_lmb_2", "spell_lmb_3",
            "spell_rmb", "spell_air_rmb",
            "spell_q", "spell_q_start", "spell_q_loop", "spell_q_end",
            "spell_e",
            "spell_r_start", "spell_r_loop", "spell_r_attack", "spell_r_end",
            "spell_f",
        };
        var clips = new List<(string name, AnimationClip clip)>();
        foreach (var animName in ServerAnimNames)
        {
            var clip = animConfig.GetClipByName(animName);
            if (clip != null)
                clips.Add((animName, clip));
        }

        if (clips.Count == 0)
        {
            Debug.LogError("No animation clips found in the Anim Config");
            return;
        }
        Debug.Log($"Found {clips.Count} animation clips from config");

        // Bone order MUST match HurtboxBoneDefs index order:
        //   0=Head, 1=Spine2(UpperChest), 2=Hips, 3=RightHand, 4=LeftHand,
        //   5=RightFoot, 6=LeftFoot, 7=RightToes, 8=LeftToes
        var humanBones = new[]
        {
            HumanBodyBones.Head,         // 0
            HumanBodyBones.UpperChest,   // 1 "mixamorig:Spine2" in Mixamo
            HumanBodyBones.Hips,         // 2
            HumanBodyBones.RightHand,    // 3
            HumanBodyBones.LeftHand,     // 4
            HumanBodyBones.RightFoot,    // 5
            HumanBodyBones.LeftFoot,     // 6
            HumanBodyBones.RightToes,    // 7
            HumanBodyBones.LeftToes,     // 8
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
        var tempHips = tempAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (tempHips == null)
        {
            Debug.LogError("Model has no Hips bone cannot compute bone positions");
            DestroyImmediate(tempGO);
            return;
        }

        string outputFile = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "..", "..",
            outputPath.TrimEnd('/'), model.name.ToLowerInvariant() + "_skeleton.bin"));
        string outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        using (var stream = new FileStream(outputFile, FileMode.Create))
        {
            // Header: magic + version
            stream.Write(Encoding.ASCII.GetBytes("SKEL"), 0, 4);
            stream.Write(BitConverter.GetBytes(1u), 0, 4);

            // Bone count + animation count (order MUST match BakedAnimationData.LoadFromBin)
            stream.Write(BitConverter.GetBytes((uint)transforms.Count), 0, 4);
            stream.Write(BitConverter.GetBytes((uint)clips.Count), 0, 4);

            // Bone names (length-prefixed)
            foreach (var name in boneNames)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(name);
                stream.Write(BitConverter.GetBytes((uint)nameBytes.Length), 0, 4);
                stream.Write(nameBytes, 0, nameBytes.Length);
            }

            // Sample each clip
            var tempBoneTransforms = new Transform[transforms.Count];
            for (int b = 0; b < transforms.Count; b++)
            {
                if (b < humanBones.Length)
                    tempBoneTransforms[b] = tempAnimator.GetBoneTransform(humanBones[b]);
                if (tempBoneTransforms[b] == null)
                    tempBoneTransforms[b] = tempGO.transform;
            }

            // Sample each clip
            foreach (var (logicalName, clip) in clips)
            {
                byte[] clipNameBytes = Encoding.UTF8.GetBytes(logicalName);
                stream.Write(BitConverter.GetBytes((uint)clipNameBytes.Length), 0, 4);
                stream.Write(clipNameBytes, 0, clipNameBytes.Length);

                float frameTime = 1f / sampleRate;
                int frameCount = Mathf.CeilToInt(clip.length * sampleRate);
                stream.Write(BitConverter.GetBytes((uint)frameCount), 0, 4);

                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = frame * frameTime;
                    clip.SampleAnimation(tempGO, time);

                    Vector3 hipsPos = tempHips.position;
                    Quaternion invHipsRot = Quaternion.Inverse(tempHips.rotation);

                    for (int b = 0; b < transforms.Count; b++)
                    {
                        Transform tempT = tempBoneTransforms[b];
                        Vector3 worldPos = tempT.position;
                        Vector3 localPos = invHipsRot * (worldPos - hipsPos);
                        stream.Write(BitConverter.GetBytes(localPos.x), 0, 4);
                        stream.Write(BitConverter.GetBytes(localPos.y), 0, 4);
                        stream.Write(BitConverter.GetBytes(localPos.z), 0, 4);
                    }
                }
            }

            DestroyImmediate(tempGO);
        }

        AssetDatabase.Refresh();
        Debug.Log($"Baked {transforms.Count} bones x {clips.Count} clips -> {outputFile} ({new FileInfo(outputFile).Length} bytes)");
    }

    [MenuItem("Tools/SlopArena/Bake All Characters")]
    public static void BakeAllCharacters()
    {
        float sampleRate = 60f;
        string baseDir = "Assets/Art/Characters";
        var configGuids = AssetDatabase.FindAssets("t:CharacterAnimationConfig", new[] { baseDir });
        int baked = 0;
        foreach (var guid in configGuids)
        {
            string configPath = AssetDatabase.GUIDToAssetPath(guid);
            var config = AssetDatabase.LoadAssetAtPath<CharacterAnimationConfig>(configPath);
            if (config == null) continue;

            // Find model FBX: config is at {name}/Animations/Animations_AnimConfig.asset
            // or at {name}/{name}_AnimConfig.asset
            string configDir = Path.GetDirectoryName(configPath);
            string charDir = Path.GetDirectoryName(configDir);
            string charName = Path.GetFileName(charDir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{charDir}/{charName}.fbx");
            if (model == null)
            {
                // Try config dir as char root: config was at {name}/{name}_AnimConfig.asset
                charName = Path.GetFileName(configDir);
                model = AssetDatabase.LoadAssetAtPath<GameObject>($"{configDir}/{charName}.fbx");
            }

            if (model == null)
            {
                Debug.LogWarning($"[BakeAll] No model FBX found for config {configPath}");
                continue;
            }

            Debug.Log($"[BakeAll] Baking {charName}...");
            var baker = CreateInstance<SlopArenaBaker>();
            baker.BakeSkeleton(model, config, sampleRate);
            baked++;
        }
        Debug.Log($"[BakeAll] Done — baked {baked} character(s)");
        AssetDatabase.Refresh();
    }
}
