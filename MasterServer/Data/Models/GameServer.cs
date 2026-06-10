// MasterServer/Data/Models/GameServer.cs
namespace MasterServer.Data.Models;

public class GameServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Region { get; set; } = string.Empty;
    public bool IsOfficial { get; set; }
    public int MaxConcurrentMatches { get; set; }
    public int CurrentMatches { get; set; }
    public string? CustomRulesJson { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public string ApiToken { get; set; } = string.Empty; // For auth
}
