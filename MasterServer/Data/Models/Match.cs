// MasterServer/Data/Models/Match.cs
namespace MasterServer.Data.Models;

public class Match
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Player1SteamId { get; set; }
    public long Player2SteamId { get; set; }
    public long? WinnerSteamId { get; set; } // Null for draws or disconnects
    public string ServerRegion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    // Navigation properties
    public User? Player1 { get; set; }
    public User? Player2 { get; set; }
    public User? Winner { get; set; }
}
