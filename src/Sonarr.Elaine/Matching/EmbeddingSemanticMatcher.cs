using System.Collections.Frozen;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// One authored example phrasing and its vector. Produced outside the engine (the model lives
/// in the infrastructure layer) and handed in already normalized to unit length.
/// </summary>
/// <param name="IntentId">Intent the example belongs to.</param>
/// <param name="Vector">Unit-length embedding, so cosine similarity is a dot product.</param>
public sealed record IntentExampleVector(string IntentId, float[] Vector);

/// <summary>
/// The real semantic tier: cosine similarity between the message and the authored example
/// phrasings, above a floor.
/// </summary>
/// <remarks>
/// Still pure. The engine never runs a model — the adapter embeds the message and the examples
/// and passes vectors in, which is what keeps <c>Sonarr.Elaine</c> free of ONNX and keeps a
/// turn replayable from its stored trace.
/// <para>Deliberately conservative. A rescue only happens when the lexical tier already
/// missed, and the score it returns is capped below what a real pattern match earns, so a
/// fuzzy semantic guess can never out-rank authored text.</para>
/// </remarks>
public sealed class EmbeddingSemanticMatcher : ISemanticMatcher
{
    /// <summary>
    /// Cosine floor for a rescue. 0.62 measured against the shipped examples on
    /// all-MiniLM-L6-v2: "everything is terrible" ↔ "i feel awful" sits around 0.42, so this
    /// is above paraphrase-of-a-different-thing and below genuine restatement. Raise it if she
    /// starts answering the wrong question; lower it if she keeps falling back.
    /// </summary>
    public const double SimilarityFloor = 0.62;

    /// <summary>
    /// Score a rescue is worth. Just over <see cref="ScoreModel.LexicalThreshold"/> so the
    /// reply is treated as confident, but below what any two-keyword lexical match earns —
    /// authored patterns always win.
    /// </summary>
    public const double RescueScore = ScoreModel.LexicalThreshold + 0.05;

    private readonly PersonaRoot _root;
    private readonly float[] _query;
    private readonly FrozenDictionary<string, IntentExampleVector[]> _byIntent;

    /// <param name="root">Needed to evaluate a rescued intent's guards (tier ordering).</param>
    /// <param name="queryVector">
    /// Embedding of this turn's message, unit length. Null or wrong-width means the model was
    /// unavailable this turn, and the matcher rescues nothing rather than guessing.
    /// </param>
    /// <param name="examples">Authored example vectors, any order.</param>
    public EmbeddingSemanticMatcher(
        PersonaRoot root, float[]? queryVector, IReadOnlyList<IntentExampleVector> examples)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(examples);

        _root = root;
        _query = queryVector ?? [];
        _byIntent = examples
            .Where(e => e.Vector.Length == _query.Length && _query.Length > 0)
            .GroupBy(e => e.IntentId, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    public MatchCandidate? Rescue(
        Normalized input, MatchContext context, IReadOnlyList<IntentDef> eligible)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(eligible);

        if (_query.Length == 0 || _byIntent.Count == 0)
        {
            return null;
        }

        IntentDef? best = null;
        double bestScore = SimilarityFloor;

        foreach (IntentDef intent in eligible)
        {
            if (!_byIntent.TryGetValue(intent.Id, out IntentExampleVector[]? vectors))
            {
                continue;
            }

            foreach (IntentExampleVector example in vectors)
            {
                double similarity = Dot(_query, example.Vector);
                // Strictly greater, so ties fall to the earlier-declared intent — the same
                // tie-break the lexical tier uses.
                if (similarity > bestScore)
                {
                    bestScore = similarity;
                    best = intent;
                }
            }
        }

        // A rescued intent still has to clear its own guards: a semantic hit is evidence about
        // what was said, never permission to ignore state.
        if (best is null || best.Patterns.Count == 0
            || best.Guards.Any(g => !GuardEvaluator.Holds(g, context, _root)))
        {
            return null;
        }

        return new MatchCandidate
        {
            Intent = best,
            Score = RescueScore,
            Captures = FrozenDictionary<string, string>.Empty,
            BestPattern = best.Patterns[0],
            MatchedPatternCount = 0,
        };
    }

    /// <summary>Cosine similarity of two unit vectors.</summary>
    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}
