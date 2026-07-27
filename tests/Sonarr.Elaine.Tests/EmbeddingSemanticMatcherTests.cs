using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The real semantic tier, driven with hand-written unit vectors so the assertions are about
/// the matcher's rules and not about what a particular checkpoint happens to think.
/// </summary>
public class EmbeddingSemanticMatcherTests
{
    private const string Yaml = """
        intents:
          - id: MOOD
            match: { keyword: [zzzz] }
            examples: ["i feel awful"]
            pool: filler
          - id: TWIN
            match: { keyword: [qqqq] }
            examples: ["i feel awful too"]
            pool: filler
          - id: GATED
            match: { keyword: [wwww] }
            examples: ["gate me"]
            guards:
              - kind: min_tier
                value: HIGH
            pool: filler
        """;

    private static PersonaGraph Graph()
    {
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", """
                version: 2
                start_activity: idle
                activities:
                  - id: idle
                    intents: ["*"]
                    fallback_pool: filler
                modes:
                  - id: NEUTRAL
                    always: true
                tiers:
                  - id: LOW
                    min_trust: 0
                  - id: HIGH
                    min_trust: 50
                """),
            ("pools/p.yaml", "pools:\n  filler: [whatever]\n"),
            ("intents/i.yaml", Yaml)));

        Assert.True(result.IsValid, result.Report());
        return result.Graph!;
    }

    /// <summary>A unit vector pointing at axis <paramref name="axis"/> of a 4-d space.</summary>
    private static float[] Axis(int axis)
    {
        float[] v = new float[4];
        v[axis] = 1f;
        return v;
    }

    private static MatchCandidate? Rescue(
        PersonaGraph graph,
        float[] query,
        IReadOnlyList<IntentExampleVector> examples,
        MatchContext? context = null)
        => new EmbeddingSemanticMatcher(graph.Root, query, examples).Rescue(
            Normalizer.Normalize("anything"),
            context ?? MatchContext.Empty,
            graph.Intents);

    [Fact]
    public void ANearExample_RescuesItsIntent()
    {
        PersonaGraph graph = Graph();
        MatchCandidate? hit = Rescue(graph, Axis(0), [new IntentExampleVector("MOOD", Axis(0))]);

        Assert.Equal("MOOD", hit?.IntentId);
        Assert.Equal(EmbeddingSemanticMatcher.RescueScore, hit!.Score);
    }

    [Fact]
    public void RescueScore_StaysBelowAConfidentLexicalMatch()
    {
        // The docs/10 contract: a rescue is evidence, not authority. It must never outrank a
        // real pattern hit, only a miss.
        Assert.True(EmbeddingSemanticMatcher.RescueScore > ScoreModel.LexicalThreshold);
        Assert.True(EmbeddingSemanticMatcher.RescueScore < 2.0);
    }

    [Fact]
    public void AnOrthogonalExample_IsNotAMemory()
    {
        PersonaGraph graph = Graph();
        Assert.Null(Rescue(graph, Axis(0), [new IntentExampleVector("MOOD", Axis(1))]));
    }

    [Fact]
    public void BelowTheFloor_RescuesNothing()
    {
        PersonaGraph graph = Graph();

        // Halfway between two axes: 0.707 similarity, comfortably over the floor.
        float[] near = [0.707f, 0.707f, 0, 0];
        Assert.NotNull(Rescue(graph, near, [new IntentExampleVector("MOOD", Axis(0))]));

        // A third of the way: 0.32, under it.
        float[] far = [0.32f, 0.947f, 0, 0];
        Assert.Null(Rescue(graph, far, [new IntentExampleVector("MOOD", Axis(0))]));
    }

    [Fact]
    public void ATie_GoesToTheEarlierDeclaredIntent()
    {
        PersonaGraph graph = Graph();
        MatchCandidate? hit = Rescue(graph, Axis(0), [
            new IntentExampleVector("TWIN", Axis(0)),
            new IntentExampleVector("MOOD", Axis(0)),
        ]);

        // Identical similarity from both, and the example order is the cache's, not the
        // persona's. The scan walks declaration order and only takes a strict improvement, so
        // the earlier-declared intent wins — same tie-break the lexical tier uses.
        Assert.Equal("MOOD", hit?.IntentId);
    }

    [Fact]
    public void AGuardedIntent_IsStillGuarded()
    {
        PersonaGraph graph = Graph();
        IntentExampleVector[] examples = [new IntentExampleVector("GATED", Axis(0))];

        Assert.Null(Rescue(graph, Axis(0), examples, MatchContext.Empty with { TierId = "LOW" }));
        Assert.NotNull(Rescue(graph, Axis(0), examples, MatchContext.Empty with { TierId = "HIGH" }));
    }

    [Fact]
    public void AWrongWidthVector_IsIgnoredRatherThanThrown()
    {
        // A model swap leaves rows of the old dimension in the cache. They must not crash a turn.
        PersonaGraph graph = Graph();
        Assert.Null(Rescue(graph, Axis(0), [new IntentExampleVector("MOOD", [1f, 0f])]));
    }

    [Fact]
    public void NoQueryVector_RescuesNothing()
    {
        PersonaGraph graph = Graph();
        Assert.Null(Rescue(graph, [], [new IntentExampleVector("MOOD", Axis(0))]));
        Assert.Null(Rescue(graph, Axis(0), []));
    }

    [Fact]
    public void AnUnknownIntentId_IsIgnored()
    {
        PersonaGraph graph = Graph();
        Assert.Null(Rescue(graph, Axis(0), [new IntentExampleVector("GONE", Axis(0))]));
    }
}
