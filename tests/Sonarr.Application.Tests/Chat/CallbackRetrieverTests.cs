using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Chat;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// The relevance gate. An ungated callback is a bot quoting itself at random, so most of these
/// assert that she stays quiet.
/// </summary>
public class CallbackRetrieverTests
{
    private const long Guild = 111;
    private const long User = 333;

    private static (CallbackRetriever Retriever, FakeEpisodeRepository Episodes, FakeTextEmbedder Embedder) Build()
    {
        FakeTextEmbedder embedder = new();
        FakeEpisodeRepository episodes = new();
        return (
            new CallbackRetriever(embedder, episodes, NullLogger<CallbackRetriever>.Instance),
            episodes,
            embedder);
    }

    private static void Seed(
        FakeEpisodeRepository episodes, FakeTextEmbedder embedder, string quote, long turn)
        => episodes.Seed(Guild, User, quote, turn, embedder.Embed(quote));

    [Fact]
    public async Task AnOldRelevantEpisode_ComesBack()
    {
        var (retriever, episodes, embedder) = Build();
        Seed(episodes, embedder, "your playlist is objectively bad", 1);

        string? quote = await retriever.QuoteAsync(Guild, User, "your playlist is objectively bad", 50);

        Assert.Equal("your playlist is objectively bad", quote);
    }

    [Fact]
    public async Task SomethingSaidTwoTurnsAgo_IsNotAMemory()
    {
        var (retriever, episodes, embedder) = Build();
        Seed(episodes, embedder, "your playlist is objectively bad", 49);

        // Repeating herself is not recall, however relevant the match.
        Assert.Null(await retriever.QuoteAsync(Guild, User, "your playlist is objectively bad", 50));
    }

    [Fact]
    public async Task AnUnrelatedEpisode_StaysBuried()
    {
        var (retriever, episodes, embedder) = Build();
        Seed(episodes, embedder, "goodnight", 1);

        Assert.Null(await retriever.QuoteAsync(Guild, User, "explain quantum tunnelling", 50));
    }

    [Fact]
    public async Task NoEpisodes_MeansNoTail()
    {
        var (retriever, _, _) = Build();
        Assert.Null(await retriever.QuoteAsync(Guild, User, "anything at all", 50));
    }

    [Fact]
    public async Task BlankText_SkipsTheLookupEntirely()
    {
        var (retriever, episodes, embedder) = Build();
        Seed(episodes, embedder, "something", 1);
        int before = embedder.Embedded;

        Assert.Null(await retriever.QuoteAsync(Guild, User, "   ", 50));
        Assert.Equal(before, embedder.Embedded);
    }

    [Fact]
    public async Task AnotherPersonsMemory_IsNotHers()
    {
        var (retriever, episodes, embedder) = Build();
        episodes.Seed(Guild, 999, "your playlist is objectively bad", 1,
            embedder.Embed("your playlist is objectively bad"));

        Assert.Null(await retriever.QuoteAsync(Guild, User, "your playlist is objectively bad", 50));
    }

    [Fact]
    public async Task ALongQuote_IsTrimmedToAnAside()
    {
        var (retriever, episodes, embedder) = Build();
        string wordy = string.Join(' ', Enumerable.Repeat("blah", 60));
        Seed(episodes, embedder, wordy, 1);

        string? quote = await retriever.QuoteAsync(Guild, User, wordy, 50);

        Assert.NotNull(quote);
        Assert.Equal(CallbackRetriever.MaxQuoteChars, quote!.Length);
        Assert.EndsWith("…", quote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeadDatabase_CostsTheTailAndNothingElse()
    {
        var (retriever, episodes, embedder) = Build();
        Seed(episodes, embedder, "your playlist is objectively bad", 1);
        episodes.Fail = true;

        Assert.Null(await retriever.QuoteAsync(Guild, User, "your playlist is objectively bad", 50));
    }

    [Fact]
    public async Task AFailedEmbedding_CostsTheTailAndNothingElse()
    {
        var (retriever, episodes, embedder) = Build();
        Seed(episodes, embedder, "your playlist is objectively bad", 1);
        embedder.Fail = true;

        Assert.Null(await retriever.QuoteAsync(Guild, User, "your playlist is objectively bad", 50));
    }
}
