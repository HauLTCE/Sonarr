using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Shared plumbing for the Redis-backed caches: one multiplexer, one JsonSerializerOptions,
/// and the fail-open read/write helpers.
/// </summary>
/// <remarks>
/// <para><b>Availability asymmetry — read this before adding a method.</b></para>
/// <para>
/// Non-critical paths FAIL OPEN: <see cref="ReadAsync"/> turns a Redis outage into a cache miss
/// and <see cref="WriteAsync"/> swallows it, both logged once per call. Redis holds nothing
/// durable (docs/05), so an outage must degrade features, not stop the bot.
/// </para>
/// <para>
/// Rate limits and cooldowns FAIL CLOSED: see <see cref="RedisCooldownStore"/>. A limit that
/// cannot be recorded cannot be enforced, so "Redis is down" must mean "denied", never "allowed".
/// That is why they do not use <see cref="ReadAsync"/> — the helper's miss semantics would
/// silently open the gate.
/// </para>
/// </remarks>
public abstract class RedisCacheBase
{
    /// <summary>One shared, cached options instance — building these per call is a known allocation trap.</summary>
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _multiplexer;

    protected RedisCacheBase(IConnectionMultiplexer multiplexer, ILogger logger)
    {
        _multiplexer = multiplexer;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    /// <summary>The single shared connection's database. Never open a connection per call.</summary>
    protected IDatabase Db => _multiplexer.GetDatabase();

    /// <summary>
    /// Runs a read. On a Redis failure the result is <paramref name="onFailure"/> — i.e. a cache
    /// miss — because callers of these areas always have a durable fallback (Postgres or "no state").
    /// </summary>
    protected async Task<T> ReadAsync<T>(Func<IDatabase, Task<T>> read, T onFailure, string operation)
    {
        try
        {
            return await read(Db).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            Logger.LogWarning(ex, "Redis read {Operation} failed; degrading to cache miss.", operation);
            return onFailure;
        }
    }

    /// <summary>
    /// Runs a write. Failures are logged and swallowed: nothing written here is the only copy.
    /// The <paramref name="expiry"/> parameter is required, not optional — a key without a TTL
    /// is a design bug per docs/05, so the type system asks for one at every write site.
    /// </summary>
    protected async Task WriteAsync(Func<IDatabase, TimeSpan, Task> write, TimeSpan expiry, string operation)
    {
        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiry), expiry, "Every Redis key must carry a positive TTL.");
        }

        try
        {
            await write(Db, expiry).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            Logger.LogWarning(ex, "Redis write {Operation} failed; state dropped.", operation);
        }
    }

    protected async Task<T?> ReadJsonAsync<T>(string key, string operation)
        where T : class
    {
        var raw = await ReadAsync(db => db.StringGetAsync(key), RedisValue.Null, operation).ConfigureAwait(false);
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw.ToString(), JsonOptions);
        }
        catch (JsonException ex)
        {
            // Shape changed under us (deploy with a changed DTO). Treat as a miss, not a crash.
            Logger.LogWarning(ex, "Cached value at {Key} could not be deserialized; treating as miss.", key);
            return null;
        }
    }

    protected Task WriteJsonAsync<T>(string key, T value, TimeSpan expiry, string operation) =>
        WriteAsync(
            (db, ttl) => db.StringSetAsync(key, JsonSerializer.Serialize(value, JsonOptions), ttl),
            expiry,
            operation);

    protected Task DeleteAsync(string key, string operation) =>
        ReadAsync(db => db.KeyDeleteAsync(key), false, operation);

    /// <summary>Transport/availability failures. Anything else is a bug and must surface.</summary>
    protected static bool IsRedisFailure(Exception ex) =>
        ex is RedisConnectionException or RedisTimeoutException or RedisServerException
            or ObjectDisposedException or TimeoutException or IOException;
}
