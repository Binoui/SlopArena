using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>
/// Full lobby membership snapshot pushed on any change via <c>LobbyUpdated</c>.
/// </summary>
/// <param name="ServerId">Game server the lobby belongs to.</param>
/// <param name="Players">Ordered player list; index 0 is the host.</param>
public sealed record LobbySnapshot(
    Guid ServerId,
    IReadOnlyList<LobbyPlayerInfo> Players);
