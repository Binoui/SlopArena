using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SlopArena.Client.Animation;
using System.Linq;
using SlopArena.Shared;
using System.IO;
using System.Collections.Generic;

namespace SlopArena.Client.Editor
{
    public static class SlopArenaAnimatorGenerator
    {
        private const string ControllerDir = "Assets/Animations/Controllers";

        [MenuItem("Assets/Create SlopArena Animator", priority = 30)]
        public static void CreateFromSelection()
        {
            var selected = Selection.activeObject;
            string assetPath = AssetDatabase.GetAssetPath(selected);
            string dir = Path.GetDirectoryName(assetPath);
            string name = selected.name;

            if (!AssetDatabase.IsValidFolder("Assets/Animations"))
                AssetDatabase.CreateFolder("Assets", "Animations");
            if (!AssetDatabase.IsValidFolder("Assets/Animations/Controllers"))
                AssetDatabase.CreateFolder("Assets/Animations", "Controllers");

            string controllerPath = $"{ControllerDir}/{name}_Animator.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            // ── Build clip map ──
            var clipMap = BuildClipMap(dir);

            // ── Create/update config ──
            var configPaths = AssetDatabase.FindAssets($"t:{nameof(CharacterAnimationConfig)}", new[] { dir });
            var config = configPaths.Length > 0
                ? AssetDatabase.LoadAssetAtPath<CharacterAnimationConfig>(
                    AssetDatabase.GUIDToAssetPath(configPaths[0]))
                : null;
            if (config == null)
            {
                string configPath = $"{dir}/{name}_AnimConfig.asset";
                config = ScriptableObject.CreateInstance<CharacterAnimationConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }
            foreach (var kv in clipMap)
                AssignClip(config, kv.Key, kv.Value);
            EditorUtility.SetDirty(config);

            // ── Parameters ──
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackSlot", AnimatorControllerParameterType.Int);
            controller.AddParameter("ComboStage", AnimatorControllerParameterType.Int);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dash", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hitstun", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Idle", AnimatorControllerParameterType.Trigger);

            // ── State machine (single layer) ──
            var sm = controller.layers[0].stateMachine;

            // BlendTree
            var blendTree = new BlendTree();
            blendTree.name = "MovementBlend";
            blendTree.blendParameter = "Speed";
            blendTree.blendType = BlendTreeType.Simple1D;
            blendTree.AddChild(GetClip(clipMap, "idle") ?? Dummy("idle"), 0f);
            blendTree.AddChild(GetClip(clipMap, "run") ?? Dummy("run"), 1f);
            AssetDatabase.AddObjectToAsset(blendTree, controller);

            var moveState = sm.AddState("Movement", new Vector3(250, 100, 0));
            moveState.motion = blendTree;
            moveState.writeDefaultValues = false;
            sm.defaultState = moveState;

            var jumpState = State(sm, "Jump", new Vector3(250, 250, 0), GetClip(clipMap, "jump"));
            var fallState = State(sm, "Fall", new Vector3(400, 250, 0), GetClip(clipMap, "fall"));
            var landState = State(sm, "Land", new Vector3(550, 250, 0), GetClip(clipMap, "land"));

            Trans(moveState, jumpState, "Jump", AnimatorConditionMode.If, 0.05f);
            Trans(moveState, fallState, "IsGrounded", AnimatorConditionMode.IfNot, 0.1f);
            Trans(jumpState, fallState, "IsGrounded", AnimatorConditionMode.IfNot, 0.1f, exitTime: 0.3f);
            Trans(fallState, landState, "IsGrounded", AnimatorConditionMode.If, 0.05f);
            Trans(landState, moveState, null, AnimatorConditionMode.If, 0.05f, exitTime: 0.9f);

            // ── Combat states ──
            var dashState = State(sm, "Dash", new Vector3(250, 350, 0), GetClip(clipMap, "dash"));
            var hitstunState = State(sm, "Hitstun", new Vector3(250, 450, 0),
                GetClip(clipMap, "hit_small") ?? GetClip(clipMap, "hit_light"));

            AutoExit(dashState, moveState);
            AutoExit(hitstunState, moveState);

            // AnyState → combat triggers
            var toDash = sm.AddAnyStateTransition(dashState);
            toDash.conditions = new[] { new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "Dash" } };
            toDash.duration = 0f;
            toDash.interruptionSource = TransitionInterruptionSource.Source;

            var toHit = sm.AddAnyStateTransition(hitstunState);
            toHit.conditions = new[] { new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "Hitstun" } };
            toHit.duration = 0f;
            toHit.interruptionSource = TransitionInterruptionSource.Source;

            // AnyState → Movement (force-clear via Idle trigger)
            var toMove = sm.AddAnyStateTransition(moveState);
            toMove.conditions = new[] { new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "Idle" } };
            toMove.duration = 0f;
            toMove.interruptionSource = TransitionInterruptionSource.Source;

            // ── Ability states ──
            var charDef = CharacterRegistry.Get(CharacterClass.Manki);
            if (charDef != null)
            {
                var seen = new HashSet<string>();
                int ax = 500, ay = 50;
                const int stepY = 40;

                for (int slot = 0; slot < 6; slot++)
                {
                    foreach (bool airborne in new[] { false, true })
                    {
                        var spec = charDef.GetSlotAbility(slot, airborne);
                        if (spec?.AnimationNames == null) continue;
                        for (int stage = 0; stage < spec.AnimationNames.Length; stage++)
                        {
                            string animName = spec.AnimationNames[stage];
                            if (string.IsNullOrEmpty(animName) || !seen.Add(animName))
                                continue;

                            var clip = GetClip(clipMap, animName);
                            var st = State(sm, animName, new Vector3(ax, ay, 0), clip);
                            AutoExit(st, moveState);

                            var t = sm.AddAnyStateTransition(st);
                            t.conditions = new[]
                            {
                                new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "Attack" },
                                new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = "AttackSlot", threshold = slot },
                                new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = "ComboStage", threshold = stage },
                            };
                            t.duration = 0f;
                            t.interruptionSource = TransitionInterruptionSource.Source;
                            ay += stepY;
                        }
                    }
                }

                if (charDef.ClipOverrides != null)
                {
                    foreach (var ov in charDef.ClipOverrides)
                    {
                        string stateName = ov.Name;
                        if (string.IsNullOrEmpty(stateName) || !seen.Add(stateName))
                            continue;
                        var clip = GetClip(clipMap, ov.Name);
                        var st = State(sm, stateName, new Vector3(ax, ay, 0), clip);
                        AutoExit(st, moveState);
                        ay += stepY;
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var loaded = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (loaded != null)
            {
                EditorGUIUtility.PingObject(loaded);
                Selection.activeObject = loaded;
            }
            Debug.Log($"[AnimGen] Created: {controllerPath} (1 layer, {clipMap.Count} clips)");
        }

        [MenuItem("Assets/Create SlopArena Animator", priority = 30, validate = true)]
        private static bool CreateFromSelectionValidate()
        {
            var selected = Selection.activeObject;
            if (selected == null) return false;
            string path = AssetDatabase.GetAssetPath(selected);
            return AssetDatabase.IsValidFolder(path);
        }

        // ── Helpers ──

        private static Dictionary<string, AnimationClip> BuildClipMap(string rootDir)
        {
            var map = new Dictionary<string, AnimationClip>(System.StringComparer.OrdinalIgnoreCase);
            var searchDirs = new List<string> { rootDir };
            string? parent = Path.GetDirectoryName(rootDir);
            if (parent != null && parent.StartsWith("Assets"))
                searchDirs.Add(parent);

            foreach (string searchDir in searchDirs.Distinct())
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { searchDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var clips = AssetDatabase.LoadAllAssetsAtPath(path)
                        .OfType<AnimationClip>()
                        .Where(c => !c.name.StartsWith("__preview") && !c.name.StartsWith("_"));
                    foreach (var clip in clips)
                    {
                        string n = clip.name.Contains("|")
                            ? clip.name.Split('|').Last() : clip.name;
                        if (n == "mixamo.com" || n == "mixamo")
                            n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                        if (!map.ContainsKey(n))
                            map[n] = clip;
                    }
                }
            }
            return map;
        }

        private static AnimationClip? GetClip(Dictionary<string, AnimationClip> clips, string name)
        {
            clips.TryGetValue(name, out var clip);
            return clip;
        }

        private static AnimatorState State(AnimatorStateMachine sm, string name, Vector3 pos, AnimationClip? clip)
        {
            var s = sm.AddState(name, pos);
            if (clip != null)
                s.motion = clip;
            else
                Debug.LogWarning($"[AnimGen] No clip found for state '{name}' — assign manually");
            s.writeDefaultValues = false;
            return s;
        }

        private static void Trans(AnimatorState from, AnimatorState to, string? param,
            AnimatorConditionMode mode, float duration, float exitTime = -1f)
        {
            var t = from.AddTransition(to);
            if (param != null)
                t.conditions = new[] { new AnimatorCondition { mode = mode, parameter = param } };
            if (exitTime >= 0f)
            {
                t.hasExitTime = true;
                t.exitTime = exitTime;
            }
            t.duration = duration;
        }

        private static void AutoExit(AnimatorState from, AnimatorState to)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 0.85f;
            t.duration = 0.1f;
        }

        private static void AssignClip(CharacterAnimationConfig config, string name, AnimationClip clip)
        {
            switch (name.ToLowerInvariant())
            {
                case "idle": config.Idle = clip; break;
                case "run": config.Run = clip; break;
                case "jump": config.Jump = clip; break;
                case "fall": config.Fall = clip; break;
                case "land": config.Land = clip; break;
                case "attack_1": config.Attack1 = clip; break;
                case "attack_2": config.Attack2 = clip; break;
                case "attack_3": config.Attack3 = clip; break;
                case "dash": config.Dash = clip; break;
                case "hit_small": config.HitSmall = clip; break;
                case "hit_large": config.HitLarge = clip; break;
                case "death": config.Death = clip; break;
                case "spell_q": config.SpellQ = clip; break;
                case "spell_e": config.SpellE = clip; break;
                case "spell_r": config.SpellR = clip; break;
                case "spell_f": config.SpellF = clip; break;
                case "spell_lmb_1": config.Attack1 = clip; break;
                case "spell_lmb_2": config.Attack2 = clip; break;
                case "spell_lmb_3": config.Attack3 = clip; break;
                case "spell_lmb_air": config.Attack3 = clip; break;
                case "hit_light": config.HitSmall = clip; break;
                case "hit_medium": config.HitLarge = clip; break;
                case "dash_loop": config.Dash = clip; break;
                case "spell_q_loop": config.SpellQ = clip; break;
            }
        }

        private static AnimationClip Dummy(string name)
        {
            var clip = new AnimationClip();
            clip.name = name;
            clip.frameRate = 60;
            clip.SetCurve("", typeof(Transform), "localPosition.x",
                new AnimationCurve(new Keyframe(0, 0), new Keyframe(1f / 60f, 0)));
            return clip;
        }
    }
}
