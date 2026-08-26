using System.Collections.Concurrent;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Microsoft.Extensions.Logging;
using Sonarr.Application.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>
/// The Lavalink half of <see cref="IVoicePlayerGateway"/>. Everything that needs a live gateway
/// lives here so <c>VoiceMoveCoordinator</c> — including the drag-to-another-channel path — stays
/// unit-testable against a fake.
/// </summary>
public sealed class VoicePlayerGateway(
    IAudioService audio,
    IDiscordClientWrapper client,
    ILogger<VoicePlayerGateway> logger) : IVoicePlayerGateway, IDisposable
{
    /// <summary>One pending disconnect per guild; re-scheduling replaces the previous timer.</summary>
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _idle = new();

    public async ValueTask<ulong?> GetPlayerChannelAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await audio.Players.GetPlayerAsync<SonarrPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);

        return player?.VoiceChannelId;
    }

    public async ValueTask ReconnectAsync(
        ulong guildId, ulong voiceChannelId, CancellationToken cancellationToken = default)
    {
        // A drag moves the session without telling the node. Re-sending the voice update points
        // the existing player at the new channel and playback continues from the same position.
        await client.SendVoiceUpdateAsync(guildId, voiceChannelId, selfDeaf: true, selfMute: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Humans in the channel, or <see cref="IVoicePlayerGateway.UnknownListeners"/> when the socket
    /// cache cannot answer.
    /// </summary>
    /// <remarks>
    /// The wrapper reads Discord.Net's socket cache, and a cache that has not caught up returns an
    /// empty list for a channel full of people — indistinguishable from a channel that really is
    /// empty, and "empty" is what pauses the music and arms the leave timer. That is half of why a
    /// freshly started track went silent (see <c>VoiceMoveCoordinator</c>).
    ///
    /// So an empty result is cross-examined: we are bound to this channel, therefore the cache
    /// must at minimum see <em>us</em> in it. Asking again with bots included and still getting
    /// nothing means the cache does not know the channel at all, not that the channel is empty.
    /// Only the second call distinguishes the two, and it only runs on the empty path.
    /// </remarks>
    public async ValueTask<int> CountListenersAsync(
        ulong guildId, ulong voiceChannelId, CancellationToken cancellationToken = default)
    {
        try
        {
            var humans = await client
                .GetChannelUsersAsync(guildId, voiceChannelId, includeBots: false, cancellationToken)
                .ConfigureAwait(false);

            if (humans.Length > 0)
            {
                return humans.Length;
            }

            var everyone = await client
                .GetChannelUsersAsync(guildId, voiceChannelId, includeBots: true, cancellationToken)
                .ConfigureAwait(false);

            if (everyone.Length == 0)
            {
                logger.LogDebug(
                    "Voice channel {ChannelId} in guild {GuildId} is not in the member cache — "
                    + "occupancy unknown, not empty.",
                    voiceChannelId, guildId);
                return IVoicePlayerGateway.UnknownListeners;
            }

            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not "someone is there" and not "nobody is there" — we genuinely do not know, and the
            // coordinator is the one that decides what to do with that.
            logger.LogDebug(ex, "Could not count listeners in {ChannelId}", voiceChannelId);
            return IVoicePlayerGateway.UnknownListeners;
        }
    }

    public async ValueTask<bool> IsPausedAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await audio.Players.GetPlayerAsync<SonarrPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);

        return player?.IsPaused ?? false;
    }

    public async ValueTask PauseAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await audio.Players.GetPlayerAsync<SonarrPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);

        if (player is not null)
        {
            await player.PauseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ResumeAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await audio.Players.GetPlayerAsync<SonarrPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);

        if (player is not null)
        {
            await player.ResumeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisconnectAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        CancelIdleDisconnect(guildId);

        var player = await audio.Players.GetPlayerAsync<SonarrPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);

        if (player is not null)
        {
            await player.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            await player.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void ScheduleIdleDisconnect(ulong guildId, TimeSpan delay)
    {
        var cts = new CancellationTokenSource();
        if (_idle.TryRemove(guildId, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        _idle[guildId] = cts;

        // ponytail: in-process timer, so a restart during the 5-minute window leaves the bot
        // parked in an empty channel until someone runs /stop. Upgrade path: a Redis-backed
        // sweep in MusicSessionSnapshotter if that ever actually annoys anyone.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                await DisconnectAsync(guildId, CancellationToken.None).ConfigureAwait(false);
                logger.LogInformation("Left voice in guild {GuildId} after {Delay} alone", guildId, delay);
            }
            catch (OperationCanceledException)
            {
                // Somebody came back. That is the happy path.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Idle disconnect failed for guild {GuildId}", guildId);
            }
        }, CancellationToken.None);
    }

    public void CancelIdleDisconnect(ulong guildId)
    {
        if (_idle.TryRemove(guildId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var cts in _idle.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _idle.Clear();
    }
}
