using System.Text.Json;
using Discord.WebSocket;
using Lavalink4NET;
using Sonarr.Bot.Discord.Music;
using Sonarr.Domain.Caching;

namespace Sonarr.Bot.Discord.Web;

/// <summary>
/// Writes the live half of the status page to <c>web:live_status</c> every 5 s
/// (docs/08-background-services.md, docs/09-web-panels.md).
/// </summary>
/// <remarks>
/// A timer rather than computing on request: the cost is then fixed regardless of how many status
/// pages are open, which is the whole point of the checklist's "at most once per 5 s". The key's
/// TTL is also 5 s, so a dead pusher makes the page say "unknown" instead of showing a latency
/// number from an hour ago.
/// </remarks>
public sealed class StatusPagePusher(
    DiscordSocketClient client,
    IAudioService audio,
    IWebSessionCache cache,
    ILogger<StatusPagePusher> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await PushAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>One pass. Internal so a test can drive it without the timer.</summary>
    internal async Task PushAsync(CancellationToken ct)
    {
        try
        {
            await cache.SetLiveStatusJsonAsync(JsonSerializer.Serialize(Snapshot()), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The status page going stale is not worth killing the loop over.
            log.LogWarning(ex, "Live status push failed");
        }
    }

    private object Snapshot() => new
    {
        at = DateTimeOffset.UtcNow,
        gateway = client.ConnectionState.ToString(),
        latencyMs = client.Latency,
        guilds = client.Guilds.Count,

        // Per-guild player state, no channel or requester names: this blob is served without auth,
        // so it carries counts and titles the guild is already hearing out loud, nothing more.
        players = audio.Players.Players.OfType<SonarrPlayer>().Select(p => new
        {
            guildId = p.GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state = p.State.ToString(),
            queued = p.Queue.Count,
            nowPlaying = p.CurrentItem?.Track?.Title,
        }),
    };
}
