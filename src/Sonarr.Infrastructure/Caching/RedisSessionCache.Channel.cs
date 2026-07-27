using Sonarr.Domain.Caching;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Channel-scoped and person-scoped half of the <c>chat</c> area: edit-detection markers, the
/// capped-10 ring buffer, the engagement budget, the last-ping hash and the hot person cache.
/// </summary>
internal sealed partial class RedisSessionCache
{
    public Task MarkRepliedAsync(
        ulong channelId,
        ulong messageId,
        string stateHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateHash);
        return WriteAsync(
            (db, ttl) => db.StringSetAsync(RedisKeys.ChatReplied(channelId, messageId), stateHash, ttl),
            CacheTtl.ChatReplied,
            "chat:replied set");
    }

    public async Task<string?> GetRepliedStateHashAsync(
        ulong channelId,
        ulong messageId,
        CancellationToken cancellationToken = default)
    {
        var raw = await ReadAsync(
            db => db.StringGetAsync(RedisKeys.ChatReplied(channelId, messageId)),
            RedisValue.Null,
            "chat:replied get").ConfigureAwait(false);

        return raw.IsNullOrEmpty ? null : raw.ToString();
    }

    public Task PushRecentMessageAsync(
        ulong channelId,
        RecentMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = RedisKeys.ChatRing(channelId);

        return WriteAsync(
            async (db, ttl) =>
            {
                await db.ListLeftPushAsync(key, Serialize(message)).ConfigureAwait(false);
                // Cap at 10 (docs/05). Trim after every push so the list cannot grow between pushes.
                await db.ListTrimAsync(key, 0, CacheTtl.RingBufferLength - 1).ConfigureAwait(false);
                await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
            },
            CacheTtl.ChatRing,
            "chat:ring push");
    }

    public async Task<IReadOnlyList<RecentMessage>> GetRecentMessagesAsync(
        ulong channelId,
        CancellationToken cancellationToken = default)
    {
        var raw = await ReadAsync(
            db => db.ListRangeAsync(RedisKeys.ChatRing(channelId), 0, CacheTtl.RingBufferLength - 1),
            [],
            "chat:ring get").ConfigureAwait(false);

        var result = new List<RecentMessage>(raw.Length);
        foreach (var value in raw)
        {
            var parsed = Deserialize(value);
            if (parsed is not null)
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    public async Task<int> IncrementEngagementAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        var key = RedisKeys.ChatBudget(channelId);
        var count = 0L;

        await WriteAsync(
            async (db, ttl) =>
            {
                count = await db.StringIncrementAsync(key).ConfigureAwait(false);
                if (count == 1)
                {
                    // Fixed window: expiry only on the first increment, so the budget actually
                    // resets every hour instead of never (setting it every hit would slide it).
                    await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
                }
            },
            CacheTtl.ChatBudget,
            "chat:budget incr").ConfigureAwait(false);

        return (int)count;
    }

    public async Task<int> GetEngagementAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        var raw = await ReadAsync(
            db => db.StringGetAsync(RedisKeys.ChatBudget(channelId)),
            RedisValue.Null,
            "chat:budget get").ConfigureAwait(false);

        return raw.IsNullOrEmpty || !raw.TryParse(out int count) ? 0 : count;
    }

    public async Task<string?> GetLastPingHashAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        var raw = await ReadAsync(
            db => db.StringGetAsync(RedisKeys.ChatLastPing(guildId, userId)),
            RedisValue.Null,
            "chat:lastping get").ConfigureAwait(false);

        return raw.IsNullOrEmpty ? null : raw.ToString();
    }

    public Task SetLastPingHashAsync(
        ulong guildId,
        ulong userId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        return WriteAsync(
            (db, ttl) => db.StringSetAsync(RedisKeys.ChatLastPing(guildId, userId), contentHash, ttl),
            CacheTtl.ChatLastPing,
            "chat:lastping set");
    }

    public async Task<TPerson?> GetHotPersonAsync<TPerson>(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
        where TPerson : class
    {
        var key = RedisKeys.ChatHotPerson(guildId, userId);
        var person = await ReadJsonAsync<TPerson>(key, "chat:hot get").ConfigureAwait(false);
        if (person is not null)
        {
            // Sliding: a read mid-conversation keeps the entry warm for another 5 min.
            await ReadAsync(db => db.KeyExpireAsync(key, CacheTtl.ChatHotPerson), false, "chat:hot slide")
                .ConfigureAwait(false);
        }

        return person;
    }

    public Task SetHotPersonAsync<TPerson>(
        ulong guildId,
        ulong userId,
        TPerson person,
        CancellationToken cancellationToken = default)
        where TPerson : class
    {
        ArgumentNullException.ThrowIfNull(person);
        return WriteJsonAsync(RedisKeys.ChatHotPerson(guildId, userId), person, CacheTtl.ChatHotPerson, "chat:hot set");
    }

    private static string Serialize(RecentMessage message) =>
        System.Text.Json.JsonSerializer.Serialize(message, JsonOptions);

    private RecentMessage? Deserialize(RedisValue value)
    {
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<RecentMessage>(value.ToString(), JsonOptions);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
