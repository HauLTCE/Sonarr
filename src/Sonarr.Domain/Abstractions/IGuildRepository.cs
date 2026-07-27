using Sonarr.Domain.Entities.Core;

namespace Sonarr.Domain.Abstractions;

/// <summary>core.guild access.</summary>
public interface IGuildRepository
{
    Task<Guild?> GetAsync(long guildId, CancellationToken ct = default);

    Task<IReadOnlyList<Guild>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Insert on join, refresh the cached name on rename. Returns the stored row.</summary>
    Task<Guild> UpsertAsync(long guildId, string name, DateTimeOffset joinedAt, CancellationToken ct = default);

    /// <summary>Removes the guild row; area tables cascade.</summary>
    Task RemoveAsync(long guildId, CancellationToken ct = default);
}
