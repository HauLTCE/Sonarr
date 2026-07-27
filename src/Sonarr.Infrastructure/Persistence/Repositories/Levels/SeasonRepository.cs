using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;

namespace Sonarr.Infrastructure.Persistence.Repositories.Levels;

/// <inheritdoc cref="ISeasonRepository"/>
public sealed class SeasonRepository(SonarrDbContext db) : ISeasonRepository
{
    public async Task<IReadOnlyList<Season>> GetAllAsync(long guildId, CancellationToken ct = default)
        => await db.Seasons
            .AsNoTracking()
            .Where(s => s.GuildId == guildId)
            .OrderByDescending(s => s.StartsAt)
            .ToListAsync(ct);

    public Task<Season?> GetAsync(long seasonId, CancellationToken ct = default)
        => db.Seasons
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SeasonId == seasonId, ct);

    public Task<Season?> GetActiveAsync(long guildId, CancellationToken ct = default)
        => db.Seasons
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GuildId == guildId && s.Status == SeasonStatus.Active, ct);

    public async Task<IReadOnlyList<LeaderboardEntry>> GetResultsAsync(
        long seasonId,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        List<SeasonResult> rows = await db.SeasonResults
            .AsNoTracking()
            .Where(r => r.SeasonId == seasonId)
            .OrderBy(r => r.Rank)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Max(take, 1))
            .ToListAsync(ct);

        // Season standings are frozen, so the stored rank is the rank — no recomputation.
        // Level is not stored per season (docs/04): the entry carries 0 and the card shows XP.
        return [.. rows.Select(r => new LeaderboardEntry(r.Rank, (ulong)r.UserId, r.XpEarned, 0))];
    }

    public Task<int> CountResultsAsync(long seasonId, CancellationToken ct = default)
        => db.SeasonResults.AsNoTracking().CountAsync(r => r.SeasonId == seasonId, ct);
}
