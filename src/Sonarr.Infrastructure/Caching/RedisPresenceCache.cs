using System.Globalization;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Caching;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Redis-backed presence state (area <c>presence</c>, docs/05-caching.md).
/// FAILS OPEN: a lost voice session means that stretch isn't credited.
/// </summary>
internal sealed class RedisPresenceCache : RedisCacheBase, IPresenceCache
{
    private const string FieldJoinedAt = "joined_at";
    private const string FieldChannel = "channel";
    private const string FieldAlone = "alone";
    private const string FieldMuted = "muted";

    public RedisPresenceCache(IConnectionMultiplexer multiplexer, ILogger<RedisPresenceCache> logger)
        : base(multiplexer, logger)
    {
    }

    public Task StartVoiceAsync(
        ulong guildId,
        ulong userId,
        VoicePresence presence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presence);
        var key = RedisKeys.PresenceVoice(guildId, userId);

        return WriteAsync(
            async (db, ttl) =>
            {
                await db.HashSetAsync(
                    key,
                    [
                        new HashEntry(FieldJoinedAt, presence.JoinedAt.ToUnixTimeMilliseconds()),
                        new HashEntry(FieldChannel, presence.ChannelId),
                        new HashEntry(FieldAlone, presence.Alone),
                        new HashEntry(FieldMuted, presence.Muted),
                    ]).ConfigureAwait(false);

                // The session normally ends on leave; the TTL is the sweep for a missed leave event.
                await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
            },
            CacheTtl.PresenceVoice,
            "presence:voice start");
    }

    public Task UpdateVoiceFlagsAsync(
        ulong guildId,
        ulong userId,
        bool alone,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        var key = RedisKeys.PresenceVoice(guildId, userId);

        return WriteAsync(
            async (db, ttl) =>
            {
                // Only touch flags — JoinedAt must survive, it is the accrual anchor.
                if (!await db.KeyExistsAsync(key).ConfigureAwait(false))
                {
                    return;
                }

                await db.HashSetAsync(key, [new HashEntry(FieldAlone, alone), new HashEntry(FieldMuted, muted)])
                    .ConfigureAwait(false);
                await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
            },
            CacheTtl.PresenceVoice,
            "presence:voice update");
    }

    public async Task<VoicePresence?> GetVoiceAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        var entries = await ReadAsync(
            db => db.HashGetAllAsync(RedisKeys.PresenceVoice(guildId, userId)),
            [],
            "presence:voice get").ConfigureAwait(false);

        return entries.Length == 0 ? null : Map(entries);
    }

    public async Task<VoicePresence?> EndVoiceAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        var presence = await GetVoiceAsync(guildId, userId, cancellationToken).ConfigureAwait(false);
        if (presence is not null)
        {
            await DeleteAsync(RedisKeys.PresenceVoice(guildId, userId), "presence:voice end").ConfigureAwait(false);
        }

        return presence;
    }

    public Task SetOnlineSampleAsync(ulong guildId, int onlineCount, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (db, ttl) => db.StringSetAsync(RedisKeys.PresenceOnlineSample(guildId), onlineCount, ttl),
            CacheTtl.PresenceOnlineSample,
            "presence:online_sample set");

    public async Task<int?> GetOnlineSampleAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var raw = await ReadAsync(
            db => db.StringGetAsync(RedisKeys.PresenceOnlineSample(guildId)),
            RedisValue.Null,
            "presence:online_sample get").ConfigureAwait(false);

        return raw.IsNullOrEmpty || !raw.TryParse(out int count) ? null : count;
    }

    private static VoicePresence Map(HashEntry[] entries)
    {
        var joinedAt = default(DateTimeOffset);
        ulong channelId = 0;
        var alone = false;
        var muted = false;

        foreach (var entry in entries)
        {
            switch ((string?)entry.Name)
            {
                case FieldJoinedAt:
                    joinedAt = entry.Value.TryParse(out long ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : default;
                    break;
                case FieldChannel:
                    channelId = ulong.TryParse(entry.Value.ToString(), CultureInfo.InvariantCulture, out var id) ? id : 0;
                    break;
                case FieldAlone:
                    alone = entry.Value == 1;
                    break;
                case FieldMuted:
                    muted = entry.Value == 1;
                    break;
            }
        }

        return new VoicePresence(joinedAt, channelId, alone, muted);
    }
}
