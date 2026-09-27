namespace SlopArena.Shared.AI;

/// <summary>
/// Named CPU challenge tiers shared by Solo, Training, and deterministic self-play.
/// The enum values are the serialized tier indices; no numeric difficulty scale is retained.
/// </summary>
public enum CpuDifficulty
{
    Easy,
    Normal,
    Hard,
}

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
    public readonly float DefenseChance;
    public readonly float JumpChance;
    public readonly float RangeError;
    public readonly float PunishChance;
    public readonly float ComboChance;
    /// <summary>Chance of selecting a special when a usable normal is also in range.</summary>
    public readonly float SpecialChance;
    private BotDifficultyProfile(
        int decisionIntervalTicks,
        int reactionDelayTicks,
        float attackChance,
        float retreatChance,
        float defenseChance,
        float jumpChance,
        float rangeError,
        float punishChance,
        float comboChance,
        float specialChance)
    {
        DecisionIntervalTicks = decisionIntervalTicks;
        ReactionDelayTicks = reactionDelayTicks;
        AttackChance = attackChance;
        RetreatChance = retreatChance;
        DefenseChance = defenseChance;
        JumpChance = jumpChance;
        RangeError = rangeError;
        PunishChance = punishChance;
        ComboChance = comboChance;
        SpecialChance = specialChance;
    }

    /// <summary>
    /// Normalize a serialized tier. Unknown values use the middle tier rather than changing
    /// match semantics through an unsupported numeric value.
    /// </summary>
    public static CpuDifficulty Normalize(CpuDifficulty difficulty)
        => difficulty switch
        {
            CpuDifficulty.Easy => CpuDifficulty.Easy,
            CpuDifficulty.Hard => CpuDifficulty.Hard,
            _ => CpuDifficulty.Normal,
        };

    /// <summary>Return the player-facing name for a CPU tier.</summary>
    public static string DisplayName(CpuDifficulty difficulty)
        => Normalize(difficulty).ToString().ToUpperInvariant();

    /// <summary>
    /// Return the fixed profile for a named CPU difficulty.
    /// Reaction delays are 24, 18, and 12 simulation ticks for Easy, Normal, and Hard.
    /// </summary>
    public static BotDifficultyProfile ForDifficulty(CpuDifficulty difficulty)
    {
        return Normalize(difficulty) switch
        {
            CpuDifficulty.Easy => new(30, 24, 0.20f, 0.35f, 0.05f, 0.20f, 0.45f, 0.00f, 0.00f, 0.08f),
            CpuDifficulty.Normal => new(14, 18, 0.56f, 0.21f, 0.30f, 0.50f, 0.20f, 0.32f, 0.22f, 0.15f),
            _ => new(4, 12, 0.88f, 0.10f, 0.62f, 0.60f, 0.05f, 0.85f, 0.72f, 0.25f),
        };
    }
}
