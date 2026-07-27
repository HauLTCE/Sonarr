using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Options;
using Sonarr.Application.Music;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>
/// <see cref="IMusicService"/> over Lavalink4NET. It lives in Sonarr.Bot because this is the only
/// project allowed to reference the Lavalink client; the interface it implements is in Domain, so
/// nothing above it sees a Lavalink type (docs/02-architecture.md).
/// </summary>
/// <remarks>
/// <b>The Lavalink server is not ours.</b> This is a client against the existing untouched
/// Lavalink 4.2.2 on the host (docs/03-stack.md) — no node lifecycle, no config, no restarts.
/// </remarks>
public sealed partial class MusicService(
    IAudioService audio,
    IMusicSessionCache cache,
    IPlaylistRepository playlists,
    IMusicStatsRepository stats,
    IMusicPrefsRepository prefs,
    IVoicePlayerGateway gateway) : IMusicService
{
    /// <summary>One factory instance: it is a delegate, and allocating it per command is waste.</summary>
    private static readonly PlayerFactory<SonarrPlayer, QueuedLavalinkPlayerOptions> Factory =
        PlayerFactory.Create<SonarrPlayer, QueuedLavalinkPlayerOptions>(static properties => new SonarrPlayer(properties));

    /// <summary>The player as the caller found it, or the one sentence explaining why not.</summary>
    private readonly record struct PlayerAccess(SonarrPlayer? Player, string? Problem)
    {
        public static PlayerAccess Denied(string problem) => new(null, problem);
    }

    public async Task<PlayOutcome> PlayAsync(
        MusicContext context,
        string query,
        bool confirmPlaylist = false,
        CancellationToken cancellationToken = default)
        => await EnqueueAsync(context, query, front: false, confirmPlaylist, cancellationToken).ConfigureAwait(false);

    public async Task<PlayOutcome> PlayNextAsync(
        MusicContext context, string query, CancellationToken cancellationToken = default)
        // Jumping the queue with 47 tracks is never what someone meant, so a playlist link here
        // is treated as a normal confirmation-free single: the first track.
        => await EnqueueAsync(context, query, front: true, confirmPlaylist: false, cancellationToken)
            .ConfigureAwait(false);

    private async Task<PlayOutcome> EnqueueAsync(
        MusicContext context,
        string query,
        bool front,
        bool confirmPlaylist,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(query))
        {
            return PlayOutcome.Fail("Give me something to play — a search or a link.");
        }

        if (context.VoiceChannelId is not { } voiceChannel)
        {
            return PlayOutcome.Fail("Join a voice channel first, then I'll know where to play.");
        }

        TrackResolution resolution = await ResolveAsync(query, ct).ConfigureAwait(false);

        switch (resolution.Kind)
        {
            case TrackResolutionKind.Empty:
                return PlayOutcome.Fail($"Nothing came back for **{query}**.");
            case TrackResolutionKind.Failed:
                return PlayOutcome.Fail("The source refused that one. Try a different link or search.");
            case TrackResolutionKind.Playlist when !confirmPlaylist && !front:
                return new PlayOutcome(
                    false,
                    $"That's a playlist — **{resolution.PlaylistName}**, {resolution.Tracks.Count} tracks. Sure?",
                    new PlaylistPrompt(resolution.PlaylistName ?? "playlist", resolution.Tracks.Count, query));
        }

        SonarrPlayer? player = await JoinAsync(context, voiceChannel, ct).ConfigureAwait(false);
        if (player is null)
        {
            return PlayOutcome.Fail("I couldn't get into that channel. Check my permissions and try again.");
        }

        IReadOnlyList<TrackInfo> tracks = front || resolution.Kind is not TrackResolutionKind.Playlist
            ? [resolution.Tracks[0]]
            : resolution.Tracks;

        List<ITrackQueueItem> items = [.. tracks.Select(t => (ITrackQueueItem)TrackMapping.FromDomain(
            t with { RequesterId = context.UserId }))];

        // Nothing playing: hand the first item straight to the player so playback starts now.
        if (player.CurrentItem is null)
        {
            await player.PlayAsync(items[0], enqueue: false, cancellationToken: ct).ConfigureAwait(false);
            items.RemoveAt(0);
        }
        else if (front)
        {
            await player.Queue.InsertAsync(0, items[0], ct).ConfigureAwait(false);
            items.Clear();
        }

        if (items.Count > 0)
        {
            await player.Queue.AddRangeAsync(items, ct).ConfigureAwait(false);
        }

        if (player.FairQueue)
        {
            await ReorderFairlyAsync(player, ct).ConfigureAwait(false);
        }

        return PlayOutcome.Ok(tracks.Count == 1
            ? $"Queued **{tracks[0].Title}**{(front ? " — up next" : string.Empty)}."
            : $"Queued **{tracks.Count}** tracks from **{resolution.PlaylistName}**.");
    }

    /// <summary>A search or a link, turned into domain tracks. Requester is filled in by the caller.</summary>
    private async Task<TrackResolution> ResolveAsync(string query, CancellationToken ct)
    {
        var trimmed = query.Trim();

        // A bare search goes to YouTube; anything that parses as a URL is loaded as-is so
        // Spotify/SoundCloud links keep working through the server's plugins.
        TrackSearchMode mode = Uri.IsWellFormedUriString(trimmed, UriKind.Absolute)
            ? TrackSearchMode.None
            : TrackSearchMode.YouTube;

        TrackLoadResult result;
        try
        {
            result = await audio.Tracks.LoadTracksAsync(trimmed, mode, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            // The node is on the host and outside our control; a bad answer is a failed search,
            // not a crashed command.
            return TrackResolution.Failed;
        }

        if (result.IsFailed)
        {
            return TrackResolution.Failed;
        }

        if (!result.HasMatches)
        {
            return TrackResolution.Empty;
        }

        if (result.IsPlaylist)
        {
            IReadOnlyList<TrackInfo> tracks = [.. result.Tracks.Select(t => TrackMapping.ToDomain(t, 0))];
            return new TrackResolution(TrackResolutionKind.Playlist, tracks, result.Playlist.Name);
        }

        return new TrackResolution(TrackResolutionKind.Track, [TrackMapping.ToDomain(result.Track, 0)]);
    }

    /// <summary>Gets or creates the guild player, joining <paramref name="voiceChannelId"/>.</summary>
    private async Task<SonarrPlayer?> JoinAsync(MusicContext context, ulong voiceChannelId, CancellationToken ct)
    {
        var options = new QueuedLavalinkPlayerOptions
        {
            SelfDeaf = true,
            // Keep history: /previous and /undo-skip both read it.
            HistoryCapacity = 25,
            InitialVolume = await ResolveInitialVolumeAsync(context, ct).ConfigureAwait(false),
        };

        var retrieve = new PlayerRetrieveOptions
        {
            ChannelBehavior = PlayerChannelBehavior.Join,
            VoiceStateBehavior = MemberVoiceStateBehavior.Ignore,
        };

        PlayerResult<SonarrPlayer> result = await audio.Players
            .RetrieveAsync(context.GuildId, voiceChannelId, Factory, Options.Create(options), retrieve, ct)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return null;
        }

        result.Player.TextChannelId = context.TextChannelId;
        return result.Player;
    }

    /// <summary>The caller's remembered volume, or the guild default (docs/07 — /volume).</summary>
    private async Task<float> ResolveInitialVolumeAsync(MusicContext context, CancellationToken ct)
    {
        var stored = await prefs.GetVolumeAsync(context.GuildId, context.UserId, ct).ConfigureAwait(false);
        return (stored ?? MusicRules.DefaultVolume) / 100f;
    }

    /// <summary>
    /// The player for a command that changes playback: it must exist, and the caller must be in
    /// the same channel. DJ-only commands pass <paramref name="djOnly"/>.
    /// </summary>
    private async Task<PlayerAccess> RequirePlayerAsync(
        MusicContext context, CancellationToken ct, bool djOnly = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (djOnly && !context.IsDj)
        {
            return PlayerAccess.Denied("That one's for DJs. An admin sets the DJ role with `/config set dj_role`.");
        }

        SonarrPlayer? player = await audio.Players
            .GetPlayerAsync<SonarrPlayer>(context.GuildId, ct)
            .ConfigureAwait(false);

        if (player is null)
        {
            return PlayerAccess.Denied("I'm not playing anything right now.");
        }

        if (context.VoiceChannelId != player.VoiceChannelId)
        {
            return PlayerAccess.Denied("You need to be in the voice channel with me for that.");
        }

        player.TextChannelId = context.TextChannelId;
        return new PlayerAccess(player, null);
    }

    /// <summary>Read-only lookups: no voice requirement, no DJ check.</summary>
    private ValueTask<SonarrPlayer?> FindPlayerAsync(ulong guildId, CancellationToken ct)
        => audio.Players.GetPlayerAsync<SonarrPlayer>(guildId, ct);
}
