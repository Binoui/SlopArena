using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>
/// Stock-mode match rules for 2-4 players (ADR-0007, issue #36).
/// Deaths is the stock counter: a player with Deaths >= maxDeaths is
/// eliminated (spectator). Last player standing wins.
/// </summary>
public static class MatchRules
{
    /// <summary>True once the player has lost all stocks and is spectating.</summary>
    public static bool IsEliminated(CharacterState state, byte maxDeaths)
        => state.Deaths >= maxDeaths;

    /// <summary>
    /// Winner of a stock match, or null while the match continues.
    /// Exactly one non-eliminated player → that player wins.
    /// Zero non-eliminated players (simultaneous last-stock trade) → the
    /// player with the fewest deaths, ties broken by lowest entity ID.
    /// </summary>
    public static ulong? FindWinner(IReadOnlyDictionary<ulong, CharacterState> states, byte maxDeaths)
    {
        ulong? soleSurvivor = null;
        int alive = 0;
        foreach (var (id, st) in states)
        {
            if (st.Deaths < maxDeaths)
            {
                alive++;
                soleSurvivor = id;
            }
        }
        if (alive == 1) return soleSurvivor;
        if (alive == 0)
        {
            ulong best = ulong.MaxValue;
            byte bestDeaths = byte.MaxValue;
            foreach (var (id, st) in states)
            {
                if (st.Deaths < bestDeaths || (st.Deaths == bestDeaths && id < best))
                {
                    bestDeaths = st.Deaths;
                    best = id;
                }
            }
            return best == ulong.MaxValue ? null : best;
        }
        return null;
    }
}
