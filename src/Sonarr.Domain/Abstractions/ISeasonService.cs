using Sonarr.Domain.Levels;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// The month boundary for one guild (docs/08 <c>SeasonRoller</c>): close the season that ended,
/// freeze its standings, open the next one.
/// </summary>
/// <remarks>
/// Behind an interface for the same reason as capsules, events and milestones: the implementation
/// needs the guild's timezone and <c>ZoneResolver</c> is internal to the application assembly.
/// </remarks>
public interface ISeasonService
{
    /// <summary>
    /// Brings the guild's seasons up to date with its own calendar. Idempotent and safe to call on
    /// any tick: it opens the current month if nothing is open, and closes what has ended.
    /// </summary>
    /// <returns>
    /// The close worth announcing, or <c>null</c> when nothing closed — the common case, since this
    /// runs on a timer and a month boundary happens once.
    /// </returns>
    Task<SeasonRoll?> RollAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Her line for the close announcement.</summary>
    string Line(SeasonRoll roll);
}

/// <summary>
/// A season that just closed and the podium it froze. <paramref name="Top"/> is empty when the month
/// went by with nobody earning XP — still worth a line, since the season did happen.
/// </summary>
public sealed record SeasonRoll(
    long SeasonId,
    string Label,
    int Participants,
    IReadOnlyList<LeaderboardEntry> Top);
