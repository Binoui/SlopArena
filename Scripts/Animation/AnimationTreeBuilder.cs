#nullable enable
using Godot;
using SlopArena.Shared;
using System.Collections.Generic;

namespace SlopArena;

/// <summary>
/// Builds an AnimationNodeStateMachine from CharacterDefinition data.
/// Replaces .tscn-based AnimationTree sub-resources — add a new character
/// by populating animation name fields, no .tscn editing required.
///
/// Produces the same state names and TimeScale parameter paths as the old .tscn
/// so the FSM (StateMachine.cs) and ApplyAnimationTimeScales() work unchanged.
/// </summary>
public static class AnimationTreeBuilder
{
    /// <summary>
    /// Build a complete AnimationNodeStateMachine for a character.
    /// Caller assigns this to AnimationTree.TreeRoot.
    /// </summary>
    public static AnimationNodeStateMachine Build(AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        _allAttackAnimNames.Clear();
        _chargeLoopAnimNames.Clear();

        var sm = new AnimationNodeStateMachine();

        AddLocomotionStates(sm, animPlayer, charDef);
        AddJumpState(sm, animPlayer, charDef);
        AddFallState(sm, animPlayer, charDef);
        AddHitReactionStates(sm, animPlayer, charDef);
        AddAttackStates(sm, animPlayer, charDef);
        AddChargeLoopStates(sm, animPlayer, charDef);
        AddTransitions(sm, charDef);

        return sm;
    }

    // ════════════════════════════════════════
    //  BUILDERS
    // ════════════════════════════════════════

    private static void AddLocomotionStates(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        // Idle (looping)
        var idle = CreateWrappedState(animPlayer, charDef.IdleAnim, charDef);
        if (idle.GetNode("Animation") is AnimationNodeAnimation idleAnim)
        {
            idleAnim.UseCustomTimeline = true;
            idleAnim.TimelineLength = 2.0f;
            idleAnim.StretchTimeScale = false;
            idleAnim.LoopMode = Animation.LoopModeEnum.Linear;
        }
        sm.AddNode("Idle", idle, new Vector2(200, 100));

        // Run (looping)
        var run = CreateWrappedState(animPlayer, charDef.RunAnim, charDef);
        if (run.GetNode("Animation") is AnimationNodeAnimation runAnim)
        {
            runAnim.UseCustomTimeline = true;
            runAnim.TimelineLength = 2.0f;
            runAnim.StretchTimeScale = false;
            runAnim.LoopMode = Animation.LoopModeEnum.Linear;
        }
        sm.AddNode("Run", run, new Vector2(400, 100));

        // Dash (looping)
        var dash = CreateWrappedState(animPlayer, charDef.DashAnim, charDef);
        if (dash.GetNode("Animation") is AnimationNodeAnimation dashAnim)
        {
            dashAnim.UseCustomTimeline = true;
            dashAnim.TimelineLength = 2.0f;
            dashAnim.StretchTimeScale = false;
            dashAnim.LoopMode = Animation.LoopModeEnum.Linear;
        }
        sm.AddNode("dash", dash, new Vector2(600, 400));
    }

    private static void AddJumpState(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        var jump = CreateWrappedState(animPlayer, charDef.JumpAnim, charDef);
        if (jump.GetNode("Animation") is AnimationNodeAnimation jumpAnim)
        {
            jumpAnim.UseCustomTimeline = true;
            jumpAnim.TimelineLength = 3.0f;
            jumpAnim.StretchTimeScale = false;
            jumpAnim.LoopMode = Animation.LoopModeEnum.Linear;
        }
        sm.AddNode("jump", jump, new Vector2(200, 175));
    }

    private static void AddFallState(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        var fall = CreateWrappedState(animPlayer, charDef.FallAnim, charDef);
        if (fall.GetNode("Animation") is AnimationNodeAnimation fallAnim)
        {
            fallAnim.TimelineLength = 20f;
            fallAnim.UseCustomTimeline = true;
            fallAnim.StretchTimeScale = false;
            fallAnim.LoopMode = Animation.LoopModeEnum.Linear;
        }
        sm.AddNode("fall", fall, new Vector2(200, 250));
    }

    private static void AddHitReactionStates(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        var hits = new[]
        {
            ("hit_small", charDef.HitSmallAnim, new Vector2(600, 600)),
            ("hit_medium", charDef.HitMediumAnim, new Vector2(800, 600)),
            ("hit_hard", charDef.HitHardAnim, new Vector2(1000, 600)),
        };

        foreach (var (stateName, clipName, pos) in hits)
        {
            var node = CreateWrappedState(animPlayer, clipName, charDef);
            if (node.GetNode("Animation") is AnimationNodeAnimation animNode)
            {
                animNode.UseCustomTimeline = true;
                animNode.TimelineLength = 2.0f;
                animNode.StretchTimeScale = false;
                animNode.LoopMode = Animation.LoopModeEnum.None;
            }
            sm.AddNode(stateName, node, pos);
        }
    }

    private static readonly HashSet<string> _allAttackAnimNames = new();
    private static readonly HashSet<string> _chargeLoopAnimNames = new();

    private static void AddAttackStates(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        _allAttackAnimNames.Clear();
        var abilities = new[]
        {
            charDef.LMB, charDef.AirLMB, charDef.RMB, charDef.AirRMB,
            charDef.Q, charDef.E, charDef.R, charDef.F
        };
        int col = 0;

        foreach (var ability in abilities)
        {
            if (ability?.AnimationNames == null) continue;
            foreach (var animName in ability.AnimationNames)
            {
                if (string.IsNullOrEmpty(animName)) continue;
                if (!_allAttackAnimNames.Add(animName)) continue; // dedupe

                var node = CreateWrappedState(animPlayer, animName, charDef);
                sm.AddNode(animName, node, new Vector2(800 + col * 200, 100));
                col++;
            }
        }
    }

    private static void AddChargeLoopStates(
        AnimationNodeStateMachine sm, AnimationPlayer animPlayer, CharacterDefinition charDef)
    {
        _chargeLoopAnimNames.Clear();

        // RMB charge loop (AerosolFlameSpec)
        if (charDef.RMB is AerosolFlameSpec flame && !string.IsNullOrEmpty(flame.ChargeAnimName))
        {
            _chargeLoopAnimNames.Add(flame.ChargeAnimName);
            var node = CreateWrappedState(animPlayer, flame.ChargeAnimName, charDef);
            if (node.GetNode("Animation") is AnimationNodeAnimation animNode)
                animNode.LoopMode = Animation.LoopModeEnum.Linear;
            sm.AddNode(flame.ChargeAnimName, node, new Vector2(1000, 250));
        }

        // Q charge loop (RoundBombSpec)
        if (charDef.Q is RoundBombSpec bomb && !string.IsNullOrEmpty(bomb.LoopAnimName))
        {
            _chargeLoopAnimNames.Add(bomb.LoopAnimName);
            var node = CreateWrappedState(animPlayer, bomb.LoopAnimName, charDef);
            sm.AddNode(bomb.LoopAnimName, node, new Vector2(792, 413));
        }
    }

    // ════════════════════════════════════════
    //  TRANSITIONS
    // ════════════════════════════════════════

    private static void AddTransitions(
        AnimationNodeStateMachine sm, CharacterDefinition charDef)
    {
        var xfade = new AnimationNodeStateMachineTransition();
        var xfadeRestart = new AnimationNodeStateMachineTransition { Reset = true };
        var xfadeSlowFall = new AnimationNodeStateMachineTransition { XfadeTime = 0.15f, Reset = true };

        // Core movement
        sm.AddTransition("Idle", "Run", xfade);
        sm.AddTransition("Run", "Idle", xfade);

        // Jump
        sm.AddTransition("Idle", "jump", xfade);
        sm.AddTransition("Run", "jump", xfade);
        sm.AddTransition("jump", "jump", xfadeRestart);
        sm.AddTransition("jump", "Run", xfade);
        sm.AddTransition("jump", "Idle", xfade);

        // Fall (from jump end: slow crossfade, from off-edge/hitstun: fast)
        sm.AddTransition("jump", "fall", xfadeSlowFall);
        sm.AddTransition("Idle", "fall", xfade);   // walked off edge — fast
        sm.AddTransition("Run", "fall", xfade);    // ran off edge — fast

        // Landing from fall
        sm.AddTransition("fall", "Idle", xfade);
        sm.AddTransition("fall", "Run", xfade);

        // Dash
        sm.AddTransition("Idle", "dash", xfade);
        sm.AddTransition("Run", "dash", xfade);
        sm.AddTransition("fall", "dash", xfade);
        sm.AddTransition("dash", "End", xfade);

        // Hit reactions → fall
        sm.AddTransition("hit_small", "fall", xfade);
        sm.AddTransition("hit_medium", "fall", xfade);
        sm.AddTransition("hit_hard", "fall", xfade);

        // Attack states: idle/run/fall → each attack, attack → End
        foreach (var animName in _allAttackAnimNames)
        {
            sm.AddTransition("Idle", animName, xfade);
            sm.AddTransition("Run", animName, xfade);
            sm.AddTransition("fall", animName, xfade);
            sm.AddTransition(animName, "End", xfade);
        }

        // Charge loops: idle/run/fall → loop, loop → End
        foreach (var loopName in _chargeLoopAnimNames)
        {
            sm.AddTransition("Idle", loopName, xfade);
            sm.AddTransition("Run", loopName, xfade);
            sm.AddTransition("fall", loopName, xfade);
            sm.AddTransition(loopName, "End", xfade);
        }

        // RMB: charge loop → attack
        if (charDef.RMB is AerosolFlameSpec flame &&
            !string.IsNullOrEmpty(flame.ChargeAnimName) &&
            flame.AnimationNames is { Length: > 0 } &&
            !string.IsNullOrEmpty(flame.AnimationNames[0]))
        {
            sm.AddTransition(flame.ChargeAnimName, flame.AnimationNames[0], xfade);
        }

        // Q: charge loop → attack (Manki)
        if (charDef.Q is RoundBombSpec bomb &&
            !string.IsNullOrEmpty(bomb.LoopAnimName) &&
            bomb.AnimationNames is { Length: > 0 } &&
            !string.IsNullOrEmpty(bomb.AnimationNames[0]))
        {
            sm.AddTransition(bomb.LoopAnimName, bomb.AnimationNames[0], xfade);
        }
    }

    // ════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════

    /// <summary>
    /// Creates the standard TimeScale → BlendTree → output wrapper triplet.
    /// Returns the BlendTree root (added as a state machine node).
    /// TimeScale parameter path: parameters/{stateName}/TimeScale/scale
    /// </summary>
    private static AnimationNodeBlendTree CreateWrappedState(
        AnimationPlayer animPlayer, string clipName, CharacterDefinition charDef)
    {
        var animNode = new AnimationNodeAnimation { Animation = clipName };
        ApplyOverrides(animNode, clipName, charDef);

        var timeScale = new AnimationNodeTimeScale();

        var blendTree = new AnimationNodeBlendTree();
        blendTree.AddNode("Animation", animNode, new Vector2(100, 100));
        blendTree.AddNode("TimeScale", timeScale, new Vector2(400, 100));
        blendTree.ConnectNode("output", 0, "TimeScale");
        blendTree.ConnectNode("TimeScale", 0, "Animation");

        return blendTree;
    }

    private static void ApplyOverrides(
        AnimationNodeAnimation node, string clipName, CharacterDefinition def)
    {
        if (def.ClipOverrides == null) return;
        foreach (var cfg in def.ClipOverrides)
        {
            if (cfg.Name != clipName) continue;
            if (cfg.LoopMode.HasValue)
                node.LoopMode = (Animation.LoopModeEnum)(int)cfg.LoopMode.Value;
            if (cfg.StartOffset.HasValue)
            {
                node.UseCustomTimeline = true;
                node.StartOffset = cfg.StartOffset.Value;
            }
            if (cfg.TimelineLength.HasValue)
            {
                node.UseCustomTimeline = true;
                node.TimelineLength = cfg.TimelineLength.Value;
            }
            if (cfg.StretchTimeScale)
                node.StretchTimeScale = true;
            break;
        }
    }
}
