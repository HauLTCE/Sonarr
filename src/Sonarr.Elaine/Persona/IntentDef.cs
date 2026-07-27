using System.Text.RegularExpressions;
using Sonarr.Elaine.Matching;

namespace Sonarr.Elaine.Persona;

/// <summary>How a lexical pattern is compared against the normalized input.</summary>
public enum MatchKind
{
    /// <summary>Any listed word appears as a token.</summary>
    Keyword,

    /// <summary>Every listed word appears as a token.</summary>
    AllKeywords,

    /// <summary>A token is within <see cref="LexicalPattern.MaxDistance"/> edits of a word.</summary>
    Fuzzy,

    /// <summary>Regex search on the lowered form; named groups become captures.</summary>
    Regex,

    /// <summary>
    /// A style flag the normalizer already detected (caps, elongated, wall_of_text…).
    /// Matches when the input carries <em>any</em> of the listed flags.
    /// </summary>
    /// <remarks>
    /// Style lives here rather than in a regex because the regexes run against the lowered
    /// text, where shouting is by definition invisible.
    /// </remarks>
    Style,
}

/// <summary>
/// One lexical pattern inside an intent. Every pattern of every eligible intent runs each
/// turn (docs/10: "all matchers run") — there is no first-match short circuit anywhere.
/// </summary>
public sealed record LexicalPattern
{
    public required MatchKind Kind { get; init; }

    /// <summary>Words for keyword/all/fuzzy kinds, flag names for style. Empty for regex.</summary>
    public required IReadOnlyList<string> Words { get; init; }

    /// <summary>Flags for <see cref="MatchKind.Style"/>; none otherwise.</summary>
    public TextStyle Style { get; init; }

    /// <summary>Compiled pattern for <see cref="MatchKind.Regex"/>; null otherwise.</summary>
    public Regex? Regex { get; init; }

    /// <summary>The regex as authored, for validator messages.</summary>
    public string? RegexSource { get; init; }

    /// <summary>Named capture groups this pattern produces. Empty for non-regex kinds.</summary>
    public IReadOnlyList<string> CaptureNames { get; init; } = [];

    public int MaxDistance { get; init; } = 1;

    public int MinLength { get; init; } = 4;

    public required PersonaLocation Location { get; init; }
}

/// <summary>
/// A gate on state rather than on text. Opaque to the matcher: the validator only checks
/// that referenced ids exist, and the decide layer (a later phase) evaluates them.
/// </summary>
/// <param name="Kind">
/// <c>min_tier</c> | <c>max_tier</c> | <c>has_slot</c> | <c>mode</c> | <c>activity</c> |
/// <c>register</c>.
/// </param>
/// <param name="Value">The referenced id, or a <c>register op number</c> expression.</param>
/// <param name="Bonus">Score added when the guard holds. Guards never subtract.</param>
public sealed record GuardDef(string Kind, string Value, double Bonus, PersonaLocation Location)
{
    public const string MinTier = "min_tier";
    public const string MaxTier = "max_tier";
    public const string HasSlot = "has_slot";
    public const string Mode = "mode";
    public const string Activity = "activity";
    public const string Register = "register";
}

/// <summary>
/// An intent: patterns that recognize a thing a user said, plus what to answer with.
/// </summary>
public sealed record IntentDef
{
    public required string Id { get; init; }

    /// <summary>Position in the merged declaration order. Ties in scoring break on this.</summary>
    public required int DeclarationIndex { get; init; }

    public required IReadOnlyList<LexicalPattern> Patterns { get; init; }

    /// <summary>Authored phrasings for the future semantic tier. Unused by lexical matching.</summary>
    public IReadOnlyList<string> SemanticExamples { get; init; } = [];

    public IReadOnlyList<GuardDef> Guards { get; init; } = [];

    /// <summary>Pool the reply is drawn from.</summary>
    public required string Pool { get; init; }

    /// <summary>Optional inline reply template; composed after the pool line when present.</summary>
    public string? Template { get; init; }

    /// <summary>Topic this intent belongs to. Matching the live topic adds affinity score.</summary>
    public string? Topic { get; init; }

    /// <summary>
    /// Slots this intent fills, as slot → capture name. Authored rather than inferred so the
    /// engine never has to know that <c>nm</c> means <c>name</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Learns { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Slots this intent asks the user about. Winning it enqueues a pending question, so she
    /// can notice when the answer never came.
    /// </summary>
    public IReadOnlyList<string> Asks { get; init; } = [];

    /// <summary>Activity to push when this intent wins (rps, argument…).</summary>
    public string? PushActivity { get; init; }

    /// <summary>True if winning this intent pops the current activity.</summary>
    public bool PopActivity { get; init; }

    /// <summary>Register deltas applied on win, by register name.</summary>
    public IReadOnlyList<AffectDelta> Affect { get; init; } = [];

    /// <summary>
    /// True if this intent can also fire as a side-effect acknowledgment alongside a
    /// different primary (docs/10 multi-intent: "hi, I'm Sam and why do you hate me").
    /// </summary>
    public bool SideEffect { get; init; }

    /// <summary>Fires at most once per person.</summary>
    public bool Once { get; init; }

    /// <summary>Logical turns that must pass before this can fire again.</summary>
    public int Cooldown { get; init; }

    /// <summary>Hand-authored specificity override; null means "derive from the patterns".</summary>
    public double? Specificity { get; init; }

    public required PersonaLocation Location { get; init; }
}

public sealed record AffectDelta(string Register, double Delta);
