namespace Sonarr.Domain.Jobs;

/// <summary>
/// The canonical <c>core.job.recurrence</c> vocabulary: <c>hourly</c>, <c>daily</c>,
/// <c>weekly</c>, <c>monthly</c>. The <b>time of day</b> lives in <c>run_at</c>, not here — the
/// next occurrence is computed from the row's own schedule, so "every monday 9am" needs no
/// second field and <c>/reminders cancel</c> stays a single-row operation.
/// </summary>
/// <remarks>
/// Deliberately not cron. The user-facing surface is "every day / every monday / every hour /
/// every month" (docs/07-commands.md), and a cron parser would be a dependency plus a whole
/// class of unreadable schedules to support.
/// </remarks>
public static class Recurrence
{
    public const string Hourly = "hourly";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static IReadOnlyList<string> All { get; } = [Hourly, Daily, Weekly, Monthly];

    public static bool IsValid(string? recurrence) => All.Contains(recurrence);

    /// <summary>How the schedule reads in a <c>/reminders list</c> line.</summary>
    public static string Describe(string? recurrence) => recurrence switch
    {
        Hourly => "every hour",
        Daily => "every day",
        Weekly => "every week",
        Monthly => "every month",
        _ => "once",
    };

    /// <summary>
    /// First occurrence at or after <paramref name="from"/> that keeps
    /// <paramref name="previous"/>'s local time-of-day. Steps in calendar units inside
    /// <paramref name="zone"/>, so a daily 9am reminder stays at 9am across a DST shift instead
    /// of sliding to 8am.
    /// </summary>
    /// <returns><c>null</c> when <paramref name="recurrence"/> is not a recurring schedule.</returns>
    public static DateTimeOffset? Next(
        string? recurrence,
        DateTimeOffset previous,
        DateTimeOffset from,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (!IsValid(recurrence))
        {
            return null;
        }

        // Hourly has no local-time anchor to protect — one hour is one hour in every zone.
        if (recurrence == Hourly)
        {
            DateTimeOffset next = previous;
            do
            {
                next = next.AddHours(1);
            }
            while (next <= from);

            return next;
        }

        DateTime local = TimeZoneInfo.ConvertTime(previous, zone).DateTime;
        var step = 0;
        DateTimeOffset candidate;
        do
        {
            step++;
            // AddMonths clamps the 31st to the last day of a shorter month — the boring answer.
            DateTime advanced = recurrence switch
            {
                Daily => local.AddDays(step),
                Weekly => local.AddDays(7 * step),
                _ => local.AddMonths(step),
            };

            candidate = ToInstant(advanced, zone);
        }
        // Bounded so a clock jumped years forward can't spin: 400 steps is >1 year of days.
        while (candidate <= from && step < 400);

        return candidate;
    }

    /// <summary>
    /// Local wall-clock to an instant. A time that does not exist (spring-forward gap) is pushed
    /// forward by the gap; an ambiguous time (autumn repeat) takes the first pass.
    /// </summary>
    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    /// <summary>
    /// Maps the words a user actually types onto the vocabulary. Returns <c>null</c> when the
    /// text is not a recurrence.
    /// </summary>
    public static string? FromWords(string? words)
    {
        var text = words?.Trim().ToLowerInvariant() ?? string.Empty;
        return text switch
        {
            "hour" or "hourly" => Hourly,
            "day" or "daily" => Daily,
            "week" or "weekly" => Weekly,
            "month" or "monthly" => Monthly,
            _ => DayOfWeekFrom(text) is not null ? Weekly : null,
        };
    }

    /// <summary>Weekday name or three-letter abbreviation, invariant — <c>null</c> if neither.</summary>
    public static DayOfWeek? DayOfWeekFrom(string? word)
    {
        var text = word?.Trim().ToLowerInvariant() ?? string.Empty;
        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            var name = day.ToString().ToLowerInvariant();
            if (text == name || (text.Length == 3 && name.StartsWith(text, StringComparison.Ordinal)))
            {
                return day;
            }
        }

        return null;
    }
}
