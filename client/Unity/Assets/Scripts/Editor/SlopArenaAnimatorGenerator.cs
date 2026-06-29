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
            string dir = AssetDatabase.IsValidFolder(assetPath) ? assetPath : System.IO.Path.GetDirectoryName(assetPath);
            string name = System.IO.Path.GetFileName(dir);

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

            var charClass = name.ToLowerInvariant() switch
            {
                "manki" => CharacterClass.Manki,
                "bunny" => CharacterClass.Bunny,
                _ => CharacterClass.Manki,
            };
            var charDef = CharacterRegistry.Get(charClass);

            // ── Build graph from character definition ──
            var graph = AnimatorGraphBuilder.Build(charDef);

            // ── Create Unity states from graph (pass 1) ──
            foreach (var gs in graph.States)
            {
                var clip = gs.IsBlendTree ? null : GetClip(clipMap, gs.MotionName);
                var st = sm.AddState(gs.Name, new Vector3(gs.PositionX, gs.PositionY, 0));
                if (gs.IsBlendTree)
                {
                    var bt = new BlendTree();
                    bt.name = "MovementBlend";
                    bt.blendParameter = "Speed";
                    bt.blendType = BlendTreeType.Simple1D;
                    bt.AddChild(GetClip(clipMap, "idle") ?? Dummy("idle"), 0f);
                    bt.AddChild(GetClip(clipMap, "run") ?? Dummy("run"), 1f);
                    AssetDatabase.AddObjectToAsset(bt, controller);
                    st.motion = bt;
                }
                else if (clip != null)
                {
                    st.motion = clip;
                }
                st.writeDefaultValues = false;
                if (gs.IsDefault)
                    sm.defaultState = st;
            }

            // Save & reload — bakes internal graph nodes so ALL transitions serialize correctly
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            sm = controller.layers[0].stateMachine;

            // ── Pass 2: All transitions after reload ──
            // (Creating transitions before save causes Edge.WakeUp NRE on reload)

            // ── Direct transitions ──
            foreach (var dt in graph.DirectTransitions)
            {
                var from = FindState(sm, dt.FromState);
                var to = FindState(sm, dt.ToState);
                if (from == null || to == null) continue;
                var t = from.AddTransition(to);
                if (dt.Conditions.Length > 0)
                {
                    t.conditions = dt.Conditions.Select(c => new AnimatorCondition
                    {
                        mode = (AnimatorConditionMode)(int)c.Mode,
                        parameter = c.Parameter,
                        threshold = c.Mode == AnimConditionMode.If || c.Mode == AnimConditionMode.IfNot ? 0f : c.Threshold,
                    }).ToArray();
                }
                else
                {
                    t.conditions = System.Array.Empty<AnimatorCondition>();
                }
                t.duration = dt.Duration;
                t.hasExitTime = dt.HasExitTime;
                if (dt.HasExitTime) t.exitTime = dt.ExitTime;
            }

            // ── Auto-exit to Movement for states that need it ──
            var moveState = FindState(sm, "Movement");
            if (moveState != null)
            {
                foreach (var gs in graph.States.Where(s => s.AutoExit))
                {
                    var from = FindState(sm, gs.Name);
                    if (from != null && from != moveState)
                    {
                        var t = from.AddTransition(moveState);
                        t.hasExitTime = true;
                        t.exitTime = 0.85f;
                        t.duration = 0.1f;
                        t.conditions = System.Array.Empty<AnimatorCondition>();
                    }
                }
            }

            // ── AnyState transitions ──
            foreach (var at in graph.AnyTransitions)
            {
                var to = FindState(sm, at.ToState);
                if (to == null) continue;
                var t = sm.AddAnyStateTransition(to);
                t.conditions = at.Conditions.Select(c => new AnimatorCondition
                {
                    mode = (AnimatorConditionMode)(int)c.Mode,
                    parameter = c.Parameter,
                    threshold = c.Mode == AnimConditionMode.If || c.Mode == AnimConditionMode.IfNot ? 0f : c.Threshold,
                }).ToArray();
                t.duration = at.Duration;
                if (at.InterruptionSource)
                    t.interruptionSource = TransitionInterruptionSource.Source;
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

            // Static assert: AnimConditionMode values match Unity's AnimatorConditionMode
            Debug.Assert((int)AnimConditionMode.If == (int)AnimatorConditionMode.If,
                "AnimConditionMode.If mismatch with Unity's AnimatorConditionMode");
            Debug.Assert((int)AnimConditionMode.IfNot == (int)AnimatorConditionMode.IfNot,
                "AnimConditionMode.IfNot mismatch with Unity's AnimatorConditionMode");
            Debug.Assert((int)AnimConditionMode.Equals == (int)AnimatorConditionMode.Equals,
                "AnimConditionMode.Equals mismatch with Unity's AnimatorConditionMode");

            return AssetDatabase.IsValidFolder(path);
        }

        // ── Helpers ──

        private static Dictionary<string, AnimationClip> BuildClipMap(string rootDir)
        {
            var map = new Dictionary<string, AnimationClip>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { rootDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clips = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview") && !c.name.StartsWith("_"));

                foreach (var clip in clips)
                {
                    string key = clip.name;
                    if (!map.ContainsKey(key))
                        map[key] = clip;
                }
            }
            return map;
        }

        private static AnimationClip? GetClip(Dictionary<string, AnimationClip> clips, string name)
        {
            clips.TryGetValue(name, out var clip);
            return clip;
        }


        /// <summary>
        /// Find an AnimatorState by name in the state machine.
        /// </summary>
        private static AnimatorState? FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var child in sm.states)
                if (child.state.name == name)
                    return child.state;
            return null;
        }

        private static void AssignClip(CharacterAnimationConfig config, string name, AnimationClip clip)
        {
            switch (name.ToLowerInvariant())
            {
                case "idle": config.Idle = clip; break;
                case "run": config.Run = clip; break;
                case "jump": config.Jump = clip; break;
                case "fall": config.Fall = clip; break;
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
