// MasterServer/DTOs/MatchResultRequest.cs
namespace MasterServer.DTOs;

public record MatchResultRequest(Guid MatchId, long WinnerSteamId);
