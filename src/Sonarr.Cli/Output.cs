using System.Globalization;

namespace Sonarr.Cli;

/// <summary>
/// Terminal formatting. Plain text, aligned columns, no box drawing — this output gets piped and
/// grepped, and a table drawn with Unicode corners is a table awk cannot read.
/// </summary>
internal static class Output
{
    /// <summary>A section title, blank line above so sections separate when scrolling.</summary>
    public static void Heading(string text)
    {
        Console.WriteLine();
        Console.WriteLine(text);
        Console.WriteLine(new string('─', text.Length));
    }

    /// <summary>
    /// A right-aligned count against a left-aligned label, columns sized to the widest row so a
    /// long command name does not push everything sideways.
    /// </summary>
    public static void Rows(IEnumerable<(string Label, string Value)> rows)
    {
        List<(string Label, string Value)> all = [.. rows];
        if (all.Count == 0)
        {
            Console.WriteLine("  (nothing)");
            return;
        }

        int width = all.Max(r => r.Label.Length);
        foreach ((string label, string value) in all)
        {
            Console.WriteLine($"  {label.PadRight(width)}  {value}");
        }
    }

    /// <summary>A count with its unit pluralised, because "1 episodes" reads like a bug.</summary>
    public static string Count(int n, string one, string many)
        => $"{n.ToString("N0", CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}";

    /// <summary>
    /// Local time, not UTC. Everything in the database is stored UTC and goal 1 was about times
    /// reading seven hours off, so the CLI converts on the way out and says which zone it used.
    /// </summary>
    public static string Local(DateTimeOffset at)
        => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string LocalDate(DateOnly day)
        => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The zone the timestamps above are in, printed once per command rather than per row.</summary>
    public static string Zone => TimeZoneInfo.Local.Id;

    /// <summary>
    /// A horizontal bar for a value against the largest in its series. Twenty columns: enough to
    /// see the shape of a week, narrow enough to survive an 80-column terminal.
    /// </summary>
    public static string Bar(long value, long max, int width = 20)
    {
        if (max <= 0 || value <= 0)
        {
            return string.Empty;
        }

        int filled = (int)Math.Max(1, Math.Round((double)value / max * width));
        return new string('█', Math.Min(filled, width));
    }
}
