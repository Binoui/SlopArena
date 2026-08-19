using System;
using System.Linq;

namespace SlopArena.Shared;

/// <summary>
/// Named knockback-tuning profiles (issue #149). One table shared by the diagnostic tools
/// (MoveDataReport --kbm, AbDiffReport --base/--cand) and the tests, so an A/B diff always
/// compares the same tuning vocabulary. Applying a profile mutates the sim's global KB
/// knobs (<see cref="Simulation.HitstunStunCoefficient"/> / <see cref="Simulation.KbScaleFactor"/>
/// / <see cref="Simulation.HitstunMagBonus"/>); "base" is the shipped tuning (the knobs'
/// static initializers), the rest are lab candidates for feel comparison.
/// </summary>
public static class TuningProfiles
{
    /// <summary>One named tuning. Description documents the hitstun formula it produces.</summary>
    public readonly record struct Profile(string Name, string Description, float StunCoeff, float KbScale, float MagBonus);

    public static readonly Profile[] Profiles =
    {
        new("base",       "shipped (melee-soft, #149) — stun 0.45×mag, KV×0.17", 0.45f, 0.17f, 0f),
        new("old",        "pre-adoption — stun 0.5×mag, KV×0.14",        0.5f, 0.14f, 0f),
        new("stunx18",    "hitstun ×1.8 — stun 0.9×mag, KV×0.14",        0.9f, 0.14f, 0f),
        new("kv70",       "travel −50% — stun 0.5×mag, KV×0.10",         0.5f, 0.10f, 0f),
        new("stun16kv11", "Melee-ish ratio — stun 0.8×mag, KV×0.11",     0.8f, 0.11f, 0f),
        new("floor30",    "Melee +30 floor — stun 0.5×(mag+30), KV×0.14", 0.5f, 0.14f, 30f),
        // Melee-shaped family (feel pass 2026-08-18): hitstun = 0.4×mag (Melee coefficient,
        // no floor) so 0% hits barely stun (no true combos at 0%) and combos emerge with %;
        // KV raised ~1.7× so launches read as a pop (≈ run-speed order) instead of a glide.
        new("melee",      "Melee shape — stun 0.4×mag, KV×0.19",        0.4f, 0.19f, 0f),
        new("melee-hot",  "Melee shape, hotter — stun 0.4×mag, KV×0.22", 0.4f, 0.22f, 0f),
        new("melee-soft", "Melee shape, softer — stun 0.45×mag, KV×0.17", 0.45f, 0.17f, 0f),
    };

    /// <summary>Apply a profile by name (case-insensitive). False when unknown — nothing is mutated.</summary>
    public static bool TryApply(string name)
    {
        var p = Find(name);
        if (p == null) return false;
        Apply(p.Value);
        return true;
    }

    /// <summary>Apply a profile by name; throws <see cref="ArgumentException"/> when unknown.</summary>
    public static void Apply(string name)
    {
        var p = Find(name) ?? throw new ArgumentException(
            $"unknown tuning profile '{name}' (expected one of: {string.Join(", ", Profiles.Select(x => x.Name))})");
        Apply(p);
    }

    /// <summary>Human-readable summary of what a profile does (for tool log lines).</summary>
    public static string Describe(string name)
    {
        var p = Find(name);
        return p != null
            ? $"{p.Value.Name} — {p.Value.Description}"
            : $"unknown profile '{name}'";
    }

    private static void Apply(in Profile p)
    {
        Simulation.HitstunStunCoefficient = p.StunCoeff;
        Simulation.KbScaleFactor = p.KbScale;
        Simulation.HitstunMagBonus = p.MagBonus;
    }

    private static Profile? Find(string name)
    {
        foreach (var p in Profiles)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }
}
