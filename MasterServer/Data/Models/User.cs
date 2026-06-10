// MasterServer/Data/Models/User.cs
namespace MasterServer.Data.Models;

public class User
{
    public long SteamId { get; set; } // Primary key
    public string Username { get; set; } = string.Empty;
    public int Mmr { get; set; } = 1000;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }
}
