using System.Globalization;

namespace Sonarr.Domain.Utility;

/// <summary>
/// What a <c>when</c> string turned into. <see cref="RunAt"/> is an instant;
/// <see cref="Recurrence"/> is set only when the user asked for a repeat.
/// </summary>
public sealed record WhenResult(DateTimeOffset RunAt, string? Recurrence, string Echo);

/// <summary>
/// Parses the <c>when</c> argument of <c>/remind</c>, <c>/announce</c> and <c>/timestamp</c>
/// against the user's timezone. Pure and deterministic: <c>now</c> and the zone are arguments,
/// never read from the environment (docs/02-architecture.md determinism boundary).
/// </summary>
/// <remarks>
/// <para>Understood, in order:</para>
/// <list type="bullet">
/// <item>durations — <c>10m</c>, <c>2h30m</c>, <c>3 days</c>, <c>90 seconds</c>, <c>1w</c></item>
/// <item>repeats — <c>every day 9am</c>, <c>every monday 09:00</c>, <c>every hour</c>, <c>every month</c></item>
/// <item>clock times — <c>9am</c>, <c>21:30</c>, <c>tomorrow 8am</c>, <c>tomorrow</c></item>
/// <item>absolute — <c>2026-08-01 18:00</c>, <c>2026-08-01</c> (invariant, the only date order
/// that isn't ambiguous)</item>
/// </list>
/// <para>
/// // ponytail: a hand-rolled parser over these four shapes, not a real NLP date library.
/// Ceiling: no "next friday week", no "in a fortnight", no localized month names. Upgrade path:
/// swap the body for a parser package behind this same signature — every caller goes through
/// <see cref="TryParse"/> and the tests are written against it.
/// </para>
/// </remarks>
public static class WhenParser
{
    /// <summary>Reminders further out than this are almost certainly a typo.</summary>
    public static readonly TimeSpan MaxHorizon = TimeSpan.FromDays(365 * 2);

    /// <summary>Discord needs the interaction answered long before this; anything shorter is noise.</summary>
    public static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Parses <paramref name="when"/> as of <paramref name="now"/> in <paramref name="zone"/>.
    /// </summary>
    /// <param name="result">The schedule when parsing succeeded, otherwise <c>null</c>.</param>
    /// <param name="error">A sentence to show the user as-is when it failed.</param>
    public static bool TryParse(
        string? when,
        DateTimeOffset now,
        TimeZoneInfo zone,
        out WhenResult? result,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(zone);

        result = null;
        var text = Collapse(when);
        if (text.Length == 0)
        {
            error = "Tell me when — something like `10m`, `2h30m`, `tomorrow 9am` or `every monday 09:00`.";
            return false;
        }

        if (!TryInterpret(text, now, zone, out DateTimeOffset runAt, out var recurrence, out var echo))
        {
            error = $"I can't read `{text}` as a time. Try `20m`, `3 days`, `tomorrow 8am`, "
                    + "`every day 09:00` or `2026-08-01 18:00`.";
            return false;
        }

        if (runAt - now < MinDelay)
        {
            error = "That's already gone by — give me a moment in the future.";
            return false;
        }

        if (runAt - now > MaxHorizon)
        {
            error = "That's more than two years out. Pick something closer.";
            return false;
        }

        result = new WhenResult(runAt, recurrence, echo);
        error = string.Empty;
        return true;
    }

    private static bool TryInterpret(
        string text,
        DateTimeOffset now,
        TimeZoneInfo zone,
        out DateTimeOffset runAt,
        out string? recurrence,
        out string echo)
    {
        recurrence = null;
        echo = text;
        runAt = default;

        if (text.StartsWith("every ", StringComparison.Ordinal))
        {
            return TryRepeat(text[6..].Trim(), now, zone, out runAt, ref recurrence);
        }

        if (TryDuration(text, out TimeSpan offset))
        {
            runAt = now + offset;
            return true;
        }

        return TryClock(text, now, zone, out runAt);
    }

    /// <summary><c>every day 9am</c>, <c>every monday 09:00</c>, <c>every hour</c>.</summary>
    private static bool TryRepeat(
        string rest,
        DateTimeOffset now,
        TimeZoneInfo zone,
        out DateTimeOffset runAt,
        ref string? recurrence)
    {
        runAt = default;

        var space = rest.IndexOf(' ', StringComparison.Ordinal);
        var unit = space < 0 ? rest : rest[..space];
        var timePart = space < 0 ? string.Empty : rest[(space + 1)..].Trim();

        recurrence = Jobs.Recurrence.FromWords(unit);
        if (recurrence is null)
        {
            return false;
        }

        if (recurrence == Jobs.Recurrence.Hourly)
        {
            runAt = now.AddHours(1);
            return true;
        }

        // No time given: same clock time as right now, one period out.
        TimeSpan timeOfDay = timePart.Length == 0
            ? TimeZoneInfo.ConvertTime(now, zone).TimeOfDay
            : TryTimeOfDay(timePart, out TimeSpan parsed) ? parsed : TimeSpan.MinValue;

        if (timeOfDay == TimeSpan.MinValue)
        {
            return false;
        }

        DateTime localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        DateTime candidate = localNow.Date + timeOfDay;

        if (Jobs.Recurrence.DayOfWeekFrom(unit) is { } weekday)
        {
            var ahead = ((int)weekday - (int)candidate.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(ahead);
        }

        runAt = ToInstant(candidate, zone);

        // "every monday 9am" typed on a Monday at 10am means next Monday, not five minutes ago.
        if (runAt <= now)
        {
            runAt = Jobs.Recurrence.Next(recurrence, runAt, now, zone) ?? runAt;
        }

        return true;
    }

    /// <summary><c>10m</c>, <c>2h30m</c>, <c>1w</c>, <c>3 days</c>, <c>90 seconds</c>.</summary>
    internal static bool TryDuration(string text, out TimeSpan total)
    {
        total = TimeSpan.Zero;
        var any = false;
        var index = 0;
        var body = text.StartsWith("in ", StringComparison.Ordinal) ? text[3..].Trim() : text;

        while (index < body.Length)
        {
            if (body[index] == ' ')
            {
                index++;
                continue;
            }

            var digitsStart = index;
            while (index < body.Length && char.IsAsciiDigit(body[index]))
            {
                index++;
            }

            if (index == digitsStart)
            {
                return false;
            }

            if (!long.TryParse(
                    body.AsSpan(digitsStart, index - digitsStart),
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                return false;
            }

            while (index < body.Length && body[index] == ' ')
            {
                index++;
            }

            var unitStart = index;
            while (index < body.Length && char.IsAsciiLetter(body[index]))
            {
                index++;
            }

            if (index == unitStart)
            {
                return false;
            }

            if (UnitToTimeSpan(body[unitStart..index], amount) is not { } part)
            {
                return false;
            }

            // Overflow guard: 999999999w would throw out of TimeSpan arithmetic.
            if (part > MaxHorizon || total > MaxHorizon - part)
            {
                total = MaxHorizon + TimeSpan.FromDays(1);
                return true;
            }

            total += part;
            any = true;
        }

        return any && total > TimeSpan.Zero;
    }

    private static TimeSpan? UnitToTimeSpan(string unit, long amount)
    {
        long seconds = unit switch
        {
            "s" or "sec" or "secs" or "second" or "seconds" => 1,
            "m" or "min" or "mins" or "minute" or "minutes" => 60,
            "h" or "hr" or "hrs" or "hour" or "hours" => 3600,
            "d" or "day" or "days" => 86400,
            "w" or "wk" or "wks" or "week" or "weeks" => 7 * 86400,
            // Calendar months would need an anchor date; 30 days is the honest approximation for a
            // duration, and "every month" (recurrence) is the calendar-correct path.
            "mo" or "month" or "months" => 30 * 86400,
            "y" or "yr" or "year" or "years" => 365 * 86400,
            _ => 0,
        };

        if (seconds == 0)
        {
            return null;
        }

        // Multiplied out in long seconds rather than TimeSpan.FromDays: `999999999w` overflows
        // TimeSpan itself, before the caller's horizon check ever gets to run.
        var cap = (long)MaxHorizon.TotalSeconds;
        return amount > cap / seconds
            ? MaxHorizon + TimeSpan.FromDays(1)
            : TimeSpan.FromSeconds(amount * seconds);
    }

    /// <summary><c>9am</c>, <c>21:30</c>, <c>tomorrow 8am</c>, <c>2026-08-01 18:00</c>.</summary>
    private static bool TryClock(string text, DateTimeOffset now, TimeZoneInfo zone, out DateTimeOffset runAt)
    {
        runAt = default;
        DateTime localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;

        var body = text;
        var dayOffset = 0;
        if (body.StartsWith("tomorrow", StringComparison.Ordinal))
        {
            dayOffset = 1;
            body = body[8..].Trim();
        }
        else if (body.StartsWith("today", StringComparison.Ordinal))
        {
            body = body[5..].Trim();
        }
        else if (body.StartsWith("tonight", StringComparison.Ordinal))
        {
            body = body[7..].Trim();
            if (body.Length == 0)
            {
                body = "20:00";
            }
        }
        else if (Jobs.Recurrence.DayOfWeekFrom(FirstWord(body)) is { } weekday)
        {
            var word = FirstWord(body);
            body = body[word.Length..].Trim();
            dayOffset = ((int)weekday - (int)localNow.DayOfWeek + 7) % 7;
            if (dayOffset == 0)
            {
                dayOffset = 7;
            }
        }

        // Absolute date, invariant order only (yyyy-MM-dd [HH:mm]).
        if (dayOffset == 0 && TryAbsolute(body, out DateTime absolute))
        {
            runAt = ToInstant(absolute, zone);
            return true;
        }

        // A bare "tomorrow"/"friday" means 09:00 there — a reminder with no time attached
        // should land in the morning, not at midnight.
        if (body.Length == 0)
        {
            if (dayOffset == 0)
            {
                return false;
            }

            runAt = ToInstant(localNow.Date.AddDays(dayOffset).Add(TimeSpan.FromHours(9)), zone);
            return true;
        }

        if (body.StartsWith("at ", StringComparison.Ordinal))
        {
            body = body[3..].Trim();
        }

        if (!TryTimeOfDay(body, out TimeSpan timeOfDay))
        {
            return false;
        }

        DateTime candidate = localNow.Date.AddDays(dayOffset) + timeOfDay;
        runAt = ToInstant(candidate, zone);

        // "9am" after 9am means tomorrow's 9am — never the past.
        if (dayOffset == 0 && runAt <= now)
        {
            runAt = ToInstant(candidate.AddDays(1), zone);
        }

        return true;
    }

    private static bool TryAbsolute(string body, out DateTime local)
    {
        string[] formats =
        [
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
        ];

        return DateTime.TryParseExact(
            body,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out local);
    }

    /// <summary><c>9am</c>, <c>9:30pm</c>, <c>21:30</c>, <c>0930</c> is not accepted.</summary>
    internal static bool TryTimeOfDay(string text, out TimeSpan timeOfDay)
    {
        timeOfDay = TimeSpan.MinValue;
        var body = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (body.Length == 0)
        {
            return false;
        }

        var meridiem = 0;
        if (body.EndsWith("am", StringComparison.Ordinal))
        {
            meridiem = 1;
            body = body[..^2];
        }
        else if (body.EndsWith("pm", StringComparison.Ordinal))
        {
            meridiem = 2;
            body = body[..^2];
        }

        var colon = body.IndexOf(':', StringComparison.Ordinal);

        // A bare number is ambiguous — "10" could be ten minutes or ten o'clock. Refusing it sends
        // the user back with an example rather than guessing wrong on their behalf. A time needs
        // either a separator (21:30) or a meridiem (9pm).
        if (colon < 0 && meridiem == 0)
        {
            return false;
        }

        var hourText = colon < 0 ? body : body[..colon];
        var minuteText = colon < 0 ? "0" : body[(colon + 1)..];

        if (!int.TryParse(hourText, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(minuteText, CultureInfo.InvariantCulture, out var minute)
            || minute is < 0 or > 59)
        {
            return false;
        }

        switch (meridiem)
        {
            case 1 when hour is < 1 or > 12:
            case 2 when hour is < 1 or > 12:
            case 0 when hour is < 0 or > 23:
                return false;
            case 1:
                hour = hour == 12 ? 0 : hour;
                break;
            case 2:
                hour = hour == 12 ? 12 : hour + 12;
                break;
        }

        timeOfDay = new TimeSpan(hour, minute, 0);
        return true;
    }

    private static string FirstWord(string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? text : text[..space];
    }

    /// <summary>
    /// Local wall-clock to an instant, matching <see cref="Jobs.Recurrence"/>: a nonexistent
    /// spring-forward time moves forward an hour, an ambiguous one takes the first pass.
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

    /// <summary>Lower-cases and squeezes runs of whitespace, so "  2 H 30 M " parses.</summary>
    private static string Collapse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[raw.Length];
        var length = 0;
        var lastWasSpace = false;
        foreach (var c in raw.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    buffer[length++] = ' ';
                    lastWasSpace = true;
                }

                continue;
            }

            buffer[length++] = char.ToLowerInvariant(c);
            lastWasSpace = false;
        }

        return new string(buffer[..length]);
    }
}
