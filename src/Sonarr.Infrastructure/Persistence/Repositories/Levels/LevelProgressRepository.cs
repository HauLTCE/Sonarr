using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;

namespace Sonarr.Infrastructure.Persistence.Repositories.Levels;

/// <inheritdoc cref="ILevelProgressRepository"/>
public sealed class LevelProgressRepository(SonarrDbContext db) : ILevelProgressRepository
{
    public Task<LevelProgress?> GetAsync(long guildId, long userId, CancellationToken ct = default)
        => db.LevelProgress
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GuildId == guildId && p.UserId == userId, ct);

    public async Task<LevelProgress> GetOrCreateAsync(long guildId, long userId, CancellationToken ct = default)
    {
        LevelProgress? row = await db.LevelProgress
            .FirstOrDefaultAsync(p => p.GuildId == guildId && p.UserId == userId, ct);

        if (row is not null)
        {
            return row;
        }

        row = new LevelProgress
        {
            GuildId = guildId,
            UserId = userId,
            Level = LevelCurve.FirstLevel,
        };

        db.LevelProgress.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task SaveAsync(LevelProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        progress.UpdatedAt = DateTimeOffset.UtcNow;
        if (db.Entry(progress).State == EntityState.Detached)
        {
            db.LevelProgress.Update(progress);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<LevelProgress> AddVoiceAsync(
        long guildId,
        long userId,
        long seconds,
        long xp,
        int newLevel,
        CancellationToken ct = default)
    {
        // Server-side increment: the accrual timer runs for many users at once and must not lose a
        // concurrent message-XP write by overwriting a stale in-memory total.
        await db.LevelProgress
            .Where(p => p.GuildId == guildId && p.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(p => p.Xp, p => p.Xp + xp)
                    .SetProperty(p => p.VoiceSeconds, p => p.VoiceSeconds + seconds)
                    .SetProperty(p => p.Level, newLevel)
                    .SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

        return await GetOrCreateAsync(guildId, userId, ct);
    }

    public async Task<int> GetRankAsync(long guildId, long userId, CancellationToken ct = default)
    {
        LevelProgress? row = await GetAsync(guildId, userId, ct);
        if (row is null)
        {
            return 0;
        }

        var ahead = await db.LevelProgress
            .AsNoTracking()
            .CountAsync(p => p.GuildId == guildId && p.Xp > row.Xp, ct);

        return ahead + 1;
    }

    public Task<int> CountAsync(long guildId, CancellationToken ct = default)
        => db.LevelProgress.AsNoTracking().CountAsync(p => p.GuildId == guildId, ct);

    public async Task<IReadOnlyList<LeaderboardEntry>> GetTopAsync(
        long guildId,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        List<LevelProgress> rows = await db.LevelProgress
            .AsNoTracking()
            .Where(p => p.GuildId == guildId)
            .OrderByDescending(p => p.Xp)
            .ThenBy(p => p.UserId)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Max(take, 1))
            .ToListAsync(ct);

        // Rank is position in the ordered set, so the page offset is the base.
        return [.. rows.Select((r, i) => new LeaderboardEntry(skip + i + 1, (ulong)r.UserId, r.Xp, r.Level))];
    }
}
