using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Levels;

namespace Sonarr.Bot.Discord.Levels;

/// <summary>
/// Message XP (docs/08-background-services.md — "XP on message"): one award per user per 60 s,
/// the daily first-message bonus, the streak touch, then the level-up announcement and any role
/// rewards earned.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hot path.</b> Every guild message reaches here on a Pentium J2900. Bots, webhooks, DMs and
/// system messages are dropped before any service call; the feature gate and the Redis cooldown
/// are the next two cheap rejections inside the service.
/// </para>
/// <para>
/// <b>Never blocks the gateway.</b> The work is detached onto a task and every exception becomes a
/// log line — XP is not worth stalling message dispatch for.
/// </para>
/// <para><b>No message content is read or logged</b> — only the fact that a message happened.</para>
/// </remarks>
public sealed class XpOnMessage(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ActivityBuffer activity,
    ILogger<XpOnMessage> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived += OnMessageAsync;
        log.LogInformation("Levels watching guild messages for XP");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived -= OnMessageAsync;
        return Task.CompletedTask;
    }

    private Task OnMessageAsync(SocketMessage message)
    {
        if (message is not SocketUserMessage user
            || user.Author.IsBot
            || user.Author.IsWebhook
            || user.Channel is not SocketGuildChannel channel)
        {
            return Task.CompletedTask;
        }

        // Detached on purpose: the gateway must not wait on Redis, Postgres or a REST call.
        _ = Task.Run(() => AwardAsync(channel.Guild, channel.Id, user.Author.Id));
        return Task.CompletedTask;
    }

    private async Task AwardAsync(SocketGuild guild, ulong channelId, ulong userId)
    {
        try
        {
            // Message counts keep accruing even with levels switched off — /userstats reads them,
            // and they are not XP.
            //
            // The names and join date ride along because this flush is the only thing that
            // creates core.member rows: /birthday and /timezone are both UPDATEs that no-op
            // without one. GetUser is the local cache, never a REST call — null on a cache miss,
            // and the flush falls back to the message time for first_seen_at.
            SocketGuildUser? cached = guild.GetUser(userId);
            activity.Record(
                guild.Id,
                userId,
                cached?.Username ?? string.Empty,
                cached?.DisplayName ?? cached?.GlobalName ?? string.Empty,
                cached?.JoinedAt);

            using IServiceScope scope = scopes.CreateScope();
            var features = scope.ServiceProvider.GetRequiredService<IFeatureGate>();

            if (!await features.IsEnabledAsync(FeatureNames.Levels, guild.Id).ConfigureAwait(false))
            {
                return;
            }

            var levels = scope.ServiceProvider.GetRequiredService<ILevelService>();

            XpAward award = await levels.AwardMessageXpAsync(
                guild.Id, userId, channelId, DateOnly.FromDateTime(DateTime.UtcNow)).ConfigureAwait(false);

            if (!award.LeveledUp)
            {
                return;
            }

            LevelsPolicy policy = await levels.GetPolicyAsync(guild.Id).ConfigureAwait(false);

            await LevelUpNotice.AnnounceAsync(guild, channelId, cached, userId, award, policy, log)
                .ConfigureAwait(false);

            if (cached is not null && award.RewardRoleIds.Count > 0)
            {
                await LevelUpNotice.GrantRolesAsync(guild, cached, award.RewardRoleIds, log).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Losing one award is survivable; losing the gateway is not.
            log.LogError(ex, "Message XP failed for {UserId} in {GuildId}", userId, guild.Id);
        }
    }
}
