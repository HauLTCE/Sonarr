using Sonarr.Domain.Utility;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>/birthday</c> and <c>/anniversary</c> (docs/07-commands.md#utility), plus the day's list for
/// the announcer. Birthdays are opt-in by command only (docs/06); anniversaries need no opt-in —
/// <c>core.member.first_seen_at</c> is written when Sonarr first sees someone, not volunteered.
/// </summary>
public interface IMilestoneService
{
    /// <summary>Stores a month+day, with the year optional. Rejections carry a showable sentence.</summary>
    Task<BirthdayResult> SetBirthdayAsync(
        ulong guildId,
        ulong userId,
        int month,
        int day,
        int? year,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets the stored birthday. Always succeeds, stored or not.</summary>
    Task<BirthdayResult> ClearBirthdayAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default);

    /// <summary>What <c>/anniversary</c> shows, in the guild's timezone.</summary>
    Task<AnniversaryCard> GetAnniversaryAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everyone in the guild with a birthday or a join anniversary on the guild's today. Empty when
    /// there is nothing to say, which is most days.
    /// </summary>
    Task<IReadOnlyList<Milestone>> TodayAsync(
        ulong guildId,
        CancellationToken cancellationToken = default);

    /// <summary>Her authored line for a milestone, or empty when the persona has no pool.</summary>
    string Line(Milestone milestone);

    /// <summary>
    /// Now, in the guild's configured timezone. The announcer needs it to decide whether the
    /// guild has reached its greeting hour, and the zone resolver it comes from is internal.
    /// </summary>
    Task<DateTimeOffset> LocalNowAsync(ulong guildId, CancellationToken cancellationToken = default);
}
