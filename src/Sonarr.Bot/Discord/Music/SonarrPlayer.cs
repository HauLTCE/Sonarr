using System.Net.WebSockets;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<SonarrPlayer> _logger = properties.Logger;

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

    /// <summary>
    /// Discord closed the voice websocket underneath us. Logged loudly because the interesting
    /// codes are silent otherwise: the player object keeps reporting itself as playing.
    /// </summary>
    /// <remarks>
    /// 4006 is the one that mattered — "session no longer valid", which is what a second voice
    /// update for an already-connected channel earns you. Audio stops, nothing throws, and
    /// <c>/play</c> looks like it worked. Without this override that failure produced no log line
    /// at all, which is why it took a kick-and-retry dance to diagnose instead of a grep.
    /// See <c>VoiceMoveCoordinator</c> for the guard that stopped provoking it.
    /// </remarks>
    protected override ValueTask NotifyWebSocketClosedAsync(
        WebSocketCloseStatus closeStatus,
        string reason,
        bool byRemote,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var code = (int)closeStatus;

        // 4014 (disconnected) and 4009 (session timeout) are routine — someone moved us, or the
        // node is reconnecting on its own. Everything else is a symptom worth a warning.
        if (code is 4014 or 4009)
        {
            _logger.LogInformation(
                "Voice websocket closed for guild {GuildId}: {Code} {Reason} (byRemote={ByRemote}).",
                GuildId, code, reason, byRemote);
        }
        else
        {
            _logger.LogWarning(
                "Voice websocket closed for guild {GuildId}: {Code} {Reason} (byRemote={ByRemote}). "
                + "Audio has stopped even though the player still reports itself connected.",
                GuildId, code, reason, byRemote);
        }

        return base.NotifyWebSocketClosedAsync(closeStatus, reason, byRemote, cancellationToken);
    }
}
