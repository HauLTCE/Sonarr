namespace Sonarr.Domain.Caching;

/// <summary>
/// Guild config and kill-switch flags (Redis area <c>cfg</c>, docs/05-caching.md).
/// Explicit invalidation on write is the "changes apply immediately" mechanism.
/// </summary>
/// <remarks>
/// Availability contract: FAILS OPEN. A miss means the caller reads Postgres, which is the
/// authority; the cache only removes a per-request DB hit.
/// </remarks>
public interface IConfigCache
{
    /// <summary>Cached guild config (<c>cfg:guild:{guild}</c>, 10 min), or <c>null</c> on a miss.</summary>
    Task<TConfig?> GetGuildConfigAsync<TConfig>(ulong guildId, CancellationToken cancellationToken = default)
        where TConfig : class;

    Task SetGuildConfigAsync<TConfig>(ulong guildId, TConfig config, CancellationToken cancellationToken = default)
        where TConfig : class;

    /// <summary>Drops the cached config so the next read sees the new row. Call after every config write.</summary>
    Task InvalidateGuildConfigAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Cached kill-switch states (<c>cfg:flags</c>, 1 min), or <c>null</c> on a miss.</summary>
    Task<TFlags?> GetFlagsAsync<TFlags>(CancellationToken cancellationToken = default)
        where TFlags : class;

    Task SetFlagsAsync<TFlags>(TFlags flags, CancellationToken cancellationToken = default)
        where TFlags : class;

    /// <summary>Drops the cached flags — call when an admin toggles a kill switch.</summary>
    Task InvalidateFlagsAsync(CancellationToken cancellationToken = default);
}
