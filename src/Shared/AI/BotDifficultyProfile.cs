namespace SlopArena.Shared.AI;

/// <summary>
/// Fixed, deterministic tuning for the simple heuristic CPU. Probabilities are in the 0..1
/// range; callers supply the only <see cref="System.Random"/> used to sample them.
/// </summary>
public readonly struct BotDifficultyProfile
{
    public readonly int DecisionIntervalTicks;
    public readonly int ReactionDelayTicks;
    public readonly float AttackChance;
    public readonly float RetreatChance;
    public readonly float DodgeChance;
    public readonly float JumpChance;
    public readonly float RangeError;
    public readonly float PunishChance;
    public readonly float ComboChance;

    private BotDifficultyProfile(
        int decisionIntervalTicks,
        int reactionDelayTicks,
        float attackChance,
        float retreatChance,
        float dodgeChance,
        float jumpChance,
        float rangeError,
        float punishChance,
        float comboChance)
    {
        DecisionIntervalTicks = decisionIntervalTicks;
        ReactionDelayTicks = reactionDelayTicks;
        AttackChance = attackChance;
        RetreatChance = retreatChance;
        DodgeChance = dodgeChance;
        JumpChance = jumpChance;
        RangeError = rangeError;
        PunishChance = punishChance;
        ComboChance = comboChance;
    }

    /// <summary>
    /// Return the fixed profile for a Smash-style CPU level. Values outside 1..9 clamp to the
    /// nearest supported level so bad inspector or replay data cannot change match semantics.
    /// </summary>
    public static BotDifficultyProfile ForLevel(int level)
    {
        level = Math.Clamp(level, 1, 9);
        return level switch
        {
            1 => new(30, 24, 0.20f, 0.35f, 0.05f, 0.10f, 0.45f, 0.00f, 0.00f),
            2 => new(26, 20, 0.28f, 0.32f, 0.10f, 0.15f, 0.38f, 0.05f, 0.03f),
            3 => new(22, 16, 0.36f, 0.29f, 0.16f, 0.20f, 0.32f, 0.12f, 0.08f),
            4 => new(18, 12, 0.46f, 0.25f, 0.22f, 0.25f, 0.26f, 0.20f, 0.14f),
            5 => new(14, 8, 0.56f, 0.21f, 0.30f, 0.30f, 0.20f, 0.32f, 0.22f),
            6 => new(11, 6, 0.64f, 0.18f, 0.38f, 0.35f, 0.15f, 0.45f, 0.32f),
            7 => new(8, 4, 0.72f, 0.15f, 0.46f, 0.40f, 0.11f, 0.58f, 0.44f),
            8 => new(6, 2, 0.80f, 0.12f, 0.54f, 0.45f, 0.08f, 0.72f, 0.58f),
            _ => new(4, 0, 0.88f, 0.10f, 0.62f, 0.50f, 0.05f, 0.85f, 0.72f),
        };
    }
}
