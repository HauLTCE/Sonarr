using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;

namespace Sonarr.Domain.Abstractions;

/// <summary>levels.progress access. Postgres is the authority for XP — Redis never holds it.</summary>
public interface ILevelProgressRepository
{
    Task<LevelProgress?> GetAsync(long guildId, long userId, CancellationToken ct = default);

    /// <summary>Inserts the row at level 1 / 0 XP if it is missing, and returns it either way.</summary>
    Task<LevelProgress> GetOrCreateAsync(long guildId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Persists an XP grant in one round trip: the new totals plus the streak/bonus day stamps.
    /// </summary>
    Task SaveAsync(LevelProgress progress, CancellationToken ct = default);

    /// <summary>
    /// Adds qualifying voice time and the XP it is worth. Separate from
    /// <see cref="SaveAsync"/> because the accrual timer has no row loaded and runs for many
    /// users at once.
    /// </summary>
    /// <returns>The row after the update, so the caller can spot a level-up.</returns>
    Task<LevelProgress> AddVoiceAsync(
        long guildId,
        long userId,
        long seconds,
        long xp,
        int newLevel,
        CancellationToken ct = default);

    /// <summary><c>1</c>-based rank by XP; <c>0</c> when the member has no row.</summary>
    Task<int> GetRankAsync(long guildId, long userId, CancellationToken ct = default);

    Task<int> CountAsync(long guildId, CancellationToken ct = default);

    /// <summary>One page of the live standings, highest XP first.</summary>
    Task<IReadOnlyList<LeaderboardEntry>> GetTopAsync(
        long guildId,
        int skip,
        int take,
        CancellationToken ct = default);
}
