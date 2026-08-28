namespace Sonarr.Iris.Logging;

/// <summary>
/// The two Serilog output templates, in one place so the bootstrap logger and the real one cannot
/// print differently — a difference there reads as a clock jump across the first few lines.
/// </summary>
/// <remarks>
/// Both carry the date and the UTC offset. The console template used to be bare
/// <c>HH:mm:ss</c>, which made a container running UTC while its host ran +07 look like a
/// seven-hour-wrong clock rather than a missing <c>TZ</c> — the offset is what tells those apart,
/// and it costs six characters a line.
/// <para>
/// Iris owns these because it also reads the result: <c>sonarr logs</c> parses the file shape, so
/// the template and its reader live in the same module and change together.
/// </para>
/// </remarks>
public static class LogTemplates
{
    /// <summary>What <c>journalctl -u sonarr</c> / the terminal shows.</summary>
    public const string Console =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss zzz} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    /// <summary>What <c>logs/sonarr-*.log</c> holds — round-trip ISO 8601, offset included.</summary>
    public const string File =
        "{Timestamp:o} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";
}
