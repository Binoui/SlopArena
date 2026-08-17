namespace SlopArena.Shared.AI;

/// <summary>
/// Persistent per-entity bot state that spans ticks. Held by the runner/controller — never
/// written into <see cref="CharacterState"/>, so the prediction wire is untouched.
/// </summary>
public sealed class BotMemory
{
    /// <summary>Ticks to wait before considering another attack press (anti-buffer spam).</summary>
    public int PressCooldownTicks;

    /// <summary>Remaining ticks backing off from the opponent (disengage spacing). While > 0 the
    /// bot moves away and does not attack — breaks the point-blank mash deadlock so fights develop.</summary>
    public int RepositionTicks;

    /// <summary>Whether the bot was in hitstun last tick (hitstun rising-edge detection).</summary>
    public bool WasInHitstun;

    /// <summary>Whether the bot was mid-attack (AttackSlot &gt; 0) last tick (attack-end detection).</summary>
    public bool WasAttacking;

    /// <summary>Reset per-tick-decrementing fields; called once before a match starts.</summary>
    public void Reset()
    {
        PressCooldownTicks = 0;
        RepositionTicks = 0;
        WasInHitstun = false;
        WasAttacking = false;
    }
}
