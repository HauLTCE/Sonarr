namespace Sonarr.Domain.Utility;

/// <summary>What a milestone is: a join anniversary or a birthday.</summary>
public enum MilestoneKind
{
    Anniversary,
    Birthday,
}

/// <summary>One thing worth mentioning about one member today.</summary>
/// <param name="Years">
/// Years being marked, or <c>0</c> when the count is unknown — a birthday stored without a year
/// (docs/04: "month+day used; year optional") has no age to announce.
/// </param>
public sealed record Milestone(MilestoneKind Kind, ulong UserId, int Years);

/// <summary>What <c>/anniversary</c> shows. <see cref="FirstSeenAt"/> is null for a stranger.</summary>
public sealed record AnniversaryCard(
    ulong UserId,
    DateTimeOffset? FirstSeenAt,
    int Years,
    DateOnly? NextOn,
    int DaysUntilNext);

/// <summary>Result of setting or clearing a birthday.</summary>
public sealed record BirthdayResult(bool Success, string Message)
{
    public static BirthdayResult Ok(string message) => new(true, message);

    public static BirthdayResult Rejected(string? reason) => new(false, reason ?? "That didn't work.");
}

/// <summary>
/// The date arithmetic behind <c>/anniversary</c> and the birthday feature. Pure functions over a
/// date the caller resolved (the guild's zone), so a rollover is testable without a clock — same
/// shape as <c>StreakRules</c>.
/// </summary>
public static class MilestoneRules
{
    /// <summary>
    /// Year stored when someone gives only a month and a day. Year 4 because it is a leap year
    /// (so 29 February stores as a real date) and nobody claims it as a birth year, which keeps
    /// "no year given" distinguishable from a real one without a second column.
    /// </summary>
    public const int UnknownYear = 4;

    /// <summary>
    /// Authored lines, one pool per kind. Code-referenced (like <c>ShipMeter.LowPool</c>), so the
    /// persona validator lists them as orphans.
    /// </summary>
    public const string BirthdayPool = "milestone_birthday";

    public const string AnniversaryPool = "milestone_anniversary";

    /// <summary>Discord's own floor, and the ceiling is a typo guard.</summary>
    public const int MinAge = 13;

    public const int MaxAge = 120;

    /// <summary>
    /// Builds the stored date from what the user typed. <paramref name="error"/> is a sentence
    /// that can be shown as-is.
    /// </summary>
    public static bool TryBuildBirthday(
        int month,
        int day,
        int? year,
        DateOnly today,
        out DateOnly birthday,
        out string? error)
    {
        birthday = default;
        error = null;

        if (month is < 1 or > 12)
        {
            error = "Month goes from 1 to 12.";
            return false;
        }

        // Validated against the year they gave, or against the leap-year sentinel so 29 February
        // is allowed when they didn't give one.
        var effectiveYear = year ?? UnknownYear;
        if (day < 1 || day > DateTime.DaysInMonth(effectiveYear, month))
        {
            error = $"{day} isn't a day in month {month}.";
            return false;
        }

        if (year is { } given)
        {
            var age = AgeOn(new DateOnly(given, month, day), today);
            if (age is null or < MinAge or > MaxAge)
            {
                error = $"That birth year would make you {age?.ToString() ?? "an unknown number of"} — try again.";
                return false;
            }
        }

        birthday = new DateOnly(effectiveYear, month, day);
        return true;
    }

    /// <summary>Whether a stored birthday carries a real birth year.</summary>
    public static bool HasYear(DateOnly birthday) => birthday.Year != UnknownYear;

    /// <summary>Age on <paramref name="on"/>, or null when the year is the sentinel.</summary>
    public static int? AgeOn(DateOnly birthday, DateOnly on)
        => HasYear(birthday) ? YearsSince(birthday, on) : null;

    /// <summary>
    /// Whole years between the two dates, never negative. A date later in the year than
    /// <paramref name="on"/> has not come round yet, so it counts one less.
    /// </summary>
    public static int YearsSince(DateOnly since, DateOnly on)
    {
        var years = on.Year - since.Year;
        if (on < since.AddYears(years))
        {
            years--;
        }

        return Math.Max(years, 0);
    }

    /// <summary>
    /// The next time this month+day comes round, counting today itself. 29 February moves to
    /// 1 March in a common year — the alternative is skipping three years out of four.
    /// </summary>
    public static DateOnly NextOccurrence(int month, int day, DateOnly on)
    {
        DateOnly thisYear = OnOrAfter(on.Year, month, day);
        return thisYear >= on ? thisYear : OnOrAfter(on.Year + 1, month, day);
    }

    /// <summary>
    /// Does <paramref name="anchor"/>'s month+day fall on <paramref name="today"/>, allowing for
    /// 29 February landing on 1 March in a common year?
    /// </summary>
    public static bool FallsOn(DateOnly anchor, DateOnly today)
        => NextOccurrence(anchor.Month, anchor.Day, today) == today;

    /// <summary>
    /// True when <paramref name="today"/> is the day a 29 February date has to be observed on,
    /// i.e. 1 March in a year that has no 29 February. Callers use it to widen a month+day query.
    /// </summary>
    public static bool ObservesLeapDay(DateOnly today)
        => today is { Month: 3, Day: 1 } && !DateTime.IsLeapYear(today.Year);

    /// <summary>
    /// The month+day in <paramref name="year"/>, rolling <em>forward</em> when that year is short:
    /// 29 February becomes 1 March, not 28 February. Counting from the first of the month does the
    /// rolling, so there is no special case for it.
    /// </summary>
    private static DateOnly OnOrAfter(int year, int month, int day)
        => new DateOnly(year, month, 1).AddDays(day - 1);
}
