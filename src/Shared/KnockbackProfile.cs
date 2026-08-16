namespace SlopArena.Shared;

/// <summary>
/// Knockback archetype — defines a fixed launch angle + base/growth magnitude.
/// Tune the profile table to adjust game-wide feel. Use Custom for unique angles.
/// </summary>
public enum KnockbackProfile : byte
{
    Light    = 0,  // 15°, base=2,  growth=1.5  — combo glue, slight pop
    Medium   = 1,  // 15°, base=8,  growth=5    — knockdown, reset
    Launcher = 2,  // 25°, base=8,  growth=4    — pop-up, stays on screen
    Kill     = 3,  // 20°, base=18, growth=10   — blast zone send
    Spike    = 4,  // -45°,base=12, growth=4    — downward
    Custom   = 5,  // uses Angle/BaseKnockback/KnockbackGrowth overrides
    Adaptive  = 6,  // auto-angle (Melee 361): pitch derived at HIT time from the
                   // hitbox→victim displacement, not a fixed constant. Uses the
                   // struct's own BaseKnockback/KnockbackGrowth; Angle is ignored.
}

/// <summary>
/// Replaces old (BaseKnockback, KnockbackGrowth, KnockbackUpward) triple.
/// Stores a profile + optional custom overrides. Call Resolve() at spawn time
/// to get flat angle/base/growth for the Hitbox struct.
/// </summary>
public struct KnockbackData
{
    /// <summary>
    /// Sentinel angle returned by Resolve() for <see cref="KnockbackProfile.Adaptive"/>.
    /// -128 is outside the valid -90..90 pitch range; SpellResolver detects it at hit
    /// time and replaces it with the position-derived auto-angle.
    /// </summary>
    public const sbyte AdaptiveAngle = sbyte.MinValue;

    public KnockbackProfile Profile;
    /// <summary>Only used when Profile == Custom or Adaptive. Degrees, -90 to 90 (ignored for Adaptive).</summary>
    public sbyte Angle;
    /// <summary>Only used when Profile == Custom or Adaptive.</summary>
    public float BaseKnockback;
    /// <summary>Only used when Profile == Custom or Adaptive.</summary>
    public float KnockbackGrowth;

    public readonly (sbyte angle, float baseKB, float growthKB) Resolve()
    {
        if (Profile == KnockbackProfile.Custom)
            return (Angle, BaseKnockback, KnockbackGrowth);
        if (Profile == KnockbackProfile.Adaptive)
            return (AdaptiveAngle, BaseKnockback, KnockbackGrowth);

        var p = ProfileTable[(int)Profile];
        return (p.angle, p.baseKB, p.growthKB);
    }

    // ── Profile table — tune these for game-wide feel ──
    private static readonly (sbyte angle, float baseKB, float growthKB)[] ProfileTable = new (sbyte, float, float)[]
    {
        ((sbyte)15,   2f,  1.5f),  // Light
        ((sbyte)15,   8f,  5f),    // Medium
        ((sbyte)25,   8f,  4f),    // Launcher
        ((sbyte)20,  18f, 10f),    // Kill
        ((sbyte)-45, 12f,  4f),    // Spike
        ((sbyte)0,    0f,  0f),    // Custom (unused)
    };
}
