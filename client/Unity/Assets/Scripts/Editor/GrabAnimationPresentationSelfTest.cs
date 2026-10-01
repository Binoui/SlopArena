using System;
using System.Linq;
using Animancer;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GrabAnimationPresentationSelfTest
{
    [MenuItem("Tools/SlopArena/Tests/Grab Animation Presentation")]
    public static void Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run the isolated grab presentation check in Edit Mode.");

        var source = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(
            "Assets/CharacterPackages/manki/CharacterAssetCatalog.asset");
        var shared = Resources.Load<CharacterAnimationConfig>("AnimationConfigs/Shared_AnimConfig");
        var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
        catalog.Animations = source.Bindings.Select(binding => new CharacterAnimationCatalog.AnimationEntry
        {
            SemanticId = binding.SemanticId,
            Clip = binding.Clip,
        }).ToArray();
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Grab presentation regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var renderer = root.AddComponent<PlayerRenderer>();
            var definition = new CharacterDefinition
            {
                Class = CharacterClass.Manki,
                IdleAnim = "anim.manki.idle",
                HitSmallAnim = "anim.manki.hit-small",
                Slot1 = new AbilitySpec
                {
                    AnimationNames = new[] { "anim.manki.g1" },
                    Stages = new[] { new AttackStage { DurationTicks = 30 } },
                },
            };
            renderer.SetAnimationCatalog(catalog);
            renderer.SetCharacterDefinition(definition);
            renderer.LoadModel(definition, source.Rig);
            var graph = root.GetComponentInChildren<AnimancerComponent>();
            var state = new CharacterState { State = ActionState.Attacking, AttackSlot = AbilitySlots.Slot1, IsGrounded = true };
            renderer.ApplyServerState(state);
            int attemptTicks = DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks
                + DefenseConfig.GrabWhiffRecoveryTicks;
            state.State = ActionState.GrabAttempt;
            state.AttackSlot = 0;
            state.StateTicks = (ushort)attemptTicks;
            renderer.ApplyServerState(state);
            Require(renderer.CurrentAttackSlot == 0, "Attack-to-grab retained an attack slot.");
            Require(graph.States.Current.Clip == shared.GetClipByName("grab"), "Grab did not replace the attack clip.");

            state.StateTicks = (ushort)(attemptTicks / 2);
            renderer.ApplyServerState(state);
            Require(Math.Abs(graph.States.Current.NormalizedTime - 0.5) < 0.00001, "Late grab snapshot lost its phase progress.");
            state.StateTicks = (ushort)attemptTicks;
            renderer.ApplyServerState(state);
            Require(graph.States.Current.NormalizedTime == 0, "Replayed grab did not rewind a reused clip.");
            state.StateTicks = (ushort)(attemptTicks / 2);
            renderer.ApplyServerState(state);
            state.HitstopTicks = 2;
            renderer.ApplyServerState(state);
            graph.Evaluate(0.2f);
            Require(Math.Abs(graph.States.Current.NormalizedTime - 0.5) < 0.00001, "Hitstop advanced the grab pose.");

            state.HitstopTicks = 0;
            state.State = ActionState.Throwing;
            state.StateTicks = DefenseConfig.ThrowReleaseTicks;
            renderer.ApplyServerState(state);
            Require(graph.States.Current.Clip == shared.GetClipByName("throw")
                && graph.States.Current.NormalizedTime == 0, "Capture did not switch directly to the start of throw.");
            state.StateTicks = (ushort)(DefenseConfig.ThrowReleaseTicks / 2);
            renderer.ApplyServerState(state);
            Require(Math.Abs(graph.States.Current.NormalizedTime - 0.5) < 0.00001, "Throw lost replicated phase progress.");
            state.State = ActionState.Idle;
            renderer.ApplyServerState(state);
            Require(graph.States.Current.Clip == source.Bindings.Single(binding => binding.SemanticId == definition.IdleAnim).Clip,
                "Release did not return to locomotion.");

            state.State = ActionState.GrabAttempt;
            state.StateTicks = (ushort)attemptTicks;
            renderer.ApplyServerState(state);
            state.State = ActionState.Hitstun;
            state.HitstunTicks = 10;
            renderer.ApplyServerState(state);
            Require(graph.States.Current.Clip == source.Bindings.Single(binding => binding.SemanticId == definition.HitSmallAnim).Clip,
                "Hitstun did not interrupt grab.");
            Debug.Log("[GrabAnimationPresentationSelfTest] Passed attack cleanup, late snapshots, replay, hitstop, direct throw, release, and interruption.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(catalog);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
