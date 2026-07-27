using System.Collections.Frozen;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// What the matcher knows about the conversation. Everything the score depends on is here,
/// so scoring is a pure function of (input, persona, this).
/// </summary>
/// <param name="ActivityId">Top of the activity stack; limits which intents are eligible.</param>
/// <param name="ModeId">Active mood mode, for <c>mode</c> guards.</param>
/// <param name="TierId">Relationship tier, for tier guards.</param>
/// <param name="Topic">Live conversation topic; matching intents get affinity score.</param>
/// <param name="Slots">Memory slots that are set, for <c>has_slot</c> guards.</param>
/// <param name="Registers">Register values, for <c>register</c> guards.</param>
public sealed record MatchContext(
    string ActivityId,
    string? ModeId = null,
    string? TierId = null,
    string? Topic = null,
    IReadOnlySet<string>? Slots = null,
    IReadOnlyDictionary<string, double>? Registers = null)
{
    public static readonly MatchContext Empty = new("idle");
}

/// <summary>One intent that matched, with its score and the captures it produced.</summary>
public sealed record MatchCandidate
{
    public required IntentDef Intent { get; init; }

    /// <summary>specificity + match-length + corroboration + topic affinity + guard bonuses.</summary>
    public required double Score { get; init; }

    /// <summary>Named captures, sliced case-preserved out of the original text.</summary>
    public required FrozenDictionary<string, string> Captures { get; init; }

    /// <summary>The single pattern that contributed the score. Useful for tracing.</summary>
    public required LexicalPattern BestPattern { get; init; }

    /// <summary>How many of the intent's patterns matched.</summary>
    public required int MatchedPatternCount { get; init; }

    public string IntentId => Intent.Id;
}

/// <summary>
/// The matcher's verdict for a turn: everything that matched, ranked, plus who won.
/// </summary>
public sealed record MatchOutcome
{
    /// <summary>Ranked candidates, highest score first, declaration order breaking ties.</summary>
    public required IReadOnlyList<MatchCandidate> Ranked { get; init; }

    /// <summary>The winner, or null when nothing matched.</summary>
    public MatchCandidate? Primary => Ranked.Count > 0 ? Ranked[0] : null;

    /// <summary>
    /// Lower-scoring matches flagged <c>side_effect</c>, for multi-intent acknowledgments
    /// ("hi, I'm Sam and why do you hate me" answers both halves).
    /// </summary>
    public required IReadOnlyList<MatchCandidate> SideEffects { get; init; }

    /// <summary>True when the best lexical score cleared the confidence threshold.</summary>
    public bool IsConfident => Primary is not null && Primary.Score >= ScoreModel.LexicalThreshold;

    public static readonly MatchOutcome None = new() { Ranked = [], SideEffects = [] };
}
