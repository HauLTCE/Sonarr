using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IFeatureFlagRepository"/>
public sealed class FeatureFlagRepository(SonarrDbContext db) : IFeatureFlagRepository
{
    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(long guildId, CancellationToken ct = default)
        => await db.FeatureFlags
            .AsNoTracking()
            .Where(f => f.GuildId == guildId || f.GuildId == 0)
            .ToListAsync(ct);

    public async Task SetAsync(
        long guildId,
        string feature,
        bool state,
        long changedBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);

        FeatureFlag? flag = await db.FeatureFlags
            .FirstOrDefaultAsync(f => f.GuildId == guildId && f.Feature == feature, ct);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (flag is null)
        {
            db.FeatureFlags.Add(new FeatureFlag
            {
                GuildId = guildId,
                Feature = feature,
                State = state,
                ChangedBy = changedBy,
                ChangedAt = now,
            });
        }
        else
        {
            flag.State = state;
            flag.ChangedBy = changedBy;
            flag.ChangedAt = now;
            flag.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
