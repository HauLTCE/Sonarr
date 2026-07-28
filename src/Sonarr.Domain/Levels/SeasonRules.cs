using Sonarr.Domain.Entities.Levels;

namespace Sonarr.Domain.Levels;

/// <summary>
/// One member's position going into a close: their lifetime XP, and how much of it earlier seasons
/// already claimed.
/// </summary>
/// <remarks>
/// <c>levels.progress.xp</c> is a lifetime total and is never reset — a season reset would throw
/// away the level everyone earned. So "XP earned this season" is the part of the total no closed
/// season has counted yet, which is exactly <c>total - sum(previous xp_earned)</c>. That keeps the
/// season columns honest without a per-season XP table or a snapshot row at open time.
/// </remarks>
public sealed record SeasonStanding(long UserId, long TotalXp, long AccountedXp);

/// <summary>
/// The month-boundary arithmetic behind <c>SeasonRoller</c>: which window a season covers, whether
/// it is over, and who placed where. Pure — the roller supplies the guild's local clock.
/// </summary>
public static class SeasonRules
{
    /// <summary>Rows in the "top chatter" announcement. A podium, not a leaderboard.</summary>
    public const int TopCount = 3;

    /// <summary>Persona pool for the close announcement.</summary>
    public const string ClosePool = "season_close";

    /// <summary>
    /// The calendar month <paramref name="localNow"/> falls in: first instant of this month to the
    /// first instant of the next.
    /// </summary>
    /// <remarks>
    /// ponytail: both ends carry <paramref name="localNow"/>'s offset, so a window spanning a DST
    /// change is an hour out at one end. That only decides whether a roll happens at 23:00 or
    /// 00:00 on the boundary day, and the poll interval is coarser than the error; storing the
    /// guild's IANA id on the season row is the fix if a season ever needs to be exact.
    /// </remarks>
    public static (DateTimeOffset Starts, DateTimeOffset Ends) MonthWindow(DateTimeOffset localNow)
    {
        DateTimeOffset starts = new(localNow.Year, localNow.Month, 1, 0, 0, 0, localNow.Offset);
        return (starts, starts.AddMonths(1));
    }

    /// <summary>Inclusive of the boundary: the month ends the instant the next one starts.</summary>
    public static bool IsOver(DateTimeOffset endsAt, DateTimeOffset localNow) => localNow >= endsAt;

    /// <summary>"July 2026" — what the announcement and the autocomplete label call the season.</summary>
    public static string Label(DateTimeOffset startsAt) => startsAt.ToString("MMMM yyyy", null);

    /// <summary>
    /// The frozen standings, best first. Members who earned nothing this season are left out
    /// entirely rather than stored at rank N with 0 XP — a season result is a record of taking
    /// part, and the row would outlive the membership for no reason (docs/06).
    /// </summary>
    public static IReadOnlyList<SeasonResult> Rank(IEnumerable<SeasonStanding> standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        return
        [
            .. standings
                // Clamped: an admin XP reset can leave a total below what earlier seasons counted,
                // and a negative "earned" would sort a wiped member to the bottom of the podium
                // instead of off it.
                .Select(s => (s.UserId, Earned: Math.Max(s.TotalXp - s.AccountedXp, 0)))
                .Where(s => s.Earned > 0)
                // Ties break on the id so two equal totals rank in a stable order rather than
                // whatever the database returned.
                .OrderByDescending(s => s.Earned)
                .ThenBy(s => s.UserId)
                .Select((s, i) => new SeasonResult
                {
                    UserId = s.UserId,
                    XpEarned = s.Earned,
                    Rank = i + 1,
                }),
        ];
    }
}
