using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sonarr.Application.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>
/// Turns Discord voice-state events into <see cref="VoiceMove"/> and hands them to
/// <see cref="VoiceMoveCoordinator"/>. Translation only — every decision (reconnect after a drag,
/// auto-pause on an empty channel, the 300 s leave) is in Application where it is testable.
/// </summary>
public sealed class VoiceStateWatcher(
    DiscordSocketClient client,
    VoiceMoveCoordinator coordinator,
    ILogger<VoiceStateWatcher> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.UserVoiceStateUpdated += OnVoiceStateUpdatedAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.UserVoiceStateUpdated -= OnVoiceStateUpdatedAsync;
        return Task.CompletedTask;
    }

    private async Task OnVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        var guildId = (before.VoiceChannel ?? after.VoiceChannel)?.Guild.Id;
        if (guildId is not { } guild)
        {
            return;
        }

        // Other bots in the channel are not listeners and not us; ignore them entirely.
        if (user.IsBot && user.Id != client.CurrentUser?.Id)
        {
            return;
        }

        var move = new VoiceMove(
            guild,
            user.Id,
            before.VoiceChannel?.Id,
            after.VoiceChannel?.Id,
            IsBot: user.Id == client.CurrentUser?.Id);

        try
        {
            await coordinator.HandleAsync(move, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The coordinator already swallows its own failures; this is the belt to its braces,
            // because an exception escaping a gateway handler kills the event loop.
            logger.LogError(ex, "Voice state handling failed for guild {GuildId}", guild);
        }
    }
}
