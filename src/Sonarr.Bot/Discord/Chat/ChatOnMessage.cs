using Discord;
using Discord.WebSocket;
using Sonarr.Application.Chat;

namespace Sonarr.Bot.Discord.Chat;

/// <summary>
/// The chat gateway handler (docs/08 — event-driven, not a timer): decides whether a message is
/// addressed to Sonarr, runs it through <see cref="IChatPipeline"/>, then paces and posts.
/// </summary>
/// <remarks>
/// <para><b>Hot path.</b> Every guild message arrives here on a Pentium J2900, so bots, webhooks
/// and DMs are dropped before any service call, and the pipeline itself checks the kill switch and
/// the engagement budget before touching Postgres.</para>
/// <para><b>Never blocks the gateway.</b> The work is detached and every exception becomes a log
/// line — a chat reply is not worth stalling message dispatch for.</para>
/// <para><b>No message content is logged.</b> The text goes to the engine and to nothing else;
/// what reaches Redis is a hash, and what reaches Postgres as an episode is her own line.</para>
/// </remarks>
public sealed class ChatOnMessage(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ILogger<ChatOnMessage> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived += OnMessageAsync;
        client.MessageUpdated += OnMessageUpdatedAsync;
        log.LogInformation("Chat watching guild messages for mentions, replies and stealth edits");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived -= OnMessageAsync;
        client.MessageUpdated -= OnMessageUpdatedAsync;
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

        _ = Task.Run(() => RespondAsync(user, channel));
        return Task.CompletedTask;
    }

    private Task OnMessageUpdatedAsync(
        Cacheable<IMessage, ulong> before,
        SocketMessage after,
        ISocketMessageChannel source)
    {
        if (after is not SocketUserMessage user
            || user.Author.IsBot
            || user.Author.IsWebhook
            || source is not SocketTextChannel channel)
        {
            return Task.CompletedTask;
        }

        _ = Task.Run(() => CallOutEditAsync(user, channel));
        return Task.CompletedTask;
    }

    private async Task CallOutEditAsync(SocketUserMessage message, SocketTextChannel channel)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            var watcher = scope.ServiceProvider.GetRequiredService<ChatEditWatcher>();

            ChatDecision decision = await watcher.HandleAsync(new ChatEdit(
                channel.Guild.Id,
                channel.Id,
                message.Author.Id,
                message.Id,
                message.Content)).ConfigureAwait(false);

            if (decision.Text is null)
            {
                return;
            }

            await SendAsync(decision, message, channel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Chat edit call-out failed in {GuildId}", channel.Guild.Id);
        }
    }

    private async Task RespondAsync(SocketUserMessage message, SocketTextChannel channel)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<IChatPipeline>();

            ChatDecision decision = await pipeline.HandleAsync(new ChatRequest(
                channel.Guild.Id,
                channel.Id,
                message.Author.Id,
                message.Id,
                message.Content,
                IsAddressed(message))).ConfigureAwait(false);

            if (decision.Reaction is { } emoji && Emoji.TryParse(emoji, out Emoji parsed))
            {
                await message.AddReactionAsync(parsed).ConfigureAwait(false);
            }

            if (decision.Text is null)
            {
                return;
            }

            await SendAsync(decision, message, channel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Losing one reply is survivable; losing the gateway is not.
            log.LogError(ex, "Chat reply failed for {UserId} in {GuildId}", message.Author.Id, channel.Guild.Id);
        }
    }

    /// <summary>
    /// Paces then posts, as a reply to the message that prompted it and with mentions inert — she
    /// never pings anyone by quoting them back.
    /// </summary>
    private static async Task SendAsync(
        ChatDecision decision,
        SocketUserMessage message,
        SocketTextChannel channel)
    {
        // Typing indicator for the pacing delay, so the pause reads as her writing rather than as
        // the bot being slow.
        using (channel.EnterTypingState())
        {
            await Task.Delay(decision.TypingDelay).ConfigureAwait(false);
        }

        await channel.SendMessageAsync(
            decision.Text,
            messageReference: new MessageReference(message.Id),
            allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    /// <summary>
    /// She answers a mention or a reply to one of her own messages, and nothing else — docs/10's
    /// gate. A role mention counts; @everyone deliberately does not, or every announcement would
    /// summon her.
    /// </summary>
    private bool IsAddressed(SocketUserMessage message)
    {
        if (message.MentionedUsers.Any(u => u.Id == client.CurrentUser.Id))
        {
            return true;
        }

        return message.ReferencedMessage?.Author.Id == client.CurrentUser.Id;
    }
}
