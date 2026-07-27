using System.Text.Json.Nodes;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// core.guild_config access. Callers go through the Config service, which owns
/// validation and Redis invalidation — repositories do not touch the cache.
/// </summary>
public interface IGuildConfigRepository
{
    Task<GuildConfig?> GetAsync(long guildId, string key, CancellationToken ct = default);

    /// <summary>Whole-guild read; the Config service caches this blob.</summary>
    Task<IReadOnlyList<GuildConfig>> GetAllAsync(long guildId, CancellationToken ct = default);

    Task SetAsync(long guildId, string key, JsonObject value, long updatedBy, CancellationToken ct = default);

    /// <summary>True if a row existed and was removed.</summary>
    Task<bool> RemoveAsync(long guildId, string key, CancellationToken ct = default);
}
