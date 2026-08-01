namespace SlopArena.Shared
{
    /// <summary>User info returned by GET /auth/me.</summary>
    public class GuestUserInfo
    {
        public long SteamId { get; set; }
        public string Username { get; set; } = string.Empty;
        public int Mmr { get; set; }
    }
}
