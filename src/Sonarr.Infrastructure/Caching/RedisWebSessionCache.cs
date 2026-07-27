using Microsoft.Extensions.Logging;
using Sonarr.Domain.Caching;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Redis-backed web panel state (area <c>web</c>, docs/05-caching.md).
/// </summary>
/// <remarks>
/// FAILS OPEN, and that is safe: this mirror can only confirm a session Postgres already issued,
/// never create or extend one. The TTL is min(row expiry, 30 d ceiling), so an expired session
/// cannot be affirmed from cache, and revocation is still checked against the DB on sensitive routes.
/// </remarks>
internal sealed class RedisWebSessionCache : RedisCacheBase, IWebSessionCache
{
    public RedisWebSessionCache(IConnectionMultiplexer multiplexer, ILogger<RedisWebSessionCache> logger)
        : base(multiplexer, logger)
    {
    }

    public async Task<WebSessionSnapshot?> GetSessionAsync(string sessionHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionHash);

        var session = await ReadJsonAsync<WebSessionSnapshot>(RedisKeys.WebSession(sessionHash), "web:session get")
            .ConfigureAwait(false);

        // Belt and braces: never hand back an expired session even if the TTL misfired.
        return session is null || session.ExpiresAt <= DateTimeOffset.UtcNow ? null : session;
    }

    public Task SetSessionAsync(string sessionHash, WebSessionSnapshot session, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionHash);
        ArgumentNullException.ThrowIfNull(session);

        var ttl = session.ExpiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            // Already expired — mirroring it would be a lie with a TTL attached.
            return Task.CompletedTask;
        }

        if (ttl > CacheTtl.WebSessionMax)
        {
            ttl = CacheTtl.WebSessionMax;
        }

        return WriteJsonAsync(RedisKeys.WebSession(sessionHash), session, ttl, "web:session set");
    }

    public Task RemoveSessionAsync(string sessionHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionHash);
        return DeleteAsync(RedisKeys.WebSession(sessionHash), "web:session remove");
    }

    public async Task<string?> GetLiveStatusJsonAsync(CancellationToken cancellationToken = default)
    {
        var raw = await ReadAsync(
            db => db.StringGetAsync(RedisKeys.WebLiveStatus),
            RedisValue.Null,
            "web:live_status get").ConfigureAwait(false);

        return raw.IsNullOrEmpty ? null : raw.ToString();
    }

    public Task SetLiveStatusJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return WriteAsync(
            (db, ttl) => db.StringSetAsync(RedisKeys.WebLiveStatus, json, ttl),
            CacheTtl.WebLiveStatus,
            "web:live_status set");
    }
}
