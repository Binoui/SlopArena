using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>Mirrors Unity's AnimatorConditionMode. Values match Unity's internal enum.</summary>
public enum AnimConditionMode : byte
{
    If    = 1,   // trigger
    IfNot = 2,   // trigger inverted
    Equals = 6,  // int/float comparison
}

public struct GraphCondition
{
    public string Parameter;
    public AnimConditionMode Mode;
    public float Threshold;
}

public struct GraphState
{
    public string Name;
    /// <summary>Clip name to bind, or "MovementBlend" for the blend tree.</summary>
    public string MotionName;
    public bool IsBlendTree;
    public bool WriteDefaultValues;
    public bool IsDefault;      // default SM state (exactly one)
    public int PositionX;       // editor grid X
    public int PositionY;       // editor grid Y
    /// <summary>Has an exit-time transition to Movement (set for ability states that auto-return).</summary>
    public bool AutoExit;
    /// <summary>Hold-phase loop state — no AutoExit, skipped by AnyState pass.</summary>
    public bool IsHoldLoop;
}

public struct GraphDirectTransition
{
    public string FromState;
    public string ToState;
    public GraphCondition[] Conditions; // empty array for exit-time-only transitions
    public float Duration;
    public bool HasExitTime;
    public float ExitTime;     // meaningful only if HasExitTime
}

public struct GraphAnyTransition
{
    public string ToState;
    public GraphCondition[] Conditions;
    public float Duration;
    public bool InterruptionSource;
}

public struct AnimatorGraph
{
    public string DefaultStateName;
    public List<GraphState> States;
    public List<GraphDirectTransition> DirectTransitions;
    public List<GraphAnyTransition> AnyTransitions;
}
