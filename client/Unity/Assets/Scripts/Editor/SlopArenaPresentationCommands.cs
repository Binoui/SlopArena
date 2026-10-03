using System;
using System.Collections.Generic;
using System.Globalization;
using Animancer;
using Newtonsoft.Json;
using SlopArena.Client.Entities;
using SlopArena.Client.Tools;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools
{
    public static class SlopArenaPresentationCommands
    {
        [CliCommand(
            "sloparena.presentation.inspect",
            "Read the current Lab renderer or an explicitly identified runtime entity presentation pose, animation, bones and attachments.",
            MainThreadRequired = true,
            Tags = new[] { "presentation/inspection" })]
        public static SlopArenaPresentationInspectionResult Inspect(
            [CliArg("target", "Renderer scope: lab (default) or runtime.")] string target = "lab",
            [CliArg("entity", "Exact unsigned decimal runtime EntityId; required for runtime target.")] string entity = null,
            [CliArg("bones", "Comma-separated presentation bone names or bone.* aliases to inspect.")] string bones = null)
        {
            var requestedBones = new List<string>();
            var seenBones = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(bones))
                foreach (string rawName in bones.Split(','))
                {
                    string name = rawName.Trim();
                    if (name.Length > 0 && seenBones.Add(name)) requestedBones.Add(name);
                }
            string[] names = requestedBones.ToArray();
            if (string.Equals(target, "lab", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(entity))
                    return Failure("entity is only valid for target=runtime.");
                AbilityLabWindow window = AbilityLabWindow.FindExistingForCommand();
                AbilityLab lab = window != null ? window.CommandLab : AbilityLab.Instance;
                if (window == null && lab == null)
                {
                    AbilityLab[] labs = Resources.FindObjectsOfTypeAll<AbilityLab>();
                    int liveLabs = 0;
                    foreach (AbilityLab candidate in labs)
                        if (candidate != null && !EditorUtility.IsPersistent(candidate))
                        {
                            liveLabs++;
                            if (liveLabs == 1) lab = candidate;
                        }
                    if (liveLabs != 1) lab = null;
                }
                if (lab == null)
                    return Failure("No existing Ability Lab renderer is selected or loaded.");
                return ReadLab(lab, names);
            }

            if (!string.Equals(target, "runtime", StringComparison.OrdinalIgnoreCase))
                return Failure("target must be 'lab' or 'runtime'.");
            if (!EditorApplication.isPlaying)
                return Failure("Runtime target is available only while the Editor is in Play Mode.");
            if (!ulong.TryParse(entity, NumberStyles.None, CultureInfo.InvariantCulture, out ulong entityId))
                return Failure("runtime target requires entity as an unsigned decimal EntityId string.");
            if (entityId == 0)
                return Failure("runtime EntityId must be non-zero.");

            PlayerRenderer[] candidates = Resources.FindObjectsOfTypeAll<PlayerRenderer>();
            PlayerRenderer runtimeMatch = null;
            int runtimeCount = 0;
            foreach (PlayerRenderer renderer in candidates)
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy
                    && IsLoaded(renderer.gameObject.scene) && !EditorUtility.IsPersistent(renderer)
                    && renderer.GetComponentInParent<AbilityLab>() == null && renderer.EntityId == entityId)
                {
                    runtimeMatch = renderer;
                    runtimeCount++;
                }
            if (runtimeCount == 0)
                return Failure($"No active runtime PlayerRenderer with EntityId {entityId.ToString(CultureInfo.InvariantCulture)} exists in a loaded scene.");
            if (runtimeCount > 1)
                return Failure($"EntityId {entityId.ToString(CultureInfo.InvariantCulture)} matches multiple active runtime renderers; target is ambiguous.");
            return new SlopArenaPresentationInspectionResult
            {
                Success = true,
                Target = "runtime",
                Actor = ReadActor(runtimeMatch, names)
            };
        }
        internal static SlopArenaPresentationInspectionResult ReadLab(AbilityLab lab, string[] bones)
        {
            // The window owns a HideAndDontSave rig, which need not belong to a loaded scene.
            if (lab == null || EditorUtility.IsPersistent(lab))
                return Failure("The selected Ability Lab is missing or is a persistent asset.");
            PlayerRenderer[] renderers = lab.GetComponentsInChildren<PlayerRenderer>(true);
            PlayerRenderer match = null;
            int count = 0;
            foreach (PlayerRenderer renderer in renderers)
                if (renderer != null && renderer.gameObject.name == "LabCharacter")
                {
                    match = renderer;
                    count++;
                }
            if (count != 1)
                return Failure(count == 0
                    ? "The selected Ability Lab has no existing LabCharacter renderer; inspection will not create one."
                    : "The selected Ability Lab has multiple LabCharacter renderers; target is ambiguous.");
            return new SlopArenaPresentationInspectionResult
            {
                Success = true,
                Target = "lab",
                Actor = ReadActor(match, bones)
            };
        }


        internal static SlopArenaPresentationActorInfo ReadActor(PlayerRenderer actor, string[] bones)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            var diagnostics = new List<string>();
            Transform root = actor.transform;
            Animator animator = actor.Animator;
            AnimancerComponent animancer = null;
            if (animator != null)
            {
                AnimancerComponent[] candidates = actor.GetComponentsInChildren<AnimancerComponent>(true);
                foreach (AnimancerComponent candidate in candidates)
                    if (candidate != null && candidate.Animator == animator)
                    {
                        if (animancer != null)
                        {
                            animancer = null;
                            diagnostics.Add("animation unavailable: multiple actor-scoped Animancer components reference the renderer Animator.");
                            break;
                        }
                        animancer = candidate;
                    }
                if (animancer == null && diagnostics.Count == 0)
                    diagnostics.Add("animation unavailable: no actor-scoped Animancer component is associated with the renderer Animator.");
            }
            else diagnostics.Add("animation unavailable: renderer Animator is missing.");
            AnimancerState state = null;
            if (animancer != null && animancer.IsGraphInitialized)
                state = animancer.States.Current;
            else if (animancer != null)
                diagnostics.Add("animation unavailable: Animancer graph is not initialized; inspection did not initialize it.");
            if (animator != null && animator.avatar == null)
                diagnostics.Add("Animator avatar unavailable.");
            else if (animator != null && !animator.avatar.isValid)
                diagnostics.Add("Animator avatar is invalid.");
            else if (animator != null && !animator.avatar.isHuman)
                diagnostics.Add("Animator avatar is not humanoid.");
            if (state == null || state.Clip == null)
                diagnostics.Add("animation clip unavailable: associated Animancer has no current clip.");

            Renderer[] renderers = actor.GetComponentsInChildren<Renderer>(true);
            Bounds? bounds = null;
            int rendererCount = 0;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff)
                    continue;
                rendererCount++;
                if (bounds == null) bounds = renderer.bounds;
                else { Bounds combined = bounds.Value; combined.Encapsulate(renderer.bounds); bounds = combined; }
            }
            float[] boundsCenter = null;
            float[] boundsSize = null;
            if (bounds.HasValue)
            {
                boundsCenter = Vector(bounds.Value.center, diagnostics, "rendererBoundsCenter");
                boundsSize = Vector(bounds.Value.size, diagnostics, "rendererBoundsSize");
            }
            else diagnostics.Add("renderer bounds unavailable: no Renderer components exist under the actor.");

            SlopArenaPresentationBoneInfo[] boneInfo;
            string[] requestedBones = bones ?? Array.Empty<string>();
            boneInfo = new SlopArenaPresentationBoneInfo[requestedBones.Length];
            for (int i = 0; i < requestedBones.Length; i++)
                boneInfo[i] = ReadBone(actor, requestedBones[i], diagnostics);

            WeaponAttach attach = actor.GetComponent<WeaponAttach>();
            WeaponAttach.WeaponAttachInspectionEntry[] attachments = attach != null ? attach.ReadInspectionEntries() : null;
            if (attach == null) diagnostics.Add("weapon attachments unavailable: actor has no WeaponAttach component.");
            else if (attachments == null) diagnostics.Add("weapon attachments unavailable: WeaponAttach has no initialized entries.");
            if (attachments != null)
                foreach (WeaponAttach.WeaponAttachInspectionEntry entry in attachments)
                {
                    if (entry.WorldPosition != null && !Finite(entry.WorldPosition))
                    {
                        diagnostics.Add($"attachments[{entry.Index}].worldPosition unavailable: non-finite transform.");
                        entry.WorldPosition = null;
                    }
                    if (entry.WorldRotation != null && !Finite(entry.WorldRotation))
                    {
                        diagnostics.Add($"attachments[{entry.Index}].worldRotation unavailable: non-finite transform.");
                        entry.WorldRotation = null;
                    }
                }

            return new SlopArenaPresentationActorInfo
            {
                EntityId = actor.EntityId.ToString(CultureInfo.InvariantCulture),
                Name = actor.EntityName,
                ActionState = actor.CurrentActionState.ToString(),
                AttackSlot = actor.CurrentAttackSlot,
                AttackElapsedTicks = actor.CurrentAttackElapsedTicks,
                ActorActive = actor.gameObject.activeInHierarchy,
                ActorEnabled = actor.enabled,
                Position = Vector(root.position, diagnostics, "position"),
                Rotation = Rotation(root.rotation, diagnostics, "rotation"),
                LocalScale = Vector(root.localScale, diagnostics, "localScale"),
                AnimatorPresent = animator != null,
                AnimatorEnabled = animator != null && animator.enabled,
                AnimatorHasAvatar = animator != null && animator.avatar != null,
                AnimatorAvatarValid = animator != null && animator.avatar != null && animator.avatar.isValid,
                AnimatorIsHuman = animator != null && animator.avatar != null && animator.avatar.isHuman,
                AnimatorCullingMode = animator != null ? animator.cullingMode.ToString() : null,
                AnimatorController = animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : null,
                AnimancerPresent = animancer != null,
                Clip = state != null && state.Clip != null ? state.Clip.name : null,
                ClipTime = Finite(state != null ? (float?)state.Time : null, diagnostics, "clipTime"),
                ClipLength = Finite(state != null ? (float?)state.Length : null, diagnostics, "clipLength"),
                ClipNormalizedTime = Finite(state != null ? (float?)state.NormalizedTime : null, diagnostics, "clipNormalizedTime"),
                ClipSpeed = Finite(state != null ? (float?)state.Speed : null, diagnostics, "clipSpeed"),
                RendererCount = rendererCount,
                HasRendererBounds = bounds.HasValue,
                RendererBoundsCenter = boundsCenter,
                RendererBoundsSize = boundsSize,
                WeaponAttachPresent = attach != null,
                Bones = boneInfo,
                Attachments = attachments,
                Diagnostics = diagnostics.ToArray()
            };
        }

        private static SlopArenaPresentationBoneInfo ReadBone(PlayerRenderer actor, string name, List<string> diagnostics)
        {
            Transform bone = actor.ResolvePresentationBone(name);
            if (bone == null)
            {
                diagnostics.Add($"bone '{name}' unavailable: presentation bone could not be resolved.");
                return new SlopArenaPresentationBoneInfo { Name = name, Found = false };
            }
            return new SlopArenaPresentationBoneInfo
            {
                Name = name,
                Found = true,
                ResolvedName = bone.name,
                LocalPosition = Vector(bone.localPosition, diagnostics, $"bones.{name}.localPosition"),
                LocalRotation = Rotation(bone.localRotation, diagnostics, $"bones.{name}.localRotation"),
                WorldPosition = Vector(bone.position, diagnostics, $"bones.{name}.worldPosition"),
                WorldRotation = Rotation(bone.rotation, diagnostics, $"bones.{name}.worldRotation")
            };
        }


        private static bool IsLoaded(Scene scene) => scene.IsValid() && scene.isLoaded;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(float[] value)
        {
            foreach (float component in value) if (!IsFinite(component)) return false;
            return true;
        }
        private static float? Finite(float? value, List<string> diagnostics, string path)
        {
            if (!value.HasValue || IsFinite(value.Value)) return value;
            diagnostics.Add($"{path} unavailable: non-finite value.");
            return null;
        }
        private static float[] Vector(Vector3 value, List<string> diagnostics, string path)
        {
            var result = new[] { value.x, value.y, value.z };
            if (Finite(result)) return result;
            diagnostics.Add($"{path} unavailable: non-finite value.");
            return null;
        }
        private static float[] Rotation(Quaternion value, List<string> diagnostics, string path)
        {
            var result = new[] { value.x, value.y, value.z, value.w };
            if (Finite(result)) return result;
            diagnostics.Add($"{path} unavailable: non-finite value.");
            return null;
        }
        private static SlopArenaPresentationInspectionResult Failure(string error)
            => new SlopArenaPresentationInspectionResult { Success = false, Error = error };
    }

    public sealed class SlopArenaPresentationInspectionResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("target")] public string Target { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
        [JsonProperty("actor")] public SlopArenaPresentationActorInfo Actor { get; set; }
    }

    public sealed class SlopArenaPresentationActorInfo
    {
        [JsonProperty("entityId")] public string EntityId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("actionState")] public string ActionState { get; set; }
        [JsonProperty("attackSlot")] public byte AttackSlot { get; set; }
        [JsonProperty("attackElapsedTicks")] public int AttackElapsedTicks { get; set; }
        [JsonProperty("position")] public float[] Position { get; set; }
        [JsonProperty("rotation")] public float[] Rotation { get; set; }
        [JsonProperty("localScale")] public float[] LocalScale { get; set; }
        [JsonProperty("actorActive")] public bool ActorActive { get; set; }
        [JsonProperty("actorEnabled")] public bool ActorEnabled { get; set; }
        [JsonProperty("animatorPresent")] public bool AnimatorPresent { get; set; }
        [JsonProperty("animatorEnabled")] public bool AnimatorEnabled { get; set; }
        [JsonProperty("animatorHasAvatar")] public bool AnimatorHasAvatar { get; set; }
        [JsonProperty("animatorAvatarValid")] public bool AnimatorAvatarValid { get; set; }
        [JsonProperty("animatorIsHuman")] public bool AnimatorIsHuman { get; set; }
        [JsonProperty("animatorCullingMode")] public string AnimatorCullingMode { get; set; }
        [JsonProperty("animatorController")] public string AnimatorController { get; set; }
        [JsonProperty("animancerPresent")] public bool AnimancerPresent { get; set; }
        [JsonProperty("clip")] public string Clip { get; set; }
        [JsonProperty("clipTime")] public float? ClipTime { get; set; }
        [JsonProperty("clipLength")] public float? ClipLength { get; set; }
        [JsonProperty("clipNormalizedTime")] public float? ClipNormalizedTime { get; set; }
        [JsonProperty("clipSpeed")] public float? ClipSpeed { get; set; }
        [JsonProperty("rendererCount")] public int RendererCount { get; set; }
        [JsonProperty("hasRendererBounds")] public bool HasRendererBounds { get; set; }
        [JsonProperty("rendererBoundsCenter")] public float[] RendererBoundsCenter { get; set; }
        [JsonProperty("rendererBoundsSize")] public float[] RendererBoundsSize { get; set; }
        [JsonProperty("weaponAttachPresent")] public bool WeaponAttachPresent { get; set; }
        [JsonProperty("bones")] public SlopArenaPresentationBoneInfo[] Bones { get; set; }
        [JsonProperty("attachments")] public WeaponAttach.WeaponAttachInspectionEntry[] Attachments { get; set; }
        [JsonProperty("diagnostics")] public string[] Diagnostics { get; set; }
    }

    public sealed class SlopArenaPresentationBoneInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("found")] public bool Found { get; set; }
        [JsonProperty("resolvedName")] public string ResolvedName { get; set; }
        [JsonProperty("localPosition")] public float[] LocalPosition { get; set; }
        [JsonProperty("localRotation")] public float[] LocalRotation { get; set; }
        [JsonProperty("worldPosition")] public float[] WorldPosition { get; set; }
        [JsonProperty("worldRotation")] public float[] WorldRotation { get; set; }
    }
}
