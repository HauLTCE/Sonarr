using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Levels;

namespace Sonarr.Infrastructure.Persistence.Repositories.Levels;

/// <inheritdoc cref="ILevelRewardRepository"/>
public sealed class LevelRewardRepository(SonarrDbContext db) : ILevelRewardRepository
{
    public async Task<IReadOnlyList<LevelReward>> GetAllAsync(long guildId, CancellationToken ct = default)
        => await db.LevelRewards
            .AsNoTracking()
            .Where(r => r.GuildId == guildId)
            .OrderBy(r => r.Level)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LevelReward>> GetEarnedAsync(
        long guildId, int level, CancellationToken ct = default)
        => await db.LevelRewards
            .AsNoTracking()
            .Where(r => r.GuildId == guildId && r.Level <= level)
            .OrderBy(r => r.Level)
            .ToListAsync(ct);

    public async Task SetAsync(long guildId, int level, long roleId, CancellationToken ct = default)
    {
        LevelReward? row = await db.LevelRewards
            .FirstOrDefaultAsync(r => r.GuildId == guildId && r.Level == level, ct);

        if (row is null)
        {
            db.LevelRewards.Add(new LevelReward { GuildId = guildId, Level = level, RoleId = roleId });
        }
        else
        {
            row.RoleId = roleId;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(long guildId, int level, CancellationToken ct = default)
        => await db.LevelRewards
            .Where(r => r.GuildId == guildId && r.Level == level)
            .ExecuteDeleteAsync(ct) > 0;
}
