using System;

namespace SlopArena.Shared
{
    /// <summary>User info returned by GET /auth/me and PUT /auth/name.</summary>
    public class GuestUserInfo
    {
        public long SteamId { get; set; }
        public string PlayerId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public int Mmr { get; set; }
        public string SessionTag { get; set; } = string.Empty;
    }

    /// <summary>JWT and identity metadata returned by guest auth and renewal.</summary>
    public class GuestAuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public long SteamId { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
