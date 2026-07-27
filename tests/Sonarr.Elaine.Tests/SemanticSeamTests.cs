using System.Collections.Frozen;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The semantic tier is a seam, not an implementation (embeddings land in a later phase).
/// What matters now is the contract: it rescues misses and never overrides a confident
/// lexical match.
/// </summary>
public class SemanticSeamTests
{
    private const string Yaml = """
        intents:
          - id: GREETING
            match: { keyword: [hello] }
            pool: filler
          - id: MOOD
            match: { keyword: [zzzz] }
            examples: ["i feel awful", "everything sucks"]
            pool: filler
        """;

    private static PersonaGraph Graph()
    {
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", "version: 2\nstart_activity: idle\nactivities:\n  - id: idle\n    intents: [\"*\"]\n    fallback_pool: filler\nmodes:\n  - id: NEUTRAL\n    always: true\n"),
            ("pools/p.yaml", "pools:\n  filler: [whatever]\n"),
            ("intents/i.yaml", Yaml)));

        Assert.True(result.IsValid, result.Report());
        return result.Graph!;
    }

    /// <summary>Stand-in for the ONNX tier: claims the first intent that has examples.</summary>
    private sealed class StubSemanticMatcher : ISemanticMatcher
    {
        public int Calls { get; private set; }

        public MatchCandidate? Rescue(
            Normalized input, MatchContext context, IReadOnlyList<IntentDef> eligible)
        {
            Calls++;
            IntentDef? intent = eligible.FirstOrDefault(i => i.SemanticExamples.Count > 0);
            return intent is null ? null : new MatchCandidate
            {
                Intent = intent,
                Score = 1.2,
                Captures = FrozenDictionary<string, string>.Empty,
                BestPattern = intent.Patterns[0],
                MatchedPatternCount = 0,
            };
        }
    }

    [Fact]
    public void ConfidentLexicalMatch_NeverReachesTheSemanticTier()
    {
        StubSemanticMatcher stub = new();
        MatchOutcome outcome = new IntentRecognizer(Graph(), stub).Recognize("hello", MatchContext.Empty);

        Assert.Equal("GREETING", outcome.Primary?.IntentId);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public void LexicalMiss_IsHandedToTheSemanticTier()
    {
        StubSemanticMatcher stub = new();
        MatchOutcome outcome = new IntentRecognizer(Graph(), stub)
            .Recognize("everything is terrible", MatchContext.Empty);

        Assert.Equal(1, stub.Calls);
        Assert.Equal("MOOD", outcome.Primary?.IntentId);
    }

    [Fact]
    public void WithoutASemanticTier_AMissIsJustAMiss()
    {
        MatchOutcome outcome = new IntentRecognizer(Graph())
            .Recognize("everything is terrible", MatchContext.Empty);

        Assert.Null(outcome.Primary);
        Assert.False(outcome.IsConfident);
    }

    [Fact]
    public void NullSemanticMatcher_RescuesNothing()
    {
        Assert.Null(NullSemanticMatcher.Instance.Rescue(
            Normalizer.Normalize("anything"), MatchContext.Empty, []));
    }
}
