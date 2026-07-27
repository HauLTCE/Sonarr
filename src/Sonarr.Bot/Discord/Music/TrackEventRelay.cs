using Lavalink4NET;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Players;
using Lavalink4NET.Rest.Entities.Tracks;
using Sonarr.Application.Music;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>
/// The three things that must happen around a track without anyone typing a command: record the
/// play, reset the vote-skip tally, and keep autoplay going when the queue runs dry
/// (docs/08-background-services.md).
/// </summary>
public sealed class TrackEventRelay(
    IAudioService audio,
    IMusicSessionCache cache,
    IServiceScopeFactory scopes,
    ILogger<TrackEventRelay> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        audio.TrackStarted += OnTrackStartedAsync;
        audio.TrackEnded += OnTrackEndedAsync;
        audio.TrackException += OnTrackExceptionAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        audio.TrackStarted -= OnTrackStartedAsync;
        audio.TrackEnded -= OnTrackEndedAsync;
        audio.TrackException -= OnTrackExceptionAsync;
        return Task.CompletedTask;
    }

    private async Task OnTrackStartedAsync(object sender, TrackStartedEventArgs args)
    {
        try
        {
            // A new track means the previous tally is meaningless.
            await cache.ClearVoteSkipAsync(args.Player.GuildId).ConfigureAwait(false);

            TrackInfo track = TrackMapping.ToDomain(args.Track, RequesterOf(args.Player));

            using IServiceScope scope = scopes.CreateScope();
            var stats = scope.ServiceProvider.GetRequiredService<IMusicStatsRepository>();
            await stats.RecordPlayAsync(args.Player.GuildId, track).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Stats are not worth interrupting playback for.
            log.LogWarning(ex, "Could not record the play for guild {GuildId}", args.Player.GuildId);
        }
    }

    private async Task OnTrackEndedAsync(object sender, TrackEndedEventArgs args)
    {
        if (args.Player is not SonarrPlayer player || player.AutoplayMode is AutoplayMode.Off)
        {
            return;
        }

        if (!player.Queue.IsEmpty || player.CurrentItem is not null)
        {
            return;
        }

        try
        {
            await AutoplayAsync(player, args.Track).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Autoplay running out of ideas ends the session quietly; it never breaks the player.
            log.LogWarning(ex, "Autoplay failed for guild {GuildId}", player.GuildId);
        }
    }

    private Task OnTrackExceptionAsync(object sender, TrackExceptionEventArgs args)
    {
        // The node reports source failures here (region blocks, dead links). Nothing to fix, but
        // silence would make "it just skipped that one" unexplainable.
        log.LogInformation(
            "Track failed on the node in guild {GuildId}: {Track}", args.Player.GuildId, args.Track.Title);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Queues something related to keep the music going. Smart mode seeds from what the room has
    /// played and liked; plain mode just follows the track that ended.
    /// </summary>
    private async Task AutoplayAsync(SonarrPlayer player, Lavalink4NET.Tracks.LavalinkTrack ended)
    {
        var seed = ended.Uri?.ToString() ?? ended.Title;

        if (player.AutoplayMode is AutoplayMode.Smart)
        {
            using IServiceScope scope = scopes.CreateScope();
            var stats = scope.ServiceProvider.GetRequiredService<IMusicStatsRepository>();

            IReadOnlyList<TrackPlayCount> history =
                await stats.GetStatsAsync(player.GuildId, MusicRules.StatsTop).ConfigureAwait(false) is { } summary
                    ? summary.MostPlayed
                    : [];

            IReadOnlyList<string> disliked = await stats.GetDislikedUrisAsync(player.GuildId).ConfigureAwait(false);

            seed = AutoplaySeeder.PickSeed(history, disliked, [ended.Uri?.ToString() ?? string.Empty]) ?? seed;
        }

        if (string.IsNullOrWhiteSpace(seed))
        {
            return;
        }

        // ponytail: "related" is a YouTube-Music search around the seed rather than the node's
        // recommendation endpoint, which the plugin set on the host may not expose. Upgrade path:
        // swap this one call for the node's related-tracks route if it turns out to be available.
        TrackLoadResult result = await audio.Tracks
            .LoadTracksAsync(seed, TrackSearchMode.YouTubeMusic)
            .ConfigureAwait(false);

        if (!result.HasMatches)
        {
            return;
        }

        var next = result.Tracks
            .FirstOrDefault(t => t.Uri?.ToString() != ended.Uri?.ToString());

        if (next is null)
        {
            return;
        }

        // Requester 0 = the bot: nobody asked for this one, so /remove ownership does not apply.
        await player
            .PlayAsync(new SonarrQueueItem(new TrackReference(next), 0), enqueue: false)
            .ConfigureAwait(false);
    }

    private static ulong RequesterOf(ILavalinkPlayer player)
        => player is SonarrPlayer sonarr && sonarr.CurrentItem is { } item
            ? TrackMapping.Required(item).RequesterId
            : 0;
}
