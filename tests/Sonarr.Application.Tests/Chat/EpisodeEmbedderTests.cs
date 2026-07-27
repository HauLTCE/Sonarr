using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Bot.Discord.Chat;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// The backfill contract (docs/11 cutover step 4): every episode without a vector eventually gets
/// one, in batches, and a failure costs a retry rather than a row.
/// </summary>
public class EpisodeEmbedderTests
{
    [Fact]
    public async Task AnUnembeddedEpisode_GetsAVector()
    {
        FakeEpisodeRepository episodes = new();
        Episode legacy = episodes.Seed(1, 2, "you said that already", turn: 3);

        int done = await Embedder(episodes, new FakeTextEmbedder()).BatchAsync(default);

        Assert.Equal(1, done);
        Assert.NotNull(legacy.Embedding);
    }

    [Fact]
    public async Task AnAlreadyEmbeddedEpisode_IsLeftAlone()
    {
        FakeEpisodeRepository episodes = new();
        FakeTextEmbedder embedder = new();
        episodes.Seed(1, 2, "already done", turn: 3, vector: embedder.Embed("already done"));
        int before = embedder.Embedded;

        int done = await Embedder(episodes, embedder).BatchAsync(default);

        Assert.Equal(0, done);
        Assert.Equal(before, embedder.Embedded);
    }

    [Fact]
    public async Task ALargeImport_DrainsOneBatchAtATime()
    {
        FakeEpisodeRepository episodes = new();
        for (int i = 0; i < EpisodeEmbedder.BatchSize + 5; i++)
        {
            episodes.Seed(1, 2, $"line {i}", turn: i);
        }

        EpisodeEmbedder embedder = Embedder(episodes, new FakeTextEmbedder());

        Assert.Equal(EpisodeEmbedder.BatchSize, await embedder.BatchAsync(default));

        // The next pass picks up the remainder — the queue is "rows with no embedding", so
        // nothing has to be remembered between batches.
        Assert.Equal(5, await embedder.BatchAsync(default));
        Assert.Empty(await episodes.GetUnembeddedAsync(100));
    }

    [Fact]
    public async Task ABlankQuote_StillLeavesTheQueue()
    {
        FakeEpisodeRepository episodes = new();
        Episode blank = episodes.Seed(1, 2, "   ", turn: 1);

        await Embedder(episodes, new FakeTextEmbedder()).BatchAsync(default);

        // Zero vector: never recalled (cosine 0), but also never re-queued forever.
        Assert.NotNull(blank.Embedding);
        Assert.Empty(await episodes.GetUnembeddedAsync(100));
    }

    [Fact]
    public async Task ADeadDatabase_LeavesTheRowsForTheNextPass()
    {
        FakeEpisodeRepository episodes = new() { Fail = true };
        Episode pending = episodes.Seed(1, 2, "still waiting", turn: 1);

        int done = await Embedder(episodes, new FakeTextEmbedder()).BatchAsync(default);

        Assert.Equal(0, done);
        Assert.Null(pending.Embedding);
    }

    [Fact]
    public async Task AMissingModel_LeavesTheRowsForTheNextPass()
    {
        FakeEpisodeRepository episodes = new();
        Episode pending = episodes.Seed(1, 2, "no model here", turn: 1);

        int done = await Embedder(episodes, new FakeTextEmbedder { Fail = true }).BatchAsync(default);

        Assert.Equal(0, done);
        Assert.Null(pending.Embedding);
    }

    [Fact]
    public async Task AnEmbeddedEpisode_BecomesRecallable()
    {
        FakeEpisodeRepository episodes = new();
        FakeTextEmbedder embedder = new();
        episodes.Seed(1, 2, "we talked about the cat", turn: 1);

        Assert.Empty(await episodes.SearchAsync(1, 2, embedder.Embed("the cat"), 3));

        await Embedder(episodes, embedder).BatchAsync(default);

        Assert.NotEmpty(
            await episodes.SearchAsync(1, 2, embedder.Embed("we talked about the cat"), 3));
    }

    private static EpisodeEmbedder Embedder(IEpisodeRepository episodes, ITextEmbedder embedder)
    {
        // The service resolves the repository per batch from a scope, like the real host does.
        ServiceCollection services = new();
        services.AddSingleton(episodes);
        return new EpisodeEmbedder(
            embedder,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EpisodeEmbedder>.Instance);
    }
}
