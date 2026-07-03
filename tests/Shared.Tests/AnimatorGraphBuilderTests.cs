using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public class AnimatorGraphBuilderTests
{
    private static GraphDirectTransition? FindDirect(List<GraphDirectTransition> list, string from, string to)
    {
        foreach (var t in list)
            if (t.FromState == from && t.ToState == to) return t;
        return null;
    }

    private static GraphAnyTransition? FindAny(List<GraphAnyTransition> list, string to)
    {
        foreach (var t in list)
            if (t.ToState == to) return t;
        return null;
    }

    private static GraphState? FindState(List<GraphState> list, string name)
    {
        foreach (var s in list)
            if (s.Name == name) return s;
        return null;
    }

    [Fact]
    public void DefaultStates_AllPresent()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        var move = FindState(graph.States, "Movement");
        Assert.NotNull(move);
        Assert.True(move.Value.IsDefault);
        Assert.True(move.Value.IsBlendTree);
        Assert.Equal("MovementBlend", move.Value.MotionName);

        Assert.NotNull(FindState(graph.States, "Jump"));
        Assert.NotNull(FindState(graph.States, "Fall"));
        Assert.NotNull(FindState(graph.States, "Dash"));
        Assert.NotNull(FindState(graph.States, "HitstunSmall"));
        Assert.NotNull(FindState(graph.States, "HitstunMedium"));
        Assert.NotNull(FindState(graph.States, "HitstunHard"));

        var dash = FindState(graph.States, "Dash");
        Assert.True(dash!.Value.AutoExit);

        var hitstunSmall = FindState(graph.States, "HitstunSmall");
        Assert.True(hitstunSmall!.Value.AutoExit);
        var hitstunMedium = FindState(graph.States, "HitstunMedium");
        Assert.True(hitstunMedium!.Value.AutoExit);
        var hitstunHard = FindState(graph.States, "HitstunHard");
        Assert.True(hitstunHard!.Value.AutoExit);
    }

    [Fact]
    public void DefaultTransitions_MovementToJump_OnJumpTrigger()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);
        var t = FindDirect(graph.DirectTransitions, "Movement", "Jump");
        Assert.NotNull(t);
        Assert.Contains(t.Value.Conditions, c => c.Parameter == "Jump" && c.Mode == AnimConditionMode.If);
    }

    [Fact]
    public void DefaultTransitions_MovementToFall_WhenNotGrounded()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);
        var t = FindDirect(graph.DirectTransitions, "Movement", "Fall");
        Assert.NotNull(t);
        Assert.Contains(t.Value.Conditions, c => c.Parameter == "IsGrounded" && c.Mode == AnimConditionMode.IfNot);
    }

    [Fact]
    public void DefaultAnyState_DashHitstunMovement_Present()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        Assert.NotNull(FindAny(graph.AnyTransitions, "Dash"));
        Assert.NotNull(FindAny(graph.AnyTransitions, "HitstunSmall"));
        Assert.NotNull(FindAny(graph.AnyTransitions, "HitstunMedium"));
        Assert.NotNull(FindAny(graph.AnyTransitions, "HitstunHard"));
        Assert.NotNull(FindAny(graph.AnyTransitions, "Movement"));
    }

    [Fact]
    public void MankiQ_HasHoldPhaseTransitions()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        var t1 = FindDirect(graph.DirectTransitions, "spell_q_start", "spell_q_loop");
        Assert.NotNull(t1);
        Assert.True(t1!.Value.HasExitTime);
        Assert.Equal(0.9f, t1.Value.ExitTime);
        Assert.Empty(t1.Value.Conditions);

        var t2 = FindDirect(graph.DirectTransitions, "spell_q_loop", "spell_q_end");
        Assert.NotNull(t2);
        Assert.Contains(t2!.Value.Conditions,
            c => c.Parameter == "ComboStage" && c.Mode == AnimConditionMode.Equals && c.Threshold == 1);
    }

    [Fact]
    public void MankiQ_HoldLoopState_NoAutoExit_IsHoldLoop()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        var loop = FindState(graph.States, "spell_q_loop");
        Assert.NotNull(loop);
        Assert.False(loop!.Value.AutoExit);
        Assert.True(loop.Value.IsHoldLoop);
    }

    [Fact]
    public void MankiQ_HoldLoopState_NoAnyState()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);
        Assert.Null(FindAny(graph.AnyTransitions, "spell_q_loop"));
    }

    [Fact]
    public void MankiQ_ThrowState_AnyStateHasEffectiveStage()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        var any = FindAny(graph.AnyTransitions, "spell_q_end");
        Assert.NotNull(any);
        Assert.Contains(any!.Value.Conditions,
            c => c.Parameter == "ComboStage" && c.Mode == AnimConditionMode.Equals && c.Threshold == 1);
        Assert.DoesNotContain(any.Value.Conditions,
            c => c.Parameter == "ComboStage" && c.Threshold == 2);
        Assert.Contains(any.Value.Conditions,
            c => c.Parameter == "AttackSlot" && c.Threshold == 2);
    }

    [Fact]
    public void MankiLMB_HasComboChainTransitions()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        var t12 = FindDirect(graph.DirectTransitions, "spell_lmb_1", "spell_lmb_2");
        Assert.NotNull(t12);
        Assert.Contains(t12!.Value.Conditions,
            c => c.Parameter == "ComboStage" && c.Mode == AnimConditionMode.Equals && c.Threshold == 1);

        var t23 = FindDirect(graph.DirectTransitions, "spell_lmb_2", "spell_lmb_3");
        Assert.NotNull(t23);
        Assert.Contains(t23!.Value.Conditions,
            c => c.Parameter == "ComboStage" && c.Mode == AnimConditionMode.Equals && c.Threshold == 2);

        var lmb3 = FindState(graph.States, "spell_lmb_3");
        Assert.True(lmb3!.Value.AutoExit);
    }

    [Fact]
    public void DeduplicatesSharedAnimationNames()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        Assert.Equal(1, graph.States.Count(s => s.Name == "spell_q_end"));
        Assert.Equal(1, graph.AnyTransitions.Count(t => t.ToState == "spell_q_end"));
    }

    [Fact]
    public void Bunny_DoesNotCrash()
    {
        var bunnyDef = CharacterRegistry.Get(CharacterClass.Bunny);
        var graph = AnimatorGraphBuilder.Build(bunnyDef);

        Assert.NotNull(graph.States);
        Assert.NotNull(graph.DirectTransitions);
        Assert.NotNull(graph.AnyTransitions);
        Assert.NotNull(FindState(graph.States, "Movement"));
        Assert.NotNull(FindState(graph.States, "spell_lmb_1"));
        Assert.NotNull(FindState(graph.States, "spell_q"));
    }

    [Fact]
    public void MankiRMB_UsesHoldReleaseNotComboChain()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        var t = FindDirect(graph.DirectTransitions, "spell_rmb_charged", "spell_rmb_attack");
        Assert.NotNull(t);
        Assert.NotEmpty(t!.Value.Conditions);
        Assert.Contains(t!.Value.Conditions,
            c => c.Parameter == "ComboStage" && c.Mode == AnimConditionMode.Equals && c.Threshold == 1);
    }

    [Fact]
    public void NoDuplicateTransitions()
    {
        var graph = AnimatorGraphBuilder.Build(TestHelpers.MankiDef);

        // All direct transitions should be unique by (from, to)
        var seen = new HashSet<(string, string)>();
        foreach (var t in graph.DirectTransitions)
        {
            bool added = seen.Add((t.FromState, t.ToState));
            Assert.True(added, $"Duplicate direct transition: {t.FromState} -> {t.ToState}");
        }

        // All AnyState transitions should be unique by ToState
        var seen2 = new HashSet<string>();
        foreach (var t in graph.AnyTransitions)
        {
            bool added = seen2.Add(t.ToState);
            Assert.True(added, $"Duplicate AnyState transition -> {t.ToState}");
        }
    }
}
