using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SlopArena.Client.Animation;
using System.Linq;
using System.IO;

namespace SlopArena.Client.Editor
{
    public static class SlopArenaAnimatorGenerator
    {
        private const string ControllerDir = "Assets/Animations/Controllers";

        [MenuItem("Assets/Create SlopArena Animator", priority = 30)]
        private static void CreateFromSelection()
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

            // Parameters
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsWarping", AnimatorControllerParameterType.Bool);
            controller.AddParameter("ComboStage", AnimatorControllerParameterType.Int);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dash", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hitstun", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AirDodge", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Slide", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Idle", AnimatorControllerParameterType.Trigger);

            // Find or create config
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

                // Auto-assign clips from ALL models in the same folder
                // (idle.fbx, run.fbx, jump.fbx, etc. — each adds its clip)
                var modelPaths = AssetDatabase.FindAssets("t:Model", new[] { dir })
                    .Select(g => AssetDatabase.GUIDToAssetPath(g))
                    .Distinct()
                    .ToList();
                foreach (string mp in modelPaths)
                {
                    var modelClips = AssetDatabase.LoadAllAssetsAtPath(mp)
                        .OfType<AnimationClip>()
                        .Where(c => !c.name.StartsWith("__preview") && !c.name.StartsWith("_"))
                        .ToList();
                    foreach (var clip in modelClips)
                    {
                        string clipName = clip.name.Contains("|")
                            ? clip.name.Split('|').Last() : clip.name;
                        // Mixamo names every clip "mixamo.com" — derive from FBX filename
                        if (clipName == "mixamo.com" || clipName == "mixamo")
                            clipName = Path.GetFileNameWithoutExtension(mp).ToLowerInvariant();
                        AssignClip(config, clipName, clip);
                    }
                }
                Debug.Log($"[AnimGen] Created config: {configPath}");
            }

            // Build state machine
            var layer = controller.layers[0];
            var sm = layer.stateMachine;

            // Movement blend tree (Idle ↔ Run via Speed)
            var blendTree = new BlendTree();
            blendTree.blendParameter = "Speed";
            blendTree.blendType = BlendTreeType.Simple1D;
            blendTree.AddChild(config.Idle != null ? config.Idle : Dummy("idle"), 0f);
            blendTree.AddChild(config.Run != null ? config.Run : Dummy("run"), 1f);

            var moveState = sm.AddState("Movement", new Vector3(250, 100, 0));
            moveState.motion = blendTree;
            moveState.writeDefaultValues = false;

            var jumpState = State(sm, "Jump", new Vector3(250, 250, 0), config.Jump);
            var fallState = State(sm, "Fall", new Vector3(400, 250, 0), config.Fall);
            var landState = State(sm, "Land", new Vector3(550, 250, 0), config.Land);
            var attackState = State(sm, "Attack", new Vector3(250, 400, 0), config.Attack1);
            var dashState = State(sm, "Dash", new Vector3(400, 400, 0), config.Dash);
            var hitstunState = State(sm, "Hitstun", new Vector3(550, 400, 0), config.HitSmall);
            var airDodgeState = State(sm, "AirDodge", new Vector3(250, 550, 0), null);
            var slideState = State(sm, "Slide", new Vector3(400, 550, 0), null);

            sm.defaultState = moveState;

            // Any State → interrupt triggers
            AnyTrigger(sm, jumpState, "Jump");
            AnyTrigger(sm, attackState, "Attack");
            AnyTrigger(sm, dashState, "Dash");
            AnyTrigger(sm, hitstunState, "Hitstun");
            AnyTrigger(sm, airDodgeState, "AirDodge");
            AnyTrigger(sm, slideState, "Slide");

            // Movement → airborne
            Trans(moveState, jumpState, "Jump", AnimatorConditionMode.If, 0.05f);
            Trans(moveState, fallState, "IsGrounded", AnimatorConditionMode.IfNot, 0.1f);

            // Jump → Fall → Land → Idle
            Trans(jumpState, fallState, "IsGrounded", AnimatorConditionMode.IfNot, 0.1f, exitTime: 0.3f);
            Trans(fallState, landState, "IsGrounded", AnimatorConditionMode.If, 0.05f);
            Trans(landState, moveState, null, AnimatorConditionMode.If, 0.05f, exitTime: 0.9f);

            // Combat → Idle on exit
            AutoExit(attackState, moveState);
            AutoExit(dashState, moveState);
            AutoExit(hitstunState, moveState);
            AutoExit(airDodgeState, moveState);
            AutoExit(slideState, moveState);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            AssetDatabase.Refresh();

            var loaded = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (loaded != null)
            {
                EditorGUIUtility.PingObject(loaded);
                Selection.activeObject = loaded;
            }
            Debug.Log($"[AnimGen] Created: {controllerPath}");
        }

        [MenuItem("Assets/Create SlopArena Animator", priority = 30, validate = true)]
        private static bool CreateFromSelectionValidate()
        {
            var selected = Selection.activeObject;
            if (selected == null) return false;
            string path = AssetDatabase.GetAssetPath(selected);
            string ext = Path.GetExtension(path).ToLower();
            return ext is ".fbx" or ".glb" or ".gltf" || selected is CharacterAnimationConfig;
        }

        // ── Helpers ──

        private static AnimatorState State(AnimatorStateMachine sm, string name, Vector3 pos, AnimationClip clip)
        {
            var s = sm.AddState(name, pos);
            s.motion = clip ?? Dummy(name.ToLower());
            s.writeDefaultValues = false;
            return s;
        }

        private static void AnyTrigger(AnimatorStateMachine sm, AnimatorState target, string trigger)
        {
            var t = sm.AddAnyStateTransition(target);
            t.conditions = new[] { new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = trigger } };
            t.duration = 0f;
        }

        private static void Trans(AnimatorState from, AnimatorState to, string param,
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
