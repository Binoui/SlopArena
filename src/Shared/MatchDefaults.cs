namespace SlopArena.Shared;

/// <summary>
/// Shared match defaults so the lobby codec, match-start request, game server
/// rule and client HUD agree on one value instead of duplicated literals
/// (issue #38). Wire DTO defaults must stay compile-time constants.
/// </summary>
public static class MatchDefaults
{
    /// <summary>Stocks per player when the producer omits maxStocks (ADR-0007).</summary>
    public const int DefaultMaxStocks = 3;
}
