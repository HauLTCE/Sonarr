using Microsoft.Extensions.Logging;
using Sonarr.Domain.Caching;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Redis-backed music transient state (area <c>music</c>, docs/05-caching.md).
/// FAILS OPEN: worst case a session can't be resumed.
/// </summary>
internal sealed class RedisMusicSessionCache : RedisCacheBase, IMusicSessionCache
{
    public RedisMusicSessionCache(IConnectionMultiplexer multiplexer, ILogger<RedisMusicSessionCache> logger)
        : base(multiplexer, logger)
    {
    }

    public Task SaveSessionAsync<TSnapshot>(
        ulong guildId,
        TSnapshot snapshot,
        CancellationToken cancellationToken = default)
        where TSnapshot : class
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // Every save re-arms the 24 h TTL; the 30 s refresh tick is therefore also the keep-alive.
        return WriteJsonAsync(RedisKeys.MusicSession(guildId), snapshot, CacheTtl.MusicSession, "music:session save");
    }

    public Task<TSnapshot?> GetSessionAsync<TSnapshot>(ulong guildId, CancellationToken cancellationToken = default)
        where TSnapshot : class =>
        ReadJsonAsync<TSnapshot>(RedisKeys.MusicSession(guildId), "music:session get");

    public Task ClearSessionAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        DeleteAsync(RedisKeys.MusicSession(guildId), "music:session clear");

    public async Task<int> AddVoteSkipAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        var key = RedisKeys.MusicVoteSkip(guildId);
        var count = 0L;

        await WriteAsync(
            async (db, ttl) =>
            {
                await db.SetAddAsync(key, userId).ConfigureAwait(false);
                // Cleared on track end; the TTL is the leak guard for a missed track-end event.
                await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
                count = await db.SetLengthAsync(key).ConfigureAwait(false);
            },
            CacheTtl.MusicVoteSkip,
            "music:voteskip add").ConfigureAwait(false);

        return (int)count;
    }

    public async Task<int> GetVoteSkipCountAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var count = await ReadAsync(
            db => db.SetLengthAsync(RedisKeys.MusicVoteSkip(guildId)),
            0L,
            "music:voteskip count").ConfigureAwait(false);

        return (int)count;
    }

    public Task ClearVoteSkipAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        DeleteAsync(RedisKeys.MusicVoteSkip(guildId), "music:voteskip clear");

    public Task SetUndoSkipAsync<TTrack>(ulong guildId, TTrack track, CancellationToken cancellationToken = default)
        where TTrack : class
    {
        ArgumentNullException.ThrowIfNull(track);
        // The 10 s TTL *is* the undo window — expiry closes it, no timer needed.
        return WriteJsonAsync(RedisKeys.MusicUndoSkip(guildId), track, CacheTtl.MusicUndoSkip, "music:undo_skip set");
    }

    public Task<TTrack?> GetUndoSkipAsync<TTrack>(ulong guildId, CancellationToken cancellationToken = default)
        where TTrack : class =>
        ReadJsonAsync<TTrack>(RedisKeys.MusicUndoSkip(guildId), "music:undo_skip get");

    public Task SetNowPlayingMessageAsync(ulong guildId, ulong messageId, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (db, ttl) => db.StringSetAsync(RedisKeys.MusicNowPlayingMessage(guildId), messageId, ttl),
            CacheTtl.MusicNowPlayingMessage,
            "music:np_msg set");

    public async Task<ulong?> GetNowPlayingMessageAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var raw = await ReadAsync(
            db => db.StringGetAsync(RedisKeys.MusicNowPlayingMessage(guildId)),
            RedisValue.Null,
            "music:np_msg get").ConfigureAwait(false);

        return raw.IsNullOrEmpty || !raw.TryParse(out long id) ? null : (ulong)id;
    }

    public Task ClearNowPlayingMessageAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        DeleteAsync(RedisKeys.MusicNowPlayingMessage(guildId), "music:np_msg clear");
}
