using Microsoft.Extensions.Logging;
using Sonarr.Domain.Caching;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Redis-backed guild config and kill-switch flags (area <c>cfg</c>, docs/05-caching.md).
/// FAILS OPEN: a miss sends the caller to Postgres, which is the authority.
/// Explicit invalidation on write is what makes "changes apply immediately" true — the TTL is
/// only the backstop for an invalidation that never arrived.
/// </summary>
internal sealed class RedisConfigCache : RedisCacheBase, IConfigCache
{
    public RedisConfigCache(IConnectionMultiplexer multiplexer, ILogger<RedisConfigCache> logger)
        : base(multiplexer, logger)
    {
    }

    public Task<TConfig?> GetGuildConfigAsync<TConfig>(ulong guildId, CancellationToken cancellationToken = default)
        where TConfig : class =>
        ReadJsonAsync<TConfig>(RedisKeys.ConfigGuild(guildId), "cfg:guild get");

    public Task SetGuildConfigAsync<TConfig>(ulong guildId, TConfig config, CancellationToken cancellationToken = default)
        where TConfig : class
    {
        ArgumentNullException.ThrowIfNull(config);
        return WriteJsonAsync(RedisKeys.ConfigGuild(guildId), config, CacheTtl.ConfigGuild, "cfg:guild set");
    }

    public Task InvalidateGuildConfigAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        DeleteAsync(RedisKeys.ConfigGuild(guildId), "cfg:guild invalidate");

    public Task<TFlags?> GetFlagsAsync<TFlags>(CancellationToken cancellationToken = default)
        where TFlags : class =>
        ReadJsonAsync<TFlags>(RedisKeys.ConfigFlags, "cfg:flags get");

    public Task SetFlagsAsync<TFlags>(TFlags flags, CancellationToken cancellationToken = default)
        where TFlags : class
    {
        ArgumentNullException.ThrowIfNull(flags);
        return WriteJsonAsync(RedisKeys.ConfigFlags, flags, CacheTtl.ConfigFlags, "cfg:flags set");
    }

    public Task InvalidateFlagsAsync(CancellationToken cancellationToken = default) =>
        DeleteAsync(RedisKeys.ConfigFlags, "cfg:flags invalidate");
}
