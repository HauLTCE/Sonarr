using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>
/// The one place Lavalink track types become domain <see cref="TrackInfo"/> and back. Keeping it
/// here is what lets <c>IMusicService</c>'s signature stay Lavalink-free.
/// </summary>
internal static class TrackMapping
{
    public static TrackInfo ToDomain(LavalinkTrack track, ulong requesterId) => new(
        track.Title,
        track.Author,
        track.Uri?.ToString() ?? track.Identifier,
        track.Identifier,
        (long)track.Duration.TotalMilliseconds,
        requesterId,
        track.ArtworkUri?.ToString());

    /// <summary>
    /// A queue item's domain view. Requester comes from <see cref="SonarrQueueItem"/> when the
    /// item is ours; 0 (= the bot) otherwise, which is what autoplay additions are.
    /// </summary>
    public static TrackInfo? ToDomain(ITrackQueueItem? item)
    {
        if (item?.Track is not { } track)
        {
            return null;
        }

        return ToDomain(track, (item as SonarrQueueItem)?.RequesterId ?? 0);
    }

    public static IReadOnlyList<TrackInfo> ToDomain(IEnumerable<ITrackQueueItem> items)
        => [.. items.Select(ToDomain).OfType<TrackInfo>()];

    /// <summary>
    /// The domain view of an item we know exists. An item can still carry only an identifier (a
    /// resumed queue that has not been re-resolved yet), so that case degrades to a title-less
    /// track rather than a null the callers would all have to re-check.
    /// </summary>
    public static TrackInfo Required(ITrackQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return ToDomain(item)
            ?? new TrackInfo(item.Identifier, string.Empty, item.Identifier, item.Identifier, 0,
                (item as SonarrQueueItem)?.RequesterId ?? 0);
    }

    /// <summary>
    /// Rebuilds a queue item from a snapshot row. The identifier is what Lavalink re-resolves,
    /// so <c>/restore-queue</c> costs one load per track instead of storing encoded blobs.
    /// </summary>
    public static SonarrQueueItem FromDomain(TrackInfo track)
        => new(new TrackReference(track.Identifier), track.RequesterId);
}
