using Lavalink4NET;
using Sonarr.Application.Music;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>
/// Writes every live player to <c>music:session:{guild}</c> every 30 s so <c>/restore-queue</c> can rebuild
/// the queue and position after a crash (docs/08-background-services.md).
/// </summary>
/// <remarks>
/// Snapshots are best-effort: the cache fails open, and a missing snapshot only costs a resume.
/// The TTL comes from <c>CacheTtl.MusicSession</c> (24 h), so an abandoned session expires itself.
/// </remarks>
public sealed class MusicSessionSnapshotter(
    IAudioService audio,
    IMusicSessionCache cache,
    ILogger<MusicSessionSnapshotter> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation(
            "MusicSessionSnapshotter saving every {Seconds}s", MusicRules.SnapshotInterval.TotalSeconds);

        using PeriodicTimer timer = new(MusicRules.SnapshotInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await SnapshotAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>One pass over the live players. Internal so a test can drive it without the timer.</summary>
    internal async Task SnapshotAsync(CancellationToken ct)
    {
        try
        {
            foreach (var player in audio.Players.Players.OfType<SonarrPlayer>())
            {
                if (player.CurrentItem is null)
                {
                    continue;
                }

                var snapshot = new MusicSessionSnapshot
                {
                    VoiceChannelId = player.VoiceChannelId,
                    TextChannelId = player.TextChannelId,
                    Current = TrackMapping.ToDomain(player.CurrentItem),
                    PositionMs = (long)(player.Position?.Position.TotalMilliseconds ?? 0),
                    Volume = player.VolumePercent,
                    Loop = player.Loop,
                    Queue = TrackMapping.ToDomain(player.Queue),
                    SavedAt = DateTimeOffset.UtcNow,
                };

                await cache.SaveSessionAsync(player.GuildId, snapshot, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A lost snapshot costs a /restore-queue, nothing else. Not worth failing the service over.
            log.LogWarning(ex, "Music session snapshot pass failed");
        }
    }
}
