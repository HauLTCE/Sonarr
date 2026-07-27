using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// The seam where the ONNX embedding tier plugs in (docs/10, Phase 4). Not implemented
/// here: <c>Sonarr.Elaine</c> stays pure, so the model lives in the infrastructure layer
/// and is injected.
/// </summary>
/// <remarks>
/// Contract, from docs/10: semantic matching <em>rescues misses</em>. It is only consulted
/// when the lexical tier fails to clear <see cref="ScoreModel.LexicalThreshold"/>, and it
/// never overrides a confident lexical match.
/// </remarks>
public interface ISemanticMatcher
{
    /// <summary>
    /// Best semantic candidate for <paramref name="input"/>, or null when nothing is close
    /// enough. Implementations must be deterministic for a given model + input.
    /// </summary>
    MatchCandidate? Rescue(Normalized input, MatchContext context, IReadOnlyList<IntentDef> eligible);
}

/// <summary>
/// The semantic tier turned off. This is the shipping implementation until Phase 4, and
/// stays the test default afterwards so lexical behavior is asserted in isolation.
/// </summary>
public sealed class NullSemanticMatcher : ISemanticMatcher
{
    public static readonly NullSemanticMatcher Instance = new();

    public MatchCandidate? Rescue(
        Normalized input, MatchContext context, IReadOnlyList<IntentDef> eligible) => null;
}
