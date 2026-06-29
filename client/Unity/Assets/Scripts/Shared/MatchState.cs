namespace SlopArena.Shared
{
    /// <summary>
    /// Server-authoritative match lifecycle state.
    /// Broadcast to clients so they can show countdown UI, round results, etc.
    /// </summary>
    public enum MatchState : byte
    {
        /// <summary>Waiting for players to connect.</summary>
        Waiting = 0,
        /// <summary>Both players connected, countdown in progress.</summary>
        Countdown = 1,
        /// <summary>Match is live.</summary>
        Playing = 2,
        /// <summary>Match ended — winner declared.</summary>
        Ended = 3,
    }
}
