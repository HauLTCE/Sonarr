using System.Text;
using System.Text.RegularExpressions;

namespace Sonarr.Iris.Logging;

/// <summary>
/// Reading the rolling files <see cref="IrisLogging"/> writes — tailing, filtering by level,
/// finding the newest day. Pure helpers over files so the command stays dispatch and the rules
/// stay testable.
/// </summary>
/// <remarks>
/// The file template is <see cref="LogTemplates.File"/>: an ISO round-trip timestamp, then
/// <c>[WRN]</c>-shaped level, then source context. Every rule here is a function of that shape,
/// which is why the template and this reader share a module.
/// </remarks>
public static partial class LogFile
{
    /// <summary>The levels <c>--errors</c> keeps: warning and everything worse.</summary>
    private static readonly string[] Elevated = [" [WRN] ", " [ERR] ", " [FTL] "];

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}")]
    private static partial Regex RecordStart();

    // The glob alone would also match a foreign sonarr-something.log, and "latest" sorts above
    // every date in ordinal order — so the reader pins what the sink writes: prefix + 8 digits.
    [GeneratedRegex(@"^sonarr-\d{8}\.log$")]
    private static partial Regex DailyFileName();

    /// <summary>
    /// True for the first line of a log record. Continuation lines (exception stacks, wrapped
    /// text) start with anything else, so they belong to the record above them.
    /// </summary>
    public static bool IsRecordStart(string line) => RecordStart().IsMatch(line);

    /// <summary>True when the line is a record at warning level or worse.</summary>
    public static bool IsElevated(string line)
        => Elevated.Any(marker => line.Contains(marker, StringComparison.Ordinal));

    /// <summary>
    /// The warning-and-above records, each with its continuation lines. Streaming: a big day's
    /// file is scanned, never loaded.
    /// </summary>
    public static IReadOnlyList<string> FilterElevated(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<string> kept = [];
        bool keeping = false;

        foreach (string line in lines)
        {
            if (IsRecordStart(line))
            {
                keeping = IsElevated(line);
            }

            if (keeping)
            {
                kept.Add(line);
            }
        }

        return kept;
    }

    /// <summary>
    /// The newest daily file, or null when the directory holds none. The file name carries the
    /// date (<c>sonarr-20260828.log</c>), so ordinal name order is chronological order — no
    /// reliance on mtimes, which a copied tree would lose.
    /// </summary>
    public static FileInfo? Newest(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!directory.Exists)
        {
            return null;
        }

        return directory
            .EnumerateFiles($"{IrisLogging.FilePrefix}*.log")
            .Where(f => DailyFileName().IsMatch(f.Name))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// The last <paramref name="count"/> lines, read backwards in chunks so a multi-megabyte
    /// day's file costs a few kilobytes of I/O, not a full read.
    /// </summary>
    /// <remarks>
    /// Opens with <see cref="FileShare.ReadWrite"/> because the bot is writing to today's file
    /// while this reads it.
    /// </remarks>
    public static IReadOnlyList<string> Tail(FileInfo file, int count)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (count <= 0)
        {
            return [];
        }

        const int chunkSize = 8 * 1024;

        using FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long position = stream.Length;
        if (position == 0)
        {
            return [];
        }

        byte[] chunk = new byte[chunkSize];
        List<byte[]> parts = [];
        int newlines = 0;

        while (position > 0)
        {
            int size = (int)Math.Min(chunkSize, position);
            position -= size;
            stream.Seek(position, SeekOrigin.Begin);
            int read = stream.Read(chunk, 0, size);
            byte[] copy = new byte[read];
            Array.Copy(chunk, copy, read);
            parts.Insert(0, copy);

            newlines += copy.Count(b => b == (byte)'\n');
            if (newlines > count)
            {
                break;
            }
        }

        string text = Encoding.UTF8.GetString([.. parts.SelectMany(p => p)]);
        List<string> lines = [.. text.Replace("\r", string.Empty).Split('\n')];

        // The file ends with a newline, so the split left an empty tail.
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        // Stopping early means the first line was cut mid-way; drop it.
        if (position > 0 && lines.Count > 0)
        {
            lines.RemoveAt(0);
        }

        return [.. lines.TakeLast(count)];
    }
}
