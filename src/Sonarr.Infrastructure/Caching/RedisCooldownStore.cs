using Microsoft.Extensions.Logging;
using Sonarr.Domain.Caching;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Redis-backed rate limits and cooldowns (area <c>rl</c>, docs/05-caching.md).
/// </summary>
/// <remarks>
/// <b>FAILS CLOSED.</b> Unlike every other cache in this folder, a Redis outage here denies the
/// action: a cooldown that cannot be recorded is a cooldown that cannot be enforced, and the cost
/// is a missed XP tick, not lost data.
/// The single exception is <see cref="RecordMessageHashAsync"/>: it drives an automated moderation
/// action, so an outage there must not punish anyone. It is marked at the call site.
/// </remarks>
internal sealed class RedisCooldownStore : RedisCacheBase, ICooldownStore
{
    public RedisCooldownStore(IConnectionMultiplexer multiplexer, ILogger<RedisCooldownStore> logger)
        : base(multiplexer, logger)
    {
    }

    public Task<bool> TryAcquireXpAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default) =>
        TryAcquireAsync(RedisKeys.RateLimitXp(guildId, userId), CacheTtl.RateLimitXp);

    public Task<bool> TryAcquireCommandAsync(ulong userId, CancellationToken cancellationToken = default) =>
        TryAcquireAsync(RedisKeys.RateLimitCommand(userId), CacheTtl.RateLimitCommand);

    public async Task<int> RecordMessageHashAsync(
        ulong guildId,
        ulong userId,
        string messageHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageHash);

        var key = RedisKeys.RateLimitSpam(guildId, userId);
        try
        {
            // Hash of recent message hashes → seen count. HashIncrement creates the field; the TTL
            // is (re)armed on the key so a quiet user's state ages out after 5 min.
            var seen = await Db.HashIncrementAsync(key, messageHash).ConfigureAwait(false);
            await Db.KeyExpireAsync(key, CacheTtl.RateLimitSpam).ConfigureAwait(false);
            return (int)Math.Min(seen, int.MaxValue);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            // FAIL OPEN, deliberately: a positive result here triggers a moderation action.
            // An outage must not auto-punish users, so report "first time seen".
            Logger.LogWarning(ex, "Redis unavailable for spam state {Guild}/{User}; treating message as unseen.", guildId, userId);
            return 1;
        }
    }

    public Task ClearMessageHashesAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default) =>
        DeleteAsync(RedisKeys.RateLimitSpam(guildId, userId), "rl:spam clear");

    /// <summary>
    /// SET NX + TTL: the classic single-round-trip cooldown. <c>true</c> means this caller
    /// created the key and owns the window.
    /// </summary>
    private async Task<bool> TryAcquireAsync(string key, TimeSpan window)
    {
        try
        {
            return await Db.StringSetAsync(key, 1, window, When.NotExists).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            // FAIL CLOSED: a cooldown that cannot be recorded is a cooldown that cannot be enforced.
            Logger.LogError(ex, "Redis unavailable for cooldown {Key}; denying (fail closed).", key);
            return false;
        }
    }
}
