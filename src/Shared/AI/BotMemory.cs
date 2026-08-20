namespace SlopArena.Shared.AI;

/// <summary>
/// Persistent per-entity bot state that spans ticks. Held by the runner/controller — never
/// written into <see cref="CharacterState"/>, so the prediction wire is untouched.
/// </summary>
public sealed class BotMemory
{
    /// <summary>Smash-style CPU level, clamped by the caller/profile factory to 1..9.</summary>
    public int DifficultyLevel = 5;

    /// <summary>Ticks remaining before a fresh attack/pressure decision.</summary>
    public int DecisionTicksRemaining;

    /// <summary>Ticks remaining before a newly detected threat can trigger a reaction.</summary>
    public int ReactionTicksRemaining;

    /// <summary>Last slot pressed by this bot; runner-owned telemetry, not sim state.</summary>
    public byte LastPressedSlot;

    /// <summary>True for the previous tick when this bot's hitbox connected.</summary>
    public bool LastAttackConnected;

    /// <summary>Whether the target was attacking/recovering on the previous pre-tick snapshot.</summary>
    public bool LastTargetWasAttacking;

    /// <summary>Whether this bot was actionable on the previous policy call.</summary>
    public bool WasActionable;

    /// <summary>Stable lateral direction for range-holding: -1 or +1 once selected.</summary>
    public sbyte StrafeDirection;

    /// <summary>Reset all runner-owned policy state before a match starts.</summary>
    public void Reset()
    {
        DifficultyLevel = 5;
        DecisionTicksRemaining = 0;
        ReactionTicksRemaining = 0;
        LastPressedSlot = 0;
        LastAttackConnected = false;
        LastTargetWasAttacking = false;
        WasActionable = false;
        StrafeDirection = 0;
    }
}
