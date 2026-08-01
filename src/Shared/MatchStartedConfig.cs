using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>
/// Payload of the <see cref="LobbyClient.MatchStarted"/> push broadcast when
/// the host starts the actual match from char select (ADR-0008, issue #34).
/// Carries the final roster with character classes the game server will spawn.
/// </summary>
public sealed record MatchStartedConfig(
    Guid ServerId,
    IReadOnlyList<LobbyPlayerInfo> Players);
