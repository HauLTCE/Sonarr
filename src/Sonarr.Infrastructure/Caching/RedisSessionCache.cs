using System.Globalization;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Caching;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Redis-backed chat-engine transient state (area <c>chat</c>, docs/05-caching.md).
/// FAILS OPEN throughout — see <see cref="RedisCacheBase"/>.
/// </summary>
internal sealed partial class RedisSessionCache : RedisCacheBase, ISessionCache
{
    private const string FieldMessageCount = "message_count";
    private const string FieldStartedAt = "started_at";
    private const string FieldLastTurnAt = "last_turn_at";

    public RedisSessionCache(IConnectionMultiplexer multiplexer, ILogger<RedisSessionCache> logger)
        : base(multiplexer, logger)
    {
    }

    public async Task<ChatSessionState?> GetSessionAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        var entries = await ReadAsync(
            db => db.HashGetAllAsync(RedisKeys.ChatSession(guildId, userId)),
            [],
            "chat:session get").ConfigureAwait(false);

        return entries.Length == 0 ? null : Map(entries);
    }

    public async Task<ChatSessionState> TouchSessionAsync(
        ulong guildId,
        ulong userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var key = RedisKeys.ChatSession(guildId, userId);
        var stamp = Stamp(now);
        var count = 1L;

        await WriteAsync(
            async (db, ttl) =>
            {
                count = await db.HashIncrementAsync(key, FieldMessageCount).ConfigureAwait(false);
                if (count == 1)
                {
                    await db.HashSetAsync(key, FieldStartedAt, stamp).ConfigureAwait(false);
                }

                await db.HashSetAsync(key, FieldLastTurnAt, stamp).ConfigureAwait(false);

                // Sliding TTL: re-armed on every touch, which is what makes an absent key mean
                // "no conversation in the last 10 minutes".
                await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
            },
            CacheTtl.ChatSession,
            "chat:session touch").ConfigureAwait(false);

        // On a Redis outage this is a plausible first-contact state rather than an exception:
        // the engine gets "message 1", which is the safe reading.
        var startedAt = count == 1 ? now : (await GetSessionAsync(guildId, userId, cancellationToken).ConfigureAwait(false))?.StartedAt ?? now;
        return new ChatSessionState((int)count, startedAt, now);
    }

    public Task SetPendingQuestionAsync(
        ulong guildId,
        ulong userId,
        string questionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        return WriteAsync(
            (db, ttl) => db.StringSetAsync(RedisKeys.ChatPendingQuestion(guildId, userId), questionId, ttl),
            CacheTtl.ChatPendingQuestion,
            "chat:pending_q set");
    }

    public async Task<string?> GetPendingQuestionAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        var raw = await ReadAsync(
            db => db.StringGetAsync(RedisKeys.ChatPendingQuestion(guildId, userId)),
            RedisValue.Null,
            "chat:pending_q get").ConfigureAwait(false);

        return raw.IsNullOrEmpty ? null : raw.ToString();
    }

    public Task ClearPendingQuestionAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default) =>
        DeleteAsync(RedisKeys.ChatPendingQuestion(guildId, userId), "chat:pending_q clear");

    private static ChatSessionState Map(HashEntry[] entries)
    {
        var count = 0;
        var startedAt = default(DateTimeOffset);
        var lastTurnAt = default(DateTimeOffset);

        foreach (var entry in entries)
        {
            switch ((string?)entry.Name)
            {
                case FieldMessageCount:
                    count = (int)entry.Value;
                    break;
                case FieldStartedAt:
                    startedAt = Parse(entry.Value);
                    break;
                case FieldLastTurnAt:
                    lastTurnAt = Parse(entry.Value);
                    break;
            }
        }

        return new ChatSessionState(count, startedAt, lastTurnAt);
    }

    private static string Stamp(DateTimeOffset value) =>
        value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(RedisValue value) =>
        value.TryParse(out long ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : default;
}
