using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Sonarr.Application.Music;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>Queue shape: paging, removal, duplicates, shuffle, loop, fair queue, now-playing, ratings.</summary>
public sealed partial class MusicService
{
    public async Task<QueuePage?> GetQueueAsync(
        MusicContext context, int page = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        SonarrPlayer? player = await FindPlayerAsync(context.GuildId, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        return QueuePlanner.Page(TrackMapping.ToDomain(player.CurrentItem), Snapshot(player), page);
    }

    public async Task<MusicResult> RemoveAsync(
        MusicContext context, int position, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        if (position < 1 || position > player.Queue.Count)
        {
            return MusicResult.Fail(player.Queue.Count == 0
                ? "The queue is empty."
                : $"Pick a position between 1 and {player.Queue.Count}.");
        }

        ITrackQueueItem item = player.Queue[position - 1];
        TrackInfo track = TrackMapping.Required(item);

        // Anyone can pull their own track; someone else's needs a DJ.
        if (track.RequesterId != context.UserId && !context.IsDj)
        {
            return MusicResult.Fail("That's someone else's track — only they or a DJ can remove it.");
        }

        await player.Queue.RemoveAtAsync(position - 1, cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok($"Removed **{track.Title}**.");
    }

    public async Task<MusicResult> RemoveDuplicatesAsync(
        MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken, djOnly: true).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        IReadOnlyList<int> positions = QueuePlanner.DuplicatePositions(Snapshot(player));
        if (positions.Count == 0)
        {
            return MusicResult.Ok("No duplicates in the queue.");
        }

        // DuplicatePositions comes back descending, so indexes stay valid as we remove.
        foreach (var position in positions)
        {
            await player.Queue.RemoveAtAsync(position - 1, cancellationToken).ConfigureAwait(false);
        }

        return MusicResult.Ok($"Removed **{positions.Count}** duplicate{(positions.Count == 1 ? string.Empty : "s")}.");
    }

    public async Task<MusicResult> ShuffleAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        if (player.Queue.Count < 2)
        {
            return MusicResult.Fail("Not enough in the queue to shuffle.");
        }

        await player.Queue.ShuffleAsync(cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok($"Shuffled **{player.Queue.Count}** tracks.");
    }

    public async Task<MusicResult> SetLoopAsync(
        MusicContext context, LoopMode mode, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        player.RepeatMode = mode switch
        {
            LoopMode.Track => TrackRepeatMode.Track,
            LoopMode.Queue => TrackRepeatMode.Queue,
            _ => TrackRepeatMode.None,
        };

        return MusicResult.Ok(mode switch
        {
            LoopMode.Track => "Looping this track.",
            LoopMode.Queue => "Looping the queue.",
            _ => "Loop off.",
        });
    }

    public async Task<MusicResult> SetFairQueueAsync(
        MusicContext context, bool enabled, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken, djOnly: true).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        player.FairQueue = enabled;
        if (enabled)
        {
            await ReorderFairlyAsync(player, cancellationToken).ConfigureAwait(false);
        }

        return MusicResult.Ok(enabled
            ? "Fair queue on — one track each, round-robin."
            : "Fair queue off.");
    }

    public async Task<MusicResult> SetAutoplayAsync(
        MusicContext context, AutoplayMode mode, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken, djOnly: true).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        player.AutoplayMode = mode;
        return MusicResult.Ok(mode switch
        {
            AutoplayMode.On => "Autoplay on — I'll keep the music going when the queue runs out.",
            AutoplayMode.Smart => "Autoplay set to smart — seeded from what this channel actually likes.",
            _ => "Autoplay off.",
        });
    }

    public async Task<NowPlayingView?> GetNowPlayingAsync(
        MusicContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        SonarrPlayer? player = await FindPlayerAsync(context.GuildId, cancellationToken).ConfigureAwait(false);
        if (player?.CurrentItem is null)
        {
            return null;
        }

        TrackInfo current = TrackMapping.Required(player.CurrentItem);
        IReadOnlyList<TrackInfo> queue = Snapshot(player);

        RatedTrack? rating = await stats.GetRatingAsync(context.GuildId, current.Uri, cancellationToken)
            .ConfigureAwait(false);

        return new NowPlayingView(
            current,
            player.Position?.Position ?? TimeSpan.Zero,
            player.IsPaused,
            player.VolumePercent,
            player.Loop,
            queue.Count,
            QueuePlanner.TracksUntil(queue, context.UserId),
            rating?.Likes ?? 0,
            rating?.Dislikes ?? 0);
    }

    public async Task<MusicResult> RateCurrentAsync(
        MusicContext context, short vote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (vote is not (1 or -1))
        {
            return MusicResult.Fail("A rating is a thumb up or down, nothing else.");
        }

        SonarrPlayer? player = await FindPlayerAsync(context.GuildId, cancellationToken).ConfigureAwait(false);
        if (player?.CurrentItem is null)
        {
            return MusicResult.Fail("Nothing is playing.");
        }

        TrackInfo current = TrackMapping.Required(player.CurrentItem);
        RatedTrack tally = await stats
            .RateAsync(context.GuildId, current.Uri, current.Title, context.UserId, vote, cancellationToken)
            .ConfigureAwait(false);

        return MusicResult.Ok(vote == 1
            ? $"Noted — **{tally.Likes}** up, **{tally.Dislikes}** down."
            : $"Noted — **{tally.Dislikes}** down, **{tally.Likes}** up.");
    }

    public async Task<TrackInfo?> GrabAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        SonarrPlayer? player = await FindPlayerAsync(context.GuildId, cancellationToken).ConfigureAwait(false);
        return player?.CurrentItem is null ? null : TrackMapping.Required(player.CurrentItem);
    }

    /// <summary>The queue as domain tracks. One allocation, used by every read above.</summary>
    private static IReadOnlyList<TrackInfo> Snapshot(SonarrPlayer player) => TrackMapping.ToDomain(player.Queue);

    /// <summary>
    /// Rebuilds the queue in round-robin requester order. Clear-then-add because
    /// <see cref="ITrackQueue"/> has no reorder; the queue is tens of items, not thousands.
    /// </summary>
    private static async Task ReorderFairlyAsync(SonarrPlayer player, CancellationToken ct)
    {
        if (player.Queue.Count < 2)
        {
            return;
        }

        IReadOnlyList<TrackInfo> fair = QueuePlanner.FairOrder(Snapshot(player));
        await player.Queue.ClearAsync(ct).ConfigureAwait(false);
        await player.Queue
            .AddRangeAsync([.. fair.Select(t => (ITrackQueueItem)TrackMapping.FromDomain(t))], ct)
            .ConfigureAwait(false);
    }
}
