using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Moderation;

namespace Sonarr.Bot.Discord.Moderation;

/// <summary>
/// AntiSpam v2 (docs/08-background-services.md): watches guild messages, asks
/// <see cref="IAntiSpamService"/> for a verdict, and applies the guild's configured action with a
/// case row behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hot path first.</b> Every message in every guild passes through here, on a Pentium J2900.
/// Bots, DMs, system messages and moderators are rejected before any service call, and the
/// service short-circuits again on policy before it touches Redis.
/// </para>
/// <para>
/// <b>Never blocks the gateway.</b> Discord.Net awaits event handlers, so the work runs on a
/// detached task and every exception is swallowed into a log line — a spam check must not stall
/// message dispatch or take the connection down.
/// </para>
/// <para>
/// <b>No message content is logged</b>, here or in the case row (docs/06, hard rule 1).
/// </para>
/// </remarks>
public sealed class AntiSpamHandler(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ILogger<AntiSpamHandler> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived += OnMessageAsync;
        log.LogInformation("AntiSpam watching guild messages");
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
            || user.Channel is not SocketTextChannel channel)
        {
            return Task.CompletedTask;
        }

        // Detached on purpose: the gateway must not wait on Redis, Postgres or a REST call.
        _ = Task.Run(() => EvaluateAsync(user, channel));
        return Task.CompletedTask;
    }

    private async Task EvaluateAsync(SocketUserMessage message, SocketTextChannel channel)
    {
        try
        {
            SocketGuild guild = channel.Guild;
            SocketGuildUser? author = message.Author as SocketGuildUser ?? guild.GetUser(message.Author.Id);

            var isModerator = author is not null
                && (author.GuildPermissions.ManageMessages
                    || author.GuildPermissions.Administrator
                    || author.Id == guild.OwnerId);

            SpamCandidate candidate = new(
                guild.Id,
                channel.Id,
                message.Author.Id,
                message.Content ?? string.Empty,
                message.MentionedUsers.Count,
                message.MentionedRoles.Count,
                message.MentionedEveryone,
                AuthorIsBot: false,
                AuthorIsModerator: isModerator);

            using IServiceScope scope = scopes.CreateScope();
            var antiSpam = scope.ServiceProvider.GetRequiredService<IAntiSpamService>();

            SpamVerdict verdict = await antiSpam.EvaluateAsync(candidate).ConfigureAwait(false);
            if (!verdict.IsSpam)
            {
                return;
            }

            if (verdict.DeleteMessage)
            {
                await DeleteQuietlyAsync(message).ConfigureAwait(false);
            }

            var moderation = scope.ServiceProvider.GetRequiredService<IModerationService>();
            await ActAsync(moderation, guild, message.Author, author, verdict).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A spam check is best-effort. Losing one is survivable; losing the gateway is not.
            log.LogError(ex, "AntiSpam evaluation failed in guild {GuildId}", channel.Guild.Id);
        }
    }

    private async Task ActAsync(
        IModerationService moderation,
        SocketGuild guild,
        IUser target,
        SocketGuildUser? member,
        SpamVerdict verdict)
    {
        SocketGuildUser? self = guild.CurrentUser;
        if (self is null)
        {
            return;
        }

        (CaseAction action, GuildPermission permission, Func<CancellationToken, Task>? effect, DateTimeOffset? expires)
            = Plan(verdict, guild, member, self);

        // Sonarr is its own actor here, and it is subject to the same hierarchy guard as a human:
        // the snapshot below treats the bot as both actor and enforcer.
        HierarchySnapshot hierarchy = new(
            ActorHasPermission: self.GuildPermissions.Has(permission) || self.GuildPermissions.Administrator,
            BotHasPermission: self.GuildPermissions.Has(permission) || self.GuildPermissions.Administrator,
            ActorIsOwner: false,
            ActorTopRole: TopRole(self),
            BotTopRole: TopRole(self),
            TargetTopRole: member is null ? null : TopRole(member),
            TargetIsOwner: target.Id == guild.OwnerId,
            TargetIsBot: target.Id == self.Id);

        ModerationOutcome outcome = await moderation.ApplyAsync(
            new NewCase(
                guild.Id,
                target.Id,
                self.Id,
                action,
                verdict.Reason,
                expires,
                verdict.Detail),
            hierarchy,
            effect).ConfigureAwait(false);

        if (!outcome.Allowed)
        {
            log.LogInformation(
                "AntiSpam {Trigger} on {TargetId} in {GuildId} not enforced: {Denial}",
                verdict.Trigger, target.Id, guild.Id, outcome.Denial);
        }
    }

    private (CaseAction Action, GuildPermission Permission, Func<CancellationToken, Task>? Effect, DateTimeOffset? Expires)
        Plan(SpamVerdict verdict, SocketGuild guild, SocketGuildUser? member, SocketGuildUser self)
    {
        switch (verdict.Action)
        {
            case SpamAction.Timeout when member is not null:
                TimeSpan span = verdict.TimeoutFor ?? ModerationPolicy.Default.Timeout;
                return (CaseAction.Timeout, GuildPermission.ModerateMembers,
                    ct => member.SetTimeOutAsync(span, Options(verdict)),
                    DateTimeOffset.UtcNow + span);

            case SpamAction.Kick when member is not null:
                return (CaseAction.Kick, GuildPermission.KickMembers,
                    ct => member.KickAsync(verdict.Reason), null);

            case SpamAction.Ban:
                return (CaseAction.Ban, GuildPermission.BanMembers,
                    ct => guild.AddBanAsync(member?.Id ?? self.Id, pruneDays: 0, reason: verdict.Reason), null);

            case SpamAction.Warn:
                return (CaseAction.Warn, GuildPermission.ModerateMembers, null, null);

            default:
                // Note, or a member-only action against someone who already left: case row only.
                return (CaseAction.Note, GuildPermission.ManageMessages, null, null);
        }
    }

    private async Task DeleteQuietlyAsync(SocketUserMessage message)
    {
        try
        {
            await message.DeleteAsync().ConfigureAwait(false);
        }
        // global:: because this file's own namespace starts with Sonarr.Bot.Discord, so a plain
        // 'Discord.Net' binds to Sonarr.Bot.Discord.Net.
        catch (global::Discord.Net.HttpException ex)
        {
            // Already gone, or Sonarr lacks Manage Messages here. Neither is worth failing over.
            log.LogDebug(ex, "AntiSpam could not delete message {MessageId}", message.Id);
        }
    }

    private static RequestOptions Options(SpamVerdict verdict)
        => new() { AuditLogReason = verdict.Reason };

    private static int TopRole(SocketGuildUser member)
        => member.Roles.Count == 0 ? 0 : member.Roles.Max(r => r.Position);
}
