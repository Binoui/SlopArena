using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>
/// Payload of the <c>MatchStarting</c> push broadcast when the host starts.
/// Carries the roster the game server will spawn.
/// </summary>
public sealed record MatchStartingConfig(
    Guid ServerId,
    IReadOnlyList<LobbyPlayerInfo> Players);
