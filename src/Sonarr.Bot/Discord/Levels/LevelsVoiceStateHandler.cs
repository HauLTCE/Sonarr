using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Levels;

namespace Sonarr.Bot.Discord.Levels;

/// <summary>
/// Keeps the levels module's voice sessions in step with the gateway: join opens one, leave closes
/// it, a move repoints it, and a mute or an occupancy change updates the flags the anti-AFK rule
/// reads.
/// </summary>
/// <remarks>
/// The music module has its own <c>UserVoiceStateUpdated</c> subscriber. Two independent handlers
/// on one event is the intended shape — neither area's needs bend the other's file, and Discord.Net
/// fans the event out to both.
/// </remarks>
public sealed class LevelsVoiceStateHandler(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ILogger<LevelsVoiceStateHandler> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.UserVoiceStateUpdated += OnVoiceStateAsync;
        log.LogInformation("Levels tracking voice sessions");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.UserVoiceStateUpdated -= OnVoiceStateAsync;
        return Task.CompletedTask;
    }

    private Task OnVoiceStateAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (user.IsBot)
        {
            return Task.CompletedTask;
        }

        // Detached on purpose: the gateway must not wait on Redis.
        _ = Task.Run(() => TrackAsync(user.Id, before, after));
        return Task.CompletedTask;
    }

    private async Task TrackAsync(ulong userId, SocketVoiceState before, SocketVoiceState after)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<IVoiceSessionTracker>();

            SocketVoiceChannel? left = before.VoiceChannel;
            SocketVoiceChannel? joined = after.VoiceChannel;

            if (joined is null)
            {
                if (left is not null)
                {
                    await tracker.EndAsync(left.Guild.Id, userId).ConfigureAwait(false);
                    await RefreshPeersAsync(tracker, left, userId).ConfigureAwait(false);
                }

                return;
            }

            if (left is null || left.Id != joined.Id)
            {
                await tracker.BeginAsync(
                    joined.Guild.Id, userId, joined.Id, Humans(joined), IsMuted(after)).ConfigureAwait(false);

                if (left is not null)
                {
                    await RefreshPeersAsync(tracker, left, userId).ConfigureAwait(false);
                }
            }
            else
            {
                // Same channel: a mute, deaf or stream toggle. Keep the session anchor.
                await tracker.UpdateAsync(
                    joined.Guild.Id, userId, Humans(joined), IsMuted(after)).ConfigureAwait(false);
            }

            await RefreshPeersAsync(tracker, joined, userId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Voice session tracking failed for {UserId}", userId);
        }
    }

    /// <summary>
    /// One person joining or leaving changes whether everyone else in the channel is alone, and the
    /// anti-AFK rule keys on that. The channels involved hold a handful of people, so refreshing
    /// them beats waiting for the next accrual tick to notice.
    /// </summary>
    private static async Task RefreshPeersAsync(
        IVoiceSessionTracker tracker, SocketVoiceChannel channel, ulong except)
    {
        var humans = Humans(channel);
        foreach (SocketGuildUser peer in channel.ConnectedUsers)
        {
            if (peer.IsBot || peer.Id == except)
            {
                continue;
            }

            await tracker.UpdateAsync(channel.Guild.Id, peer.Id, humans, IsMuted(peer.VoiceState))
                .ConfigureAwait(false);
        }
    }

    internal static int Humans(SocketVoiceChannel channel)
        => channel.ConnectedUsers.Count(u => !u.IsBot);

    /// <summary>
    /// Self-mute, server-mute and self-deaf all mean "not participating" for XP purposes — a
    /// deafened listener is as AFK as a muted one.
    /// </summary>
    internal static bool IsMuted(IVoiceState? state)
        => state is null
           || state.IsMuted || state.IsSelfMuted || state.IsDeafened || state.IsSelfDeafened;
}
