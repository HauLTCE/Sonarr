using System.Globalization;
using Discord;
using Discord.Interactions;
using Sonarr.Application.Utility;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/ticket</c> (docs/07-commands.md#server-management): a private thread with the mod team, and a
/// close button that saves a transcript.
/// </summary>
/// <remarks>
/// The thread is private (<see cref="ThreadType.PrivateThread"/>), so it is invisible to the rest of
/// the channel and its members are added explicitly — the opener, plus whoever staff pulls in. The
/// transcript is read back from the thread at close time rather than mirrored message-by-message
/// into Postgres: docs/06 stores what a user explicitly saves, and a live ticket is not that.
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Tickets)]
public sealed class TicketModule(TicketService tickets, IGuildConfigService config)
    : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Component id — <c>ticket:close:{ticketId}</c>.</summary>
    public const string ClosePrefix = "ticket:close";

    /// <summary>Mirrors <c>TicketService.MaxTopicLength</c>; the service is still the authority.</summary>
    private const int MaxTopicLength = 80;

    /// <summary>How far back the transcript reads. Past this a ticket is a project, not a question.</summary>
    private const int TranscriptMessageLimit = 500;

    [SlashCommand("ticket", "Open a private thread with the mod team.")]
    public async Task OpenAsync([Summary("topic", "What it's about, briefly")] string topic)
    {
        if (InputGuards.Length(topic, MaxTopicLength, "topic") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        if (await tickets.CheckAsync(Context.Guild.Id, Context.User.Id, topic) is { } refusal)
        {
            await RespondInvalidAsync(refusal);
            return;
        }

        if (Context.Channel is not ITextChannel channel)
        {
            await RespondInvalidAsync("Run this in a normal text channel — I can't start a thread here.");
            return;
        }

        if (Context.Guild.CurrentUser is { } self
            && !self.GetPermissions(channel).CreatePrivateThreads)
        {
            await RespondInvalidAsync("I can't create private threads here — ask staff to check my permissions.");
            return;
        }

        // Deferred and ephemeral: creating a thread and adding members is several round trips, and
        // the whole point is that the channel does not see this.
        await DeferAsync(ephemeral: true);

        IThreadChannel thread;
        try
        {
            thread = await channel.CreateThreadAsync(
                TicketService.ThreadName(topic),
                ThreadType.PrivateThread,
                // Auto-archive is the safety net for a ticket nobody closes.
                autoArchiveDuration: ThreadArchiveDuration.OneWeek,
                invitable: false);

            await thread.AddUserAsync((IGuildUser)Context.User);
        }
        catch (global::Discord.Net.HttpException ex)
        {
            await FollowupAsync($"Couldn't open the thread — {ex.Reason ?? "Discord said no"}.", ephemeral: true);
            return;
        }

        TicketResult result = await tickets.RecordAsync(Context.Guild.Id, Context.User.Id, thread.Id);

        Embed embed = new EmbedBuilder()
            .WithTitle($"Ticket #{result.TicketId}")
            .WithDescription(Format.Sanitize(topic.Trim()))
            .WithColor(new Color(0x3BA55D))
            .WithFooter($"opened by {Context.User.Username} · close saves a transcript")
            .WithCurrentTimestamp()
            .Build();

        MessageComponent close = new ComponentBuilder()
            .WithButton("Close ticket", $"{ClosePrefix}:{result.TicketId}", ButtonStyle.Danger)
            .Build();

        await thread.SendMessageAsync(embed: embed, components: close, allowedMentions: AllowedMentions.None);

        await FollowupAsync(
            $"{result.Message} {MentionUtils.MentionChannel(thread.Id)}",
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction($"{ClosePrefix}:*", ignoreGroupNames: true)]
    public async Task CloseAsync(string rawTicketId)
    {
        if (!long.TryParse(rawTicketId, CultureInfo.InvariantCulture, out long ticketId))
        {
            await RespondInvalidAsync("That button is from an older message.");
            return;
        }

        if (await tickets.GetOpenAsync(Context.Channel.Id) is not { } ticket
            || ticket.TicketId != ticketId
            || ticket.GuildId != (long)Context.Guild.Id)
        {
            await RespondInvalidAsync("This ticket is already closed.");
            return;
        }

        // The opener or staff. A member who was pulled into the thread is neither.
        bool isStaff = Context.User is IGuildUser { GuildPermissions.ManageMessages: true };
        if (!isStaff && ticket.OpenerId != (long)Context.User.Id)
        {
            await RespondInvalidAsync("Only the person who opened this, or a mod, can close it.");
            return;
        }

        await DeferAsync();

        string? transcriptRef = await SaveTranscriptAsync(ticket);

        if (!await tickets.CloseAsync(ticket.TicketId, Context.User.Id, transcriptRef))
        {
            // Lost the race with another click. The other one posted the transcript.
            await FollowupAsync("Already closed.", ephemeral: true);
            return;
        }

        await FollowupAsync($"Closed by {Context.User.Mention}.", allowedMentions: AllowedMentions.None);

        if (Context.Channel is IThreadChannel thread)
        {
            await thread.ModifyAsync(t =>
            {
                t.Archived = true;
                t.Locked = true;
            });
        }
    }

    /// <summary>
    /// Reads the thread back and posts the transcript to the mod log, returning the message link
    /// stored in <c>transcript_ref</c>. Null when there is no log channel or the post failed — the
    /// ticket still closes, because refusing to close it would trap the thread.
    /// </summary>
    private async Task<string?> SaveTranscriptAsync(Ticket ticket)
    {
        if (await config.GetAsync(Context.Guild.Id, ConfigKeys.LogChannel) is not { } value
            || !ulong.TryParse(value.Raw, CultureInfo.InvariantCulture, out ulong logChannelId)
            || Context.Guild.GetTextChannel(logChannelId) is not { } logChannel)
        {
            return null;
        }

        try
        {
            List<TranscriptLine> lines = [];
            await foreach (var page in Context.Channel
                .GetMessagesAsync(TranscriptMessageLimit)
                .WithCancellation(CancellationToken.None))
            {
                lines.AddRange(page
                    .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                    .Select(m => new TranscriptLine(m.CreatedAt, m.Author.Username, m.Content)));
            }

            // Discord pages newest-first; a transcript reads forwards.
            lines.Reverse();

            using MemoryStream file = new(
                System.Text.Encoding.UTF8.GetBytes(TicketTranscript.Render(ticket.TicketId, lines)));

            IUserMessage posted = await logChannel.SendFileAsync(
                file,
                $"ticket-{ticket.TicketId}.txt",
                text: $"Ticket #{ticket.TicketId} closed by {MentionUtils.MentionUser(Context.User.Id)} "
                    + $"(opened by {MentionUtils.MentionUser((ulong)ticket.OpenerId)})",
                allowedMentions: AllowedMentions.None);

            return posted.GetJumpUrl();
        }
        catch (global::Discord.Net.HttpException)
        {
            // No permission in the log channel, or the read failed. Not worth trapping the thread over.
            return null;
        }
    }
}
