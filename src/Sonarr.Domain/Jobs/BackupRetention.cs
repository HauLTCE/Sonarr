using System.Globalization;

namespace Sonarr.Domain.Jobs;

/// <summary>One backup file on disk, as the retention policy sees it.</summary>
/// <param name="Path">Path exactly as it was found, so the caller can delete it.</param>
/// <param name="Prefix">The <c>sonarr</c> / <c>config</c> half of the name — retention is per set.</param>
/// <param name="Day">The date in the name, not the file's mtime: a copied tree keeps its meaning.</param>
public sealed record BackupFile(string Path, string Prefix, DateOnly Day);

/// <summary>
/// The naming and retention rules of docs/11 — "dailies 30 days, then first-of-month kept 12
/// months" — as a pure function, because the thing that acts on its answer is a delete loop.
/// </summary>
/// <remarks>
/// <para>One rule covers both sets: past the daily window, keep the <b>earliest surviving file of
/// each calendar month</b>. For nightly dumps that is the 1st, which is what docs/11 says. For the
/// weekly config archive it is whichever week landed first — the literal "first-of-month" reading
/// would delete almost every config backup, since a weekly job rarely runs on the 1st.</para>
/// <para><see cref="Parse"/> returns null for anything it does not recognise, and
/// <see cref="Expired"/> only ever names files it parsed. A stray file in the backup tree is
/// therefore left alone rather than deleted by a rule that was never written with it in mind.</para>
/// </remarks>
public static class BackupRetention
{
    /// <summary>Nightly <c>pg_dump</c> output.</summary>
    public const string DumpPrefix = "sonarr";

    /// <summary>Weekly persona + <c>.env</c> archive.</summary>
    public const string ConfigPrefix = "config";

    /// <summary>Custom-format dump. Not <c>.gz</c> — see <c>BackupRunner</c> for why.</summary>
    public const string DumpExtension = ".dump";

    public const string ConfigExtension = ".tar.gz";

    /// <summary>Every daily is kept this long, whatever day of the month it is.</summary>
    public static readonly TimeSpan DailyWindow = TimeSpan.FromDays(30);

    /// <summary>How many months of monthlies survive the window.</summary>
    public const int MonthsKept = 12;

    /// <summary>
    /// Where a backup for <paramref name="day"/> goes, relative to the backup root:
    /// <c>YYYY/MM/prefix-YYYY-MM-DD.ext</c> (docs/11 — foldered by year/month, one `scp -r` to move).
    /// </summary>
    public static string RelativePath(string prefix, DateOnly day, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{day.Year:D4}/{day.Month:D2}/{prefix}-{day:yyyy-MM-dd}{extension}");
    }

    /// <summary>
    /// Reads the prefix and date back out of a file name, or null if it is not one of ours. The
    /// extension must be one we write and the name must end in a date, so
    /// <c>config-2026-07-05.tar.gz</c> and <c>sonarr-2026-07-05.dump</c> parse while
    /// <c>sonarr-latest.dump</c> and a half-written <c>....dump.partial</c> do not.
    /// </summary>
    public static BackupFile? Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Not Path.GetFileName: on Linux `\` is a legal filename character, so a Windows-shaped
        // path comes back whole and the prefix ends up "C:\backups\2026\07\sonarr". Splitting on
        // both separators parses either shape on either OS, which is what a name written on one
        // box and pruned on another needs.
        string name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

        // Matching the extension rather than "everything before the first dot" is what keeps a
        // `.partial` invisible: the prune must never delete the dump still being written.
        string? extension = new[] { DumpExtension, ConfigExtension }
            .FirstOrDefault(e => name.EndsWith(e, StringComparison.Ordinal));

        if (extension is null)
        {
            return null;
        }

        string stem = name[..^extension.Length];
        if (stem.Length < 12 || stem[^11] != '-')
        {
            return null;
        }

        return DateOnly.TryParseExact(
                stem[^10..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateOnly day)
            ? new BackupFile(path, stem[..^11], day)
            : null;
    }

    /// <summary>
    /// Which of <paramref name="files"/> may be deleted as of <paramref name="today"/>. Anything not
    /// named is kept, so a caller that deletes exactly this set can never over-delete.
    /// </summary>
    public static IReadOnlyList<BackupFile> Expired(
        IEnumerable<BackupFile> files, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(files);

        // Grouped per set and per month: the earliest config archive of a month survives even if
        // that month also holds 30 dumps, and vice versa.
        return
        [
            .. files
                .GroupBy(f => (f.Prefix, f.Day.Year, f.Day.Month))
                .SelectMany(month =>
                {
                    // Over the whole month, not just its expired files — otherwise a month
                    // straddling the window boundary would keep both the 1st and the oldest
                    // file still inside it.
                    DateOnly earliest = month.Min(f => f.Day);
                    return month.Where(f => !Keep(f, earliest, today));
                }),
        ];
    }

    private static bool Keep(BackupFile file, DateOnly earliestInMonth, DateOnly today)
    {
        if (today.DayNumber - file.Day.DayNumber <= DailyWindow.TotalDays)
        {
            return true;
        }

        int monthsOld = ((today.Year - file.Day.Year) * 12) + today.Month - file.Day.Month;
        return file.Day == earliestInMonth && monthsOld <= MonthsKept;
    }
}
