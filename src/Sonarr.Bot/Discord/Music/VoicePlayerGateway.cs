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

    public async ValueTask<int> CountListenersAsync(
        ulong guildId, ulong voiceChannelId, CancellationToken cancellationToken = default)
    {
        try
        {
            var users = await client
                .GetChannelUsersAsync(guildId, voiceChannelId, includeBots: false, cancellationToken)
                .ConfigureAwait(false);

            return users.Length;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unknown occupancy must not pause the music: treat it as "someone is there".
            logger.LogDebug(ex, "Could not count listeners in {ChannelId}", voiceChannelId);
            return 1;
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
