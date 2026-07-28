using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;

namespace Sonarr.Domain.Abstractions;

/// <summary>levels.season / levels.season_result access.</summary>
public interface ISeasonRepository
{
    /// <summary>The guild's seasons, newest first — feeds the <c>/leaderboard</c> autocomplete.</summary>
    Task<IReadOnlyList<Season>> GetAllAsync(long guildId, CancellationToken ct = default);

    Task<Season?> GetAsync(long seasonId, CancellationToken ct = default);

    /// <summary>The open season, if the guild has one.</summary>
    Task<Season?> GetActiveAsync(long guildId, CancellationToken ct = default);

    /// <summary>Frozen standings for a closed season, best rank first.</summary>
    Task<IReadOnlyList<LeaderboardEntry>> GetResultsAsync(
        long seasonId,
        int skip,
        int take,
        CancellationToken ct = default);

    Task<int> CountResultsAsync(long seasonId, CancellationToken ct = default);

    /// <summary>
    /// Every member with XP in the guild, paired with how much of it earlier closed seasons already
    /// counted. Feeds <see cref="SeasonRules.Rank"/> at close time.
    /// </summary>
    Task<IReadOnlyList<SeasonStanding>> GetStandingsAsync(long guildId, CancellationToken ct = default);

    /// <summary>
    /// Writes the frozen standings and flips the season to <c>closed</c>, in one transaction: a
    /// season marked closed with no results would strand a month's standings with nothing to
    /// recompute them from.
    /// </summary>
    /// <returns>
    /// <c>false</c> when the season was not open — two rollers racing means the second one is a
    /// no-op, not a second set of results.
    /// </returns>
    Task<bool> CloseAsync(
        long seasonId,
        IReadOnlyList<SeasonResult> results,
        CancellationToken ct = default);

    /// <summary>Opens a season for the window and returns it. The caller guarantees no other is open.</summary>
    Task<Season> OpenAsync(
        long guildId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        CancellationToken ct = default);
}
