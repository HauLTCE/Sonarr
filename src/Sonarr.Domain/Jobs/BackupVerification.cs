using System.Text;

namespace Sonarr.Domain.Jobs;

/// <summary>
/// "Is this dump restorable-shaped?" (docs/08 — non-zero plus a header check). Pure, because the
/// caller's next move on a bad answer is to delete the file and go red.
/// </summary>
/// <remarks>
/// Shape, not content: proving a dump restores means restoring it, which is the quarterly drill in
/// docs/11, not something a nightly job can do on a 20 GB disk. What this catches is the failure
/// that actually happens — <c>pg_dump</c> exiting mid-write, leaving a truncated or empty file that
/// looks like a backup in a directory listing and is worthless at 3 a.m. six months later.
/// </remarks>
public static class BackupVerification
{
    /// <summary>
    /// Every <c>pg_dump -Fc</c> archive starts with this. A plain-SQL dump or a gzip wrapper does
    /// not, so this also catches "someone changed the format flag and the restore drill still says
    /// <c>pg_restore</c>".
    /// </summary>
    public static readonly byte[] CustomFormatMagic = Encoding.ASCII.GetBytes("PGDMP");

    /// <summary>
    /// Smallest plausible dump. An empty schema still writes a header and a table-of-contents, so
    /// anything under this is a truncated write rather than a quiet database.
    /// </summary>
    public const long MinimumBytes = 1024;

    /// <summary>
    /// Null when the file looks restorable, otherwise the clause to put in the red line.
    /// </summary>
    public static string? Check(long bytes, ReadOnlySpan<byte> header)
    {
        if (bytes < MinimumBytes)
        {
            return $"only {bytes} byte(s) — pg_dump wrote a truncated file";
        }

        return header.StartsWith(CustomFormatMagic)
            ? null
            : "not a pg_dump custom-format archive (no PGDMP header)";
    }
}
