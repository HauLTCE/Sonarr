using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Chat;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// The example-vector cache: embed once, reuse forever, prune what the persona no longer says,
/// and never let any of that cost a reply.
/// </summary>
public class SemanticIntentIndexTests
{
    private const string TwoExamples = """
        intents:
          - id: MOOD
            match: { keyword: [zzzz] }
            examples: ["i feel awful", "everything sucks"]
            pool: filler
        """;

    private static PersonaHolder Holder(string intents)
        => PersonaHolder.TryCreate(new MemoryPersonaSource(intents), out PersonaValidationResult result)
            ?? throw new InvalidOperationException(result.Report());

    private static SemanticIntentIndex Index(PersonaHolder holder, FakeTextEmbedder embedder)
        => new(holder, embedder, NullLogger<SemanticIntentIndex>.Instance);

    [Fact]
    public async Task AWarm_EmbedsEveryAuthoredExample()
    {
        FakeTextEmbedder embedder = new();
        FakeIntentEmbeddingRepository repository = new();
        SemanticIntentIndex index = Index(Holder(TwoExamples), embedder);

        await index.WarmAsync(repository);

        Assert.Equal(2, index.Vectors.Count);
        Assert.Equal(2, embedder.Embedded);
        Assert.Equal(2, repository.Count);
        Assert.All(index.Vectors, v => Assert.Equal("MOOD", v.IntentId));
    }

    [Fact]
    public async Task ASecondWarm_IsFree()
    {
        FakeTextEmbedder embedder = new();
        FakeIntentEmbeddingRepository repository = new();
        SemanticIntentIndex index = Index(Holder(TwoExamples), embedder);

        await index.WarmAsync(repository);
        int embedded = embedder.Embedded;
        int syncs = repository.Syncs;

        await index.WarmAsync(repository);

        Assert.Equal(embedded, embedder.Embedded);
        Assert.Equal(syncs, repository.Syncs);
    }

    [Fact]
    public async Task ARestart_ReadsTheCacheInsteadOfTheModel()
    {
        FakeIntentEmbeddingRepository repository = new();
        await Index(Holder(TwoExamples), new FakeTextEmbedder()).WarmAsync(repository);

        // Same repository, a fresh process: the vectors are already keyed by content hash.
        FakeTextEmbedder embedder = new();
        SemanticIntentIndex restarted = Index(Holder(TwoExamples), embedder);
        await restarted.WarmAsync(repository);

        Assert.Equal(2, restarted.Vectors.Count);
        Assert.Equal(0, embedder.Embedded);
    }

    [Fact]
    public async Task APersonaEdit_OnlyEmbedsWhatChanged()
    {
        FakeIntentEmbeddingRepository repository = new();
        await Index(Holder(TwoExamples), new FakeTextEmbedder()).WarmAsync(repository);

        FakeTextEmbedder embedder = new();
        await Index(Holder("""
            intents:
              - id: MOOD
                match: { keyword: [zzzz] }
                examples: ["i feel awful", "everything sucks", "this day is a write-off"]
                pool: filler
            """), embedder).WarmAsync(repository);

        Assert.Equal(1, embedder.Embedded);
        Assert.Equal(3, repository.Count);
    }

    [Fact]
    public async Task ARemovedExample_IsPrunedFromTheCache()
    {
        FakeIntentEmbeddingRepository repository = new();
        await Index(Holder(TwoExamples), new FakeTextEmbedder()).WarmAsync(repository);

        FakeTextEmbedder embedder = new();
        SemanticIntentIndex trimmed = Index(Holder("""
            intents:
              - id: MOOD
                match: { keyword: [zzzz] }
                examples: ["i feel awful"]
                pool: filler
            """), embedder);
        await trimmed.WarmAsync(repository);

        // Nothing to embed, but the dropped row must go: otherwise she keeps rescuing to an
        // intent the persona no longer describes that way.
        Assert.Equal(0, embedder.Embedded);
        Assert.Equal(1, repository.Count);
        Assert.Single(trimmed.Vectors);
    }

    [Fact]
    public async Task ADeadDatabase_LeavesHerLexicalOnly()
    {
        FakeIntentEmbeddingRepository repository = new() { Fail = true };
        SemanticIntentIndex index = Index(Holder(TwoExamples), new FakeTextEmbedder());

        await index.WarmAsync(repository);

        Assert.Empty(index.Vectors);
        Assert.IsType<NullSemanticMatcher>(
            index.MatcherFor(Build.Graph.Root, "everything is terrible"));
    }

    [Fact]
    public async Task NoAuthoredExamples_IsNotAnError()
    {
        FakeIntentEmbeddingRepository repository = new();
        SemanticIntentIndex index = Index(Holder("""
            intents:
              - id: MOOD
                match: { keyword: [zzzz] }
                pool: filler
            """), new FakeTextEmbedder());

        await index.WarmAsync(repository);

        Assert.Empty(index.Vectors);
        Assert.Equal(0, repository.Syncs);
    }

    [Fact]
    public async Task TheMatcher_EmbedsNothingUntilARescueIsAskedFor()
    {
        FakeTextEmbedder embedder = new();
        PersonaHolder holder = Holder(TwoExamples);
        SemanticIntentIndex index = Index(holder, embedder);
        await index.WarmAsync(new FakeIntentEmbeddingRepository());
        int afterWarm = embedder.Embedded;

        ISemanticMatcher matcher = index.MatcherFor(holder.Current.Root, "everything is terrible");
        Assert.Equal(afterWarm, embedder.Embedded);

        matcher.Rescue(Normalizer.Normalize("everything is terrible"), MatchContext.Empty, holder.Current.Intents);
        Assert.Equal(afterWarm + 1, embedder.Embedded);
    }

    [Fact]
    public async Task AFailedEmbedding_MidTurn_RescuesNothing()
    {
        FakeTextEmbedder embedder = new();
        PersonaHolder holder = Holder(TwoExamples);
        SemanticIntentIndex index = Index(holder, embedder);
        await index.WarmAsync(new FakeIntentEmbeddingRepository());

        ISemanticMatcher matcher = index.MatcherFor(holder.Current.Root, "i feel awful");
        embedder.Fail = true;

        Assert.Null(matcher.Rescue(
            Normalizer.Normalize("i feel awful"), MatchContext.Empty, holder.Current.Intents));
    }

    [Fact]
    public void BlankText_GetsNoMatcherAtAll()
        => Assert.IsType<NullSemanticMatcher>(
            Index(Holder(TwoExamples), new FakeTextEmbedder()).MatcherFor(Build.Graph.Root, "  "));
}

/// <summary>A persona built from literal YAML, for tests that need examples the shipped one lacks.</summary>
internal sealed class MemoryPersonaSource(string intents) : IPersonaSource
{
    public IReadOnlyList<PersonaFile> Read() =>
    [
        new PersonaFile("sonarr.yaml", """
            version: 2
            start_activity: idle
            activities:
              - id: idle
                intents: ["*"]
                fallback_pool: filler
            modes:
              - id: NEUTRAL
                always: true
            """),
        new PersonaFile("pools/p.yaml", "pools:\n  filler: [whatever]\n"),
        new PersonaFile("intents/i.yaml", intents),
    ];
}
