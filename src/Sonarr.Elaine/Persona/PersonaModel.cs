using System.Collections.Frozen;

namespace Sonarr.Elaine.Persona;

/// <summary>
/// A loaded, frozen persona. Immutable, so it can be shared across turns and hot-swapped
/// atomically (docs/10: a broken edit never takes her down — the old graph stays live).
/// </summary>
public sealed record PersonaGraph
{
    public required PersonaRoot Root { get; init; }

    /// <summary>Intents in declared order. Order breaks scoring ties and nothing else.</summary>
    public required IReadOnlyList<IntentDef> Intents { get; init; }

    public required FrozenDictionary<string, PoolDef> Pools { get; init; }

    public required IReadOnlyList<StanceDef> Stances { get; init; }

    public required IReadOnlyList<OverlayDef> Overlays { get; init; }

    private FrozenDictionary<string, IntentDef>? _intentIndex;

    /// <summary>Intent by id (case-sensitive, as authored).</summary>
    public FrozenDictionary<string, IntentDef> IntentsById =>
        _intentIndex ??= Intents.ToFrozenDictionary(i => i.Id, StringComparer.Ordinal);

    private FrozenDictionary<string, StanceDef>? _stanceIndex;

    /// <summary>
    /// Stance by the pool it argues from — which opinion an intent voices when it fires.
    /// </summary>
    /// <remarks>
    /// The pool is the join: <c>stances.yaml</c> says pineapple_pizza is argued from
    /// <c>social_food</c>, and the FOOD intent draws from <c>social_food</c>, so a turn that drew
    /// that pool put that opinion on the table. Nothing else has to be authored twice.
    /// <para>First stance wins a shared pool. Two opinions from one pool is an authoring mistake,
    /// but throwing here would take her down on a hot reload, which is the one thing the graph is
    /// built not to do.</para>
    /// </remarks>
    public FrozenDictionary<string, StanceDef> StancesByPool =>
        _stanceIndex ??= Stances
            .GroupBy(s => s.Pool, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
}

/// <summary>Contents of <c>sonarr.yaml</c>: the skeleton every other file hangs off.</summary>
public sealed record PersonaRoot
{
    public required int Version { get; init; }

    /// <summary>Activity that a fresh conversation starts in.</summary>
    public required string StartActivity { get; init; }

    public required IReadOnlyList<ActivityDef> Activities { get; init; }

    /// <summary>Mood register baselines and per-turn decay, by register name.</summary>
    public required PersonalityDef Personality { get; init; }

    /// <summary>Relationship tiers, ascending. Guards reference these by id.</summary>
    public required IReadOnlyList<TierDef> Tiers { get; init; }

    /// <summary>Mood modes in priority order; exactly one is the <c>always</c> fallback.</summary>
    public required IReadOnlyList<ModeDef> Modes { get; init; }

    /// <summary>Pool ids that must have a variant for every non-fallback mode.</summary>
    public required IReadOnlyList<string> ModeCoveragePools { get; init; }

    /// <summary>
    /// Pool ids the host application draws by name rather than through an intent, activity,
    /// stance or overlay — <c>/relationship</c>, <c>/memories</c>, <c>/ship</c>, milestones,
    /// season close. Reachable, just not from here.
    /// </summary>
    /// <remarks>
    /// Declared in YAML because the dependency only runs one way: <c>Sonarr.Application</c>
    /// references this assembly, so the validator cannot see a <c>const string</c> over there.
    /// Without the declaration those pools look unreferenced, and they were — 22 of the 78
    /// reported orphans were commands that work, which is worse than a miscount: it buries the
    /// pools nothing really reaches under noise nobody can act on.
    /// <para>Only the ids C# names literally. A pool the host builds by prefix
    /// (<c>relationship_</c> + tier id) still has to be listed, because a prefix is not a
    /// reference the validator can check.</para>
    /// </remarks>
    public required IReadOnlyList<string> HostPools { get; init; }

    /// <summary>Declared memory slot keys. Templates may only reference these as <c>{slot}</c>.</summary>
    public required IReadOnlyList<string> Slots { get; init; }

    public PersonaLocation Location { get; init; } = new("sonarr.yaml", "");
}

/// <param name="Baselines">Register → resting value.</param>
/// <param name="Decay">Register → amount pulled back toward baseline per turn.</param>
public sealed record PersonalityDef(
    FrozenDictionary<string, double> Baselines,
    FrozenDictionary<string, double> Decay);

/// <summary>One layer of the activity stack (idle / rps / argument / story-scene…).</summary>
/// <param name="Intents">Intent ids eligible in this activity; <c>["*"]</c> means all.</param>
/// <param name="FallbackPool">Pool used when nothing scores above threshold.</param>
public sealed record ActivityDef(
    string Id,
    IReadOnlyList<string> Intents,
    string FallbackPool,
    PersonaLocation Location)
{
    public const string AllIntents = "*";

    public bool Allows(string intentId) =>
        Intents.Contains(AllIntents, StringComparer.Ordinal)
        || Intents.Contains(intentId, StringComparer.Ordinal);
}

/// <param name="MinTrust">Lower bound of the trust range this tier covers.</param>
public sealed record TierDef(string Id, double MinTrust, PersonaLocation Location);

/// <param name="When">Opaque register conditions, ALL of which must hold.</param>
/// <param name="Always">True for the single fallback mode (NEUTRAL).</param>
public sealed record ModeDef(
    string Id,
    IReadOnlyList<string> When,
    bool Always,
    PersonaLocation Location);
