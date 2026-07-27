using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IGuildConfigRepository"/>
public sealed class GuildConfigRepository(SonarrDbContext db) : IGuildConfigRepository
{
    public Task<GuildConfig?> GetAsync(long guildId, string key, CancellationToken ct = default)
        => db.GuildConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Key == key, ct);

    public async Task<IReadOnlyList<GuildConfig>> GetAllAsync(long guildId, CancellationToken ct = default)
        => await db.GuildConfigs
            .AsNoTracking()
            .Where(c => c.GuildId == guildId)
            .OrderBy(c => c.Key)
            .ToListAsync(ct);

    public async Task SetAsync(
        long guildId,
        string key,
        JsonObject value,
        long updatedBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        GuildConfig? row = await db.GuildConfigs
            .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Key == key, ct);

        if (row is null)
        {
            db.GuildConfigs.Add(new GuildConfig
            {
                GuildId = guildId,
                Key = key,
                Value = value,
                UpdatedBy = updatedBy,
            });
        }
        else
        {
            row.Value = value;
            row.UpdatedBy = updatedBy;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(long guildId, string key, CancellationToken ct = default)
        => await db.GuildConfigs
            .Where(c => c.GuildId == guildId && c.Key == key)
            .ExecuteDeleteAsync(ct) > 0;
}
