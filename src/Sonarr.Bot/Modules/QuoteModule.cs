using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Social;

namespace Sonarr.Bot.Modules;

/// <summary>
/// The server quote board (docs/07-commands.md#utility): <c>/quote save</c>, <c>/quote random</c>,
/// <c>/quote delete</c>, and the "Save quote" message context menu. The chat engine recalls what
/// this fills (<c>QuoteBoardRecall</c>).
/// </summary>
/// <remarks>
/// <para>This is one of the three ways content is stored at all (docs/06): you talk to her, a mod
/// action captures context, or <b>you explicitly save it</b>. Saving is that explicit act, which is
/// why it is a command and never automatic — she does not mine the channel for quotable lines.</para>
/// <para>Replies are public, unlike <c>/memories</c>: a quote board is the server's shared joke, and
/// an ephemeral confirmation would hide from the quoted person that their line was saved.</para>
/// <para>The context menu lives on this class rather than inside the group below because a context
/// menu command has no subcommand path — Discord has nowhere to put a group name.</para>
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Social)]
public sealed class QuoteModule(IQuoteRepository quotes) : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Matches the <c>content</c> column (2048); the schema is still the authority.</summary>
    public const int MaxContentLength = 2048;

    /// <summary>Below this a "quote" is a reaction, and the board fills with noise.</summary>
    public const int MinContentLength = 2;

    /// <summary>
    /// How this actually gets used: right-click → Apps → Save quote. No retyping, and the jump link
    /// back to the original message comes for free.
    /// </summary>
    [MessageCommand("Save quote")]
    public async Task SaveContextAsync(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Author.IsBot)
        {
            // Her own lines are already in chat.episode, and saving a bot's output is not a quote.
            await RespondInvalidAsync("Save something a person said.");
            return;
        }

        await SaveAsync(Context, quotes, message.Author, message.Content, message);
    }

    [Group("quote", "The server's quote board.")]
    public sealed class QuoteGroup(IQuoteRepository quotes) : SonarrModuleBase<SocketInteractionContext>
    {
        [SlashCommand("save", "Put someone's line on the board.")]
        public Task SaveAsync(
            [Summary("user", "Who said it")] IUser user,
            [Summary("text", "What they said")] string text)
            => QuoteModule.SaveAsync(Context, quotes, user, text, message: null);

        [SlashCommand("random", "Pull something off the board.")]
        public async Task RandomAsync(
            [Summary("user", "Only their quotes. Leave empty for anyone's.")] IUser? user = null)
        {
            QuoteBoard? quote = await quotes.RandomAsync(
                (long)Context.Guild.Id, user is null ? null : (long)user.Id);

            if (quote is null)
            {
                await RespondInvalidAsync(user is null
                    ? "The board is empty. Right-click a message → Apps → Save quote."
                    : $"Nothing from {user.Mention} on the board yet.");
                return;
            }

            await RespondAsync(
                embed: Render(Context.Guild.Id, quote), allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("delete", "Take a quote off the board. One you saved, or one of you.")]
        public async Task DeleteAsync([Summary("id", "The `#id` in the quote's footer")] long id)
        {
            // Ownership is enforced in the repository's WHERE clause, not by trusting this id — same
            // rule as /reminders cancel. docs/06: the person quoted can always take their own words
            // down, and so can whoever saved it.
            bool gone = await quotes.DeleteAsync((long)Context.Guild.Id, id, (long)Context.User.Id);

            await RespondPersonalAsync(gone
                ? $"`#{id}` is off the board."
                : $"No quote `#{id}` here that's yours to remove.");
        }
    }

    /// <summary>
    /// The one save path, shared by the slash command and the context menu — the guards and the
    /// stored shape must not be able to drift apart between two entry points.
    /// </summary>
    private static async Task SaveAsync(
        SocketInteractionContext context,
        IQuoteRepository quotes,
        IUser author,
        string? text,
        IMessage? message)
    {
        string content = (text ?? string.Empty).Trim();

        if (InputGuards.Length(content, MaxContentLength, "quote", MinContentLength) is { } problem)
        {
            // Interaction.RespondAsync rather than the base class's RespondInvalidAsync: this is
            // shared by two module types, so it cannot use either one's protected members.
            await context.Interaction.RespondAsync(problem, ephemeral: true);
            return;
        }

        QuoteBoard quote = new()
        {
            GuildId = (long)context.Guild.Id,
            AuthorId = (long)author.Id,
            SavedBy = (long)context.User.Id,
            Content = content,
            MessageId = message is null ? null : (long)message.Id,
            ChannelId = message is null ? null : (long)message.Channel.Id,
        };

        // SaveAsync fills QuoteId from the identity column, so the same instance renders the reply.
        await quotes.SaveAsync(quote);

        await context.Interaction.RespondAsync(
            $"Saved as `#{quote.QuoteId}`.",
            embed: Render(context.Guild.Id, quote),
            allowedMentions: AllowedMentions.None);
    }

    /// <summary>
    /// One quote as an embed. Every call site passes <see cref="AllowedMentions.None"/>, so the
    /// <c>&lt;@id&gt;</c> renders as a name without pinging whoever is quoted — being on the board
    /// should not mean a notification every time somebody runs <c>/quote random</c>.
    /// </summary>
    private static Embed Render(ulong guildId, QuoteBoard quote)
    {
        var embed = new EmbedBuilder()
            .WithColor(new Color(0x5865F2))
            .WithDescription(
                $"> {Quotable(quote.Content)}\n— {MentionUtils.MentionUser((ulong)quote.AuthorId)}")
            .WithFooter($"#{quote.QuoteId} · /quote delete to remove");

        if (quote is { MessageId: { } messageId, ChannelId: { } channelId })
        {
            embed.WithUrl($"https://discord.com/channels/{guildId}/{channelId}/{messageId}");
        }

        return embed.Build();
    }

    /// <summary>
    /// The quote is another member's text going back into a public message. Neutralise markdown so
    /// a saved line cannot break out of the blockquote, and keep continuation lines inside it.
    /// </summary>
    private static string Quotable(string content)
        => Format.Sanitize(content).ReplaceLineEndings("\n> ");
}
