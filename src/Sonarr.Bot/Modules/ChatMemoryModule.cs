using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Application.Chat;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Configuration;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/relationship</c> and <c>/memories</c> (docs/07-commands.md#chat). Thin translator: guard
/// the input, ask <see cref="ChatIntrospection"/>, print what it says.
/// </summary>
/// <remarks>
/// Every reply here is ephemeral. What she thinks of you and what she remembers about you is
/// yours, and a channel-visible answer would publish it to everyone (docs/06).
/// <para>The prose is authored in the persona, so this module formats and never phrases. The one
/// thing it adds is the fact list itself, which is data the user already told her.</para>
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Chat)]
public sealed class ChatMemoryModule(ChatIntrospection chat, ChatOpinions opinions)
    : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Discord's own cap on how many facts we can offer in one autocomplete.</summary>
    public const int MaxSuggestions = 25;

    /// <summary>
    /// <c>/opinion</c> — she reads what the channel is talking about and drops an authored take.
    /// </summary>
    /// <remarks>
    /// Public, unlike everything else here: a take on the room's topic is about the room, not
    /// about you. The only personal part is the "you already agreed with me" tail, which is your
    /// own row and nobody else's (docs/06).
    /// <para>The channel history is read here rather than from the ring buffer because that
    /// buffer is metadata-only by design (docs/05) — and it is passed straight through to the
    /// embedder and dropped. Nothing about these messages is stored.</para>
    /// </remarks>
    [SlashCommand("opinion", "Her take on whatever this channel is on about.")]
    public async Task OpinionAsync()
    {
        ArgumentNullException.ThrowIfNull(opinions);

        // Embedding a dozen messages is a few milliseconds on the dev box and several times that
        // on the J2900, which is close enough to Discord's 3 s window to not gamble on it.
        await DeferAsync();

        IReadOnlyList<string> recent = Context.Channel is null
            ? []
            : [.. (await Context.Channel
                    .GetMessagesAsync(ChatOpinions.RecentMessages)
                    .FlattenAsync())
                .Where(m => !m.Author.IsBot)
                .Select(m => m.Content)
                .Where(c => !string.IsNullOrWhiteSpace(c))];

        string line = await opinions.OpinionAsync(
            (long)Context.Guild.Id, (long)Context.User.Id, recent);

        await FollowupAsync(line);
    }

    [SlashCommand("relationship", "How she feels about you.")]
    public async Task RelationshipAsync([Summary("user", "Whose standing to ask about")] IUser? user = null)
    {
        ArgumentNullException.ThrowIfNull(chat);
        IUser target = user ?? Context.User;

        string line = await chat.DescribeRelationshipAsync(
            (long)Context.Guild.Id, (long)target.Id, (long)Context.User.Id);

        await RespondPersonalAsync(line);
    }

    [Group("memories", "What she remembers about you.")]
    public sealed class MemoriesGroup(ChatIntrospection chat) : SonarrModuleBase<SocketInteractionContext>
    {
        [SlashCommand("list", "Everything she is holding on you.")]
        public async Task ListAsync()
        {
            ArgumentNullException.ThrowIfNull(chat);

            MemoryReport report = await chat.ListMemoriesAsync((long)Context.Guild.Id, (long)Context.User.Id);
            if (report.Facts.Count == 0)
            {
                await RespondPersonalAsync(report.Opener);
                return;
            }

            var embed = new EmbedBuilder()
                .WithColor(new Color(0x5865F2))
                .WithDescription(report.Opener)
                .WithFooter("/memories forget — she'll drop one. grudgingly.");

            // 25 is Discord's field cap. Facts come back newest first, so the overflow is the
            // stuff she cares least about.
            foreach (RememberedFact fact in report.Facts.Take(EmbedBuilder.MaxFieldCount))
            {
                embed.AddField(
                    Humanize(fact.Predicate),
                    $"{fact.Value}\n{TimestampTag.FromDateTimeOffset(fact.LearnedAt, TimestampTagStyles.Relative)}",
                    inline: true);
            }

            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }

        [SlashCommand("forget", "Ask her to drop one thing she remembers.")]
        public async Task ForgetAsync(
            [Summary("fact", "Which one to drop")]
            [Autocomplete(typeof(RememberedFactAutocompleteHandler))] string fact)
        {
            ArgumentNullException.ThrowIfNull(chat);

            // The value arrives as free text: autocomplete is a suggestion, not a constraint, and
            // a raw predicate can be typed or replayed from a stale interaction.
            if (InputGuards.Length(fact, 64, "fact") is { } problem)
            {
                await RespondInvalidAsync(problem);
                return;
            }

            string line = await chat.ForgetAsync(
                (long)Context.Guild.Id, (long)Context.User.Id, fact.Trim());

            await RespondPersonalAsync(line);
        }

        private static string Humanize(string predicate) =>
            predicate.Replace('_', ' ');
    }
}

/// <summary>Autocomplete over your own facts — never anyone else's (docs/06).</summary>
public sealed class RememberedFactAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(services);

        if (context.Guild is null)
        {
            return AutocompletionResult.FromSuccess();
        }

        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;
        var chat = services.GetRequiredService<ChatIntrospection>();

        MemoryReport report = await chat.ListMemoriesAsync((long)context.Guild.Id, (long)context.User.Id);

        IEnumerable<AutocompleteResult> matches = report.Facts
            .Where(f => f.Predicate.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || f.Value.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(ChatMemoryModule.MaxSuggestions)
            .Select(f => new AutocompleteResult(
                Label($"{f.Predicate.Replace('_', ' ')}: {f.Value}"), f.Predicate));

        return AutocompletionResult.FromSuccess(matches);
    }

    /// <summary>Choice labels are capped at 100 characters; a long fact value would 400.</summary>
    private static string Label(string text) =>
        text.Length <= 100 ? text : string.Concat(text.AsSpan(0, 97), "...");
}
