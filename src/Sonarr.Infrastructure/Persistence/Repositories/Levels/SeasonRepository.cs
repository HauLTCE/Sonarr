using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public async Task<IReadOnlyList<SeasonStanding>> GetStandingsAsync(
        long guildId, CancellationToken ct = default)
    {
        // Two queries and a join in memory rather than one grouped join: the guild's progress rows
        // are the leaderboard (thousands at most), and "already counted" is a sum over past results
        // for the same people. A single LINQ query would have EF build a correlated subquery per
        // row.
        List<LevelProgress> rows = await db.LevelProgress
            .AsNoTracking()
            .Where(p => p.GuildId == guildId && p.Xp > 0)
            .ToListAsync(ct);

        Dictionary<long, long> accounted = await db.SeasonResults
            .AsNoTracking()
            .Where(r => r.Season!.GuildId == guildId)
            .GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, Xp = g.Sum(r => r.XpEarned) })
            .ToDictionaryAsync(x => x.UserId, x => x.Xp, ct);

        return
        [
            .. rows.Select(p => new SeasonStanding(
                p.UserId,
                p.Xp,
                accounted.TryGetValue(p.UserId, out var seen) ? seen : 0)),
        ];
    }

    public async Task<bool> CloseAsync(
        long seasonId,
        IReadOnlyList<SeasonResult> results,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(results);

        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);

        // The status test is in the WHERE, so two rollers racing means the loser updates 0 rows and
        // its transaction rolls back without having written a second set of results.
        var flipped = await db.Seasons
            .Where(s => s.SeasonId == seasonId && s.Status == SeasonStatus.Active)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, SeasonStatus.Closed)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

        if (flipped == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        foreach (SeasonResult result in results)
        {
            result.SeasonId = seasonId;
        }

        db.SeasonResults.AddRange(results);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<Season> OpenAsync(
        long guildId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        CancellationToken ct = default)
    {
        Season season = new()
        {
            GuildId = guildId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Status = SeasonStatus.Active,
        };

        db.Seasons.Add(season);
        await db.SaveChangesAsync(ct);
        return season;
    }
}
