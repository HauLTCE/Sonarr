using Lavalink4NET.Players;
using Sonarr.Application.Music;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>Named playlists, crash-session resume, and the stats read-outs.</summary>
public sealed partial class MusicService
{
    public async Task<MusicResult> SavePlaylistAsync(
        MusicContext context, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Invalid(name) is { } problem)
        {
            return MusicResult.Fail(problem);
        }

        SonarrPlayer? player = await FindPlayerAsync(context.GuildId, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return MusicResult.Fail("Nothing is playing, so there's nothing to save.");
        }

        List<TrackInfo> tracks = [];
        if (TrackMapping.ToDomain(player.CurrentItem) is { } current)
        {
            tracks.Add(current);
        }

        tracks.AddRange(Snapshot(player));

        if (tracks.Count == 0)
        {
            return MusicResult.Fail("The queue is empty.");
        }

        if (tracks.Count > MusicRules.MaxPlaylistTracks)
        {
            // Truncate rather than refuse: saving the first 500 of a 900-track queue is still
            // what the person wanted.
            tracks.RemoveRange(MusicRules.MaxPlaylistTracks, tracks.Count - MusicRules.MaxPlaylistTracks);
        }

        await playlists.SaveAsync(context.GuildId, name.Trim(), context.UserId, tracks, cancellationToken)
            .ConfigureAwait(false);

        return MusicResult.Ok($"Saved **{tracks.Count}** tracks as **{name.Trim()}**.");
    }

    public async Task<MusicResult> LoadPlaylistAsync(
        MusicContext context, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Invalid(name) is { } problem)
        {
            return MusicResult.Fail(problem);
        }

        if (context.VoiceChannelId is not { } voiceChannel)
        {
            return MusicResult.Fail("Join a voice channel first.");
        }

        PlaylistContents? saved = await playlists.GetAsync(context.GuildId, name.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (saved is null || saved.Tracks.Count == 0)
        {
            return MusicResult.Fail($"No playlist called **{name.Trim()}** here.");
        }

        SonarrPlayer? player = await JoinAsync(context, voiceChannel, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return MusicResult.Fail("I couldn't get into that channel.");
        }

        var queued = await EnqueueDomainAsync(player, saved.Tracks, context.UserId, cancellationToken)
            .ConfigureAwait(false);

        return MusicResult.Ok($"Queued **{queued}** tracks from **{saved.Name}**.");
    }

    public Task<IReadOnlyList<PlaylistSummary>> ListPlaylistsAsync(
        ulong guildId, CancellationToken cancellationToken = default)
        => playlists.ListAsync(guildId, cancellationToken);

    public async Task<MusicResult> DeletePlaylistAsync(
        MusicContext context, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Invalid(name) is { } problem)
        {
            return MusicResult.Fail(problem);
        }

        var trimmed = name.Trim();
        PlaylistContents? saved = await playlists.GetAsync(context.GuildId, trimmed, cancellationToken)
            .ConfigureAwait(false);

        if (saved is null)
        {
            return MusicResult.Fail($"No playlist called **{trimmed}** here.");
        }

        if (saved.OwnerId != context.UserId && !context.IsDj)
        {
            return MusicResult.Fail("That's not your playlist — only the owner or a DJ can delete it.");
        }

        await playlists.DeleteAsync(context.GuildId, trimmed, cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok($"Deleted **{trimmed}**.");
    }

    public async Task<MusicResult> ResumeSessionAsync(
        MusicContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        MusicSessionSnapshot? snapshot = await cache
            .GetSessionAsync<MusicSessionSnapshot>(context.GuildId, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot?.Current is null)
        {
            return MusicResult.Fail("No saved session to restore.");
        }

        // Prefer where the caller is; fall back to where the session was.
        var voiceChannel = context.VoiceChannelId ?? snapshot.VoiceChannelId;
        if (voiceChannel == 0)
        {
            return MusicResult.Fail("Join a voice channel first.");
        }

        SonarrPlayer? player = await JoinAsync(context with { VoiceChannelId = voiceChannel }, voiceChannel, cancellationToken)
            .ConfigureAwait(false);

        if (player is null)
        {
            return MusicResult.Fail("I couldn't get into that channel.");
        }

        await player.PlayAsync(
                TrackMapping.FromDomain(snapshot.Current),
                enqueue: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (snapshot.PositionMs > 0)
        {
            await player.SeekAsync(TimeSpan.FromMilliseconds(snapshot.PositionMs), cancellationToken)
                .ConfigureAwait(false);
        }

        var restored = await EnqueueDomainAsync(player, snapshot.Queue, requesterOverride: null, cancellationToken)
            .ConfigureAwait(false);

        await SetLoopAsync(context, snapshot.Loop, cancellationToken).ConfigureAwait(false);

        return MusicResult.Ok(
            $"Picked up **{snapshot.Current.Title}** where it stopped, with **{restored}** tracks behind it.");
    }

    public Task<MusicStats> GetStatsAsync(ulong guildId, CancellationToken cancellationToken = default)
        => stats.GetStatsAsync(guildId, MusicRules.StatsTop, cancellationToken);

    public Task<IReadOnlyList<TrackPlayCount>> GetMyTracksAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default)
        => stats.GetUserHistoryAsync(guildId, userId, MusicRules.StatsTop, cancellationToken);

    public Task<IReadOnlyList<RatedTrack>> GetTopTracksAsync(
        ulong guildId, CancellationToken cancellationToken = default)
        => stats.GetTopRatedAsync(guildId, MusicRules.StatsTop, cancellationToken);

    /// <summary>Appends domain tracks to a live player, starting playback if it is idle.</summary>
    private static async Task<int> EnqueueDomainAsync(
        SonarrPlayer player, IReadOnlyList<TrackInfo> tracks, ulong? requesterOverride, CancellationToken ct)
    {
        List<ITrackQueueItem> items =
        [
            .. tracks.Select(t => (ITrackQueueItem)TrackMapping.FromDomain(
                requesterOverride is { } id ? t with { RequesterId = id } : t)),
        ];

        if (items.Count == 0)
        {
            return 0;
        }

        var count = items.Count;

        if (player.CurrentItem is null)
        {
            await player.PlayAsync(items[0], enqueue: false, cancellationToken: ct).ConfigureAwait(false);
            items.RemoveAt(0);
        }

        if (items.Count > 0)
        {
            await player.Queue.AddRangeAsync(items, ct).ConfigureAwait(false);
        }

        return count;
    }

    /// <summary>Playlist-name validation at the trust boundary: the name reaches a unique index.</summary>
    private static string? Invalid(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? "Give the playlist a name."
            : name.Trim().Length > MusicRules.MaxPlaylistNameLength
                ? $"Keep the name under {MusicRules.MaxPlaylistNameLength} characters."
                : null;
}
