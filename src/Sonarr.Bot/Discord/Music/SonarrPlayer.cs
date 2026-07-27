using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>
/// One queued track plus who asked for it. Lavalink4NET's own queue item carries no requester,
/// and "who queued this" drives <c>/remove</c> ownership, fair-queue and tracks-until-yours.
/// </summary>
public sealed record SonarrQueueItem(TrackReference Reference, ulong RequesterId)
    : TrackQueueItem(Reference);

/// <summary>
/// The guild's player. Everything per-guild that Lavalink does not already track — the text
/// channel to post in, fair-queue and autoplay mode — hangs here rather than in a side
/// dictionary that can outlive the player.
/// </summary>
public sealed class SonarrPlayer(IPlayerProperties<SonarrPlayer, QueuedLavalinkPlayerOptions> properties)
    : QueuedLavalinkPlayer(properties)
{
    /// <summary>Where now-playing updates go. Set on every command so it follows the room.</summary>
    public ulong TextChannelId { get; set; }

    /// <summary><c>/fairqueue</c>: re-order the queue round-robin whenever tracks are added.</summary>
    public bool FairQueue { get; set; }

    public AutoplayMode AutoplayMode { get; set; }

    /// <summary>Volume as the user talks about it (0–150), not Lavalink's 0.0–1.5.</summary>
    public int VolumePercent => (int)Math.Round(Volume * 100f);

    public LoopMode Loop => RepeatMode switch
    {
        TrackRepeatMode.Track => LoopMode.Track,
        TrackRepeatMode.Queue => LoopMode.Queue,
        _ => LoopMode.Off,
    };
}
