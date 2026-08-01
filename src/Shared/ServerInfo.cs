using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// A game server entry in the server browser list (GET /servers).
    /// </summary>
    public class ServerInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Region { get; set; } = string.Empty;
        public int CurrentMatches { get; set; }
        public int MaxConcurrentMatches { get; set; }
        public bool IsOfficial { get; set; }
    }
}
