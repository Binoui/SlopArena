// MasterServer/DTOs/ServerRegistrationRequest.cs
namespace MasterServer.DTOs;

public record ServerRegistrationRequest(
    string Name,
    string IpAddress,
    int Port,
    string Region,
    bool IsOfficial,
    int MaxConcurrentMatches,
    string? CustomRulesJson
);
