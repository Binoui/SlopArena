using System.Text.RegularExpressions;

namespace SlopArena.Shared;

/// <summary>
/// Parses the embedded game-server subprocess's stdout/stderr to detect the
/// moment it has registered with the master server (ADR-0005, issue #39).
///
/// The server logs the assigned <c>server_id</c> GUID to stdout on a
/// successful <c>POST /servers/register</c>. Two known line shapes carry it:
/// <list type="bullet">
/// <item><c>Registered successfully (Server ID: &lt;guid&gt;).</c> — from <c>Program.cs</c></item>
/// <item><c>[Registration] Registered as '&lt;name&gt;' (ID: &lt;guid&gt;, IP: &lt;ip&gt;)</c> — from <c>GameServerRegistration</c></item>
/// </list>
/// This extracts the GUID generically so log wording changes don't break the
/// host flow. Pure and Unity-free so it is unit-testable.
/// </summary>
public static class ServerLogParser
{
    private static readonly Regex s_guid = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Try to extract a registration server-id GUID from one stdout/stderr
    /// line. Returns true and sets <paramref name="serverId"/> on the first
    /// GUID found; false if the line carries none.
    /// </summary>
    public static bool TryParseServerId(string line, out Guid serverId)
    {
        serverId = Guid.Empty;
        if (string.IsNullOrEmpty(line))
            return false;

        var match = s_guid.Match(line);
        if (!match.Success)
            return false;

        return Guid.TryParse(match.Value, out serverId);
    }
}
