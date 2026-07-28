using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Chat;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;
using Sonarr.Elaine.Conversation;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// Recall off <c>social.quote_board</c> (docs/04, docs/07). The rules that matter are attribution
/// and scope: the quote belongs to whoever said it, and never leaves the guild it was saved in.
/// </summary>
public class QuoteBoardRecallTests
{
    private const long Guild = 111;
    private const long Author = 777;

    private static (QuoteBoardRecall Recall, FakeQuoteRepository Quotes) Build()
    {
        FakeQuoteRepository quotes = new();
        return (new QuoteBoardRecall(quotes, NullLogger<QuoteBoardRecall>.Instance), quotes);
    }

    [Fact]
    public async Task ASavedLineComesBackWithItsAuthor()
    {
        var (recall, quotes) = Build();
        quotes.Seed(Guild, Author, "the microwave is a portal");

        RecalledQuote? found = await recall.RecallAsync(Guild, "did someone say portal");

        Assert.NotNull(found);
        Assert.Equal("the microwave is a portal", found!.Quote);
        Assert.Equal($"<@{Author}>", found.Author);
    }

    [Fact]
    public async Task AnotherGuildsInsideJokeStaysThere()
    {
        var (recall, quotes) = Build();
        quotes.Seed(222, Author, "the microwave is a portal");

        Assert.Null(await recall.RecallAsync(Guild, "did someone say portal"));
    }

    [Fact]
    public async Task NothingOnTheBoardRelatesMeansNoTail()
    {
        var (recall, quotes) = Build();
        quotes.Seed(Guild, Author, "the microwave is a portal");

        Assert.Null(await recall.RecallAsync(Guild, "explain quantum tunnelling please"));
    }

    [Fact]
    public async Task ShortWordsAreNotWorthMatchingOn()
    {
        var (recall, quotes) = Build();
        quotes.Seed(Guild, Author, "a is the of it");

        // Otherwise "is the" would match most of the board and the tail would be noise.
        Assert.Null(await recall.RecallAsync(Guild, "is it the one or is it not"));
        Assert.Equal(0, quotes.Searches);
    }

    [Fact]
    public async Task BlankTextSkipsTheLookupEntirely()
    {
        var (recall, quotes) = Build();
        quotes.Seed(Guild, Author, "something quotable");

        Assert.Null(await recall.RecallAsync(Guild, "   "));
        Assert.Equal(0, quotes.Searches);
    }

    [Fact]
    public async Task ALongQuoteIsTrimmedToAnAside()
    {
        var (recall, quotes) = Build();
        quotes.Seed(Guild, Author, string.Join(' ', Enumerable.Repeat("waffles", 60)));

        RecalledQuote? found = await recall.RecallAsync(Guild, "waffles");

        Assert.NotNull(found);
        Assert.Equal(QuoteBoardRecall.MaxQuoteChars, found!.Quote.Length);
        Assert.EndsWith("…", found.Quote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeadDatabaseCostsTheTailAndNothingElse()
    {
        var (recall, quotes) = Build();
        quotes.Seed(Guild, Author, "the microwave is a portal");
        quotes.Fail = true;

        Assert.Null(await recall.RecallAsync(Guild, "did someone say portal"));
    }

    [Fact]
    public async Task ALongMessageSearchesOnASaneNumberOfWords()
    {
        var (recall, quotes) = Build();
        quotes.Seed(Guild, Author, "unrelated");

        await recall.RecallAsync(
            Guild, "absolutely everything about deployment pipelines container images and rollbacks");

        Assert.Equal(QuoteBoardRecall.MaxWords, quotes.LastWords.Count);
    }

    [Fact]
    public async Task ARepeatedWordIsNotSearchedTwice()
    {
        var (recall, quotes) = Build();
        quotes.Seed(Guild, Author, "unrelated");

        await recall.RecallAsync(Guild, "portal portal portal");

        Assert.Equal(["portal"], quotes.LastWords);
    }
}

/// <summary>
/// In-memory quote board. Matching is the same contract the repository promises: guild-scoped,
/// case-insensitive substring on any supplied word, newest first.
/// </summary>
internal sealed class FakeQuoteRepository : IQuoteRepository
{
    private readonly List<QuoteBoard> _quotes = [];

    /// <summary>Next search throws, standing in for an unreachable database.</summary>
    public bool Fail { get; set; }

    /// <summary>How many searches reached the database, so a test can prove a gate skipped one.</summary>
    public int Searches { get; private set; }

    /// <summary>The words the last search ran with.</summary>
    public IReadOnlyList<string> LastWords { get; private set; } = [];

    public void Seed(long guildId, long authorId, string content)
        => _quotes.Add(new QuoteBoard
        {
            QuoteId = _quotes.Count + 1,
            GuildId = guildId,
            AuthorId = authorId,
            SavedBy = 999,
            Content = content,
        });

    public Task<IReadOnlyList<QuoteBoard>> SearchAsync(
        long guildId, IReadOnlyList<string> words, int limit, CancellationToken ct = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("database unreachable");
        }

        Searches++;
        LastWords = words;

        return Task.FromResult<IReadOnlyList<QuoteBoard>>(
            [.. _quotes
                .Where(q => q.GuildId == guildId
                    && words.Any(w => q.Content.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(q => q.QuoteId)
                .Take(limit)]);
    }
}
