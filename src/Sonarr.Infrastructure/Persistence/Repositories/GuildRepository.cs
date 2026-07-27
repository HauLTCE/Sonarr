using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IGuildRepository"/>
public sealed class GuildRepository(SonarrDbContext db) : IGuildRepository
{
    public Task<Guild?> GetAsync(long guildId, CancellationToken ct = default)
        => db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.GuildId == guildId, ct);

    public async Task<IReadOnlyList<Guild>> GetAllAsync(CancellationToken ct = default)
        => await db.Guilds.AsNoTracking().OrderBy(g => g.GuildId).ToListAsync(ct);

    public async Task<Guild> UpsertAsync(
        long guildId,
        string name,
        DateTimeOffset joinedAt,
        CancellationToken ct = default)
    {
        Guild? guild = await db.Guilds.FirstOrDefaultAsync(g => g.GuildId == guildId, ct);
        if (guild is null)
        {
            guild = new Guild { GuildId = guildId, Name = name, JoinedAt = joinedAt };
            db.Guilds.Add(guild);
        }
        else
        {
            guild.Name = name;
            guild.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return guild;
    }

    public Task RemoveAsync(long guildId, CancellationToken ct = default)
        => db.Guilds.Where(g => g.GuildId == guildId).ExecuteDeleteAsync(ct);
}
