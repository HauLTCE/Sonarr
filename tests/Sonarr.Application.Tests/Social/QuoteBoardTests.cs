using Microsoft.Extensions.DependencyInjection;
using Discord.Interactions;
using Discord.Rest;
using Sonarr.Application.Tests.Chat;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Modules;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;

namespace Sonarr.Application.Tests.Social;

/// <summary>
/// The quote board's command surface and its ownership rules. No database here — the interesting
/// half is what the repository contract promises, which <see cref="FakeQuoteRepository"/> mirrors.
/// </summary>
public sealed class QuoteBoardTests
{
    private const long Guild = 1;
    private const long Other = 2;
    private const long Author = 777;
    private const long Saver = 888;
    private const long Stranger = 999;

    /// <summary>
    /// A context menu command cannot live inside a <c>[Group]</c> — Discord has nowhere to put the
    /// group name — so "Save quote" sits on the outer class and the slash commands nest. If that
    /// ever stops being legal, the real module builder says so here rather than at startup.
    /// </summary>
    [Fact]
    public async Task The_module_builds_with_both_a_group_and_a_context_menu()
    {
        using var rest = new DiscordRestClient();
        using var interactions = new InteractionService(rest);
        // The builder constructs the module to read its attributes, so the repository has to be
        // resolvable — the fake is enough, nothing is called.
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IQuoteRepository>(new FakeQuoteRepository())
            .BuildServiceProvider();

        ModuleInfo module = await interactions.AddModuleAsync(typeof(QuoteModule), services);

        Assert.Contains(module.ContextCommands, c => c.Name == "Save quote");
        Assert.Contains(
            module.SubModules.SelectMany(m => m.SlashCommands),
            c => c.Name is "save" or "random" or "delete");
    }

    [Fact]
    public async Task A_saved_quote_comes_back_at_random()
    {
        IQuoteRepository quotes = new FakeQuoteRepository();

        await quotes.SaveAsync(Quote("the microwave is a portal"));

        QuoteBoard? drawn = await quotes.RandomAsync(Guild);
        Assert.Equal("the microwave is a portal", drawn?.Content);
    }

    [Fact]
    public async Task Save_fills_the_id_so_the_reply_can_print_it()
    {
        IQuoteRepository quotes = new FakeQuoteRepository();
        QuoteBoard quote = Quote("first");

        long id = await quotes.SaveAsync(quote);

        Assert.Equal(id, quote.QuoteId);
        Assert.NotEqual(0, id);
    }

    /// <summary>One server's board must never surface in another (docs/06).</summary>
    [Fact]
    public async Task Another_guilds_board_stays_there()
    {
        IQuoteRepository quotes = new FakeQuoteRepository();
        await quotes.SaveAsync(Quote("ours"));

        Assert.Null(await quotes.RandomAsync(Other));
    }

    [Fact]
    public async Task Filtering_by_author_only_draws_theirs()
    {
        IQuoteRepository quotes = new FakeQuoteRepository();
        await quotes.SaveAsync(Quote("mine", author: Author));
        await quotes.SaveAsync(Quote("theirs", author: Stranger));

        Assert.Equal("theirs", (await quotes.RandomAsync(Guild, Stranger))?.Content);
    }

    [Fact]
    public async Task An_empty_board_draws_nothing_rather_than_throwing()
        => Assert.Null(await new FakeQuoteRepository().RandomAsync(Guild));

    /// <summary>docs/06: the person quoted can always take their own words down.</summary>
    [Fact]
    public async Task The_quoted_person_can_delete_it()
    {
        IQuoteRepository quotes = new FakeQuoteRepository();
        long id = await quotes.SaveAsync(Quote("regrettable"));

        Assert.True(await quotes.DeleteAsync(Guild, id, Author));
        Assert.Null(await quotes.RandomAsync(Guild));
    }

    [Fact]
    public async Task So_can_whoever_saved_it()
    {
        IQuoteRepository quotes = new FakeQuoteRepository();
        long id = await quotes.SaveAsync(Quote("regrettable"));

        Assert.True(await quotes.DeleteAsync(Guild, id, Saver));
    }

    /// <summary>
    /// The point of putting ownership in the WHERE clause: a guessed id belonging to somebody else
    /// matches nothing instead of deleting their row.
    /// </summary>
    [Fact]
    public async Task A_stranger_cannot_delete_it()
    {
        IQuoteRepository quotes = new FakeQuoteRepository();
        long id = await quotes.SaveAsync(Quote("regrettable"));

        Assert.False(await quotes.DeleteAsync(Guild, id, Stranger));
        Assert.NotNull(await quotes.RandomAsync(Guild));
    }

    [Fact]
    public async Task Deleting_across_guilds_does_nothing()
    {
        IQuoteRepository quotes = new FakeQuoteRepository();
        long id = await quotes.SaveAsync(Quote("ours"));

        Assert.False(await quotes.DeleteAsync(Other, id, Author));
    }

    [Fact]
    public async Task Deleting_an_id_that_never_existed_is_just_false()
        => Assert.False(await new FakeQuoteRepository().DeleteAsync(Guild, 4242, Author));

    /// <summary>
    /// The guard the module runs before it stores anything. 2048 matches the <c>content</c> column,
    /// and the floor stops the board filling with "lol".
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("k")]
    public void Too_short_or_blank_is_refused(string content)
        => Assert.NotNull(InputGuards.Length(
            content, QuoteModule.MaxContentLength, "quote", QuoteModule.MinContentLength));

    [Fact]
    public void A_quote_past_the_column_width_is_refused()
        => Assert.NotNull(InputGuards.Length(
            new string('x', QuoteModule.MaxContentLength + 1),
            QuoteModule.MaxContentLength,
            "quote",
            QuoteModule.MinContentLength));

    [Fact]
    public void A_normal_line_passes()
        => Assert.Null(InputGuards.Length(
            "the microwave is a portal",
            QuoteModule.MaxContentLength,
            "quote",
            QuoteModule.MinContentLength));

    private static QuoteBoard Quote(string content, long author = Author) => new()
    {
        GuildId = Guild,
        AuthorId = author,
        SavedBy = Saver,
        Content = content,
    };
}
