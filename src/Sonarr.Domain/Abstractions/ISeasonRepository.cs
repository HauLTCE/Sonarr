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
}
