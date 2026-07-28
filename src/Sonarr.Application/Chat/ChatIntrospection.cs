using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Chat;

/// <summary>
/// What <c>/relationship</c> and <c>/memories</c> read (docs/07). Every word comes out of the
/// persona; no register number ever reaches a user.
/// </summary>
/// <remarks>
/// A read-only view of the same state the pipeline writes: no turn is spent, no register moves,
/// nothing is persisted. Asking her how she feels is not a conversation.
/// <para>Lines are drawn with the person's own logical clock as the seed, so the answer is stable
/// while her state is — asking twice in a row does not shuffle her opinion.</para>
/// </remarks>
/// <param name="clock">
/// Only the trend window needs the time — "this week" is wall-clock, and the engine may not read
/// a clock, so it is read here. Optional: without one she still answers, just without trajectory.
/// </param>
public sealed class ChatIntrospection(
    PersonaHolder persona, IPersonRepository people, IClock? clock = null)
{
    /// <summary>Pool id prefix; the suffix is the tier id from <c>sonarr.yaml</c>.</summary>
    public const string TierPoolPrefix = "relationship_";

    public const string ThirdPartyPool = "relationship_third_party";

    /// <summary>Trajectory pools appended to the tier line: which way the last week went.</summary>
    public const string TrendUpPool = "trend_up";

    public const string TrendDownPool = "trend_down";

    /// <summary>How far back a trend looks. "This week" is the phrasing the pools use.</summary>
    public static readonly TimeSpan TrendWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Events scanned for a trend. A week of one person's turns is far below this; the cap only
    /// stops a pathological row count from turning one slash command into a table scan.
    /// </summary>
    public const int TrendEventLimit = 500;

    /// <summary>
    /// Net trust movement below this is flat, and flat gets no line. Roughly one earned moment:
    /// a week that netted a single "good bot" is not a trajectory.
    /// </summary>
    public const double TrendThreshold = 1.0;

    /// <summary>Holding the top or the bottom of the guild's trust ordering gets its own line.</summary>
    public const string TopStandingPool = "standing_top";

    public const string BottomStandingPool = "standing_bottom";

    /// <summary>
    /// Trust this far from neutral before a standing line is even considered. Guards the query:
    /// somebody she has no feeling about is neither a favorite nor an enemy, whatever the
    /// ordering says on a quiet server.
    /// </summary>
    public const double StandingThreshold = 3.0;

    /// <summary>
    /// How many people have to be on a list before topping it means anything. Two: being first
    /// of one is not a ranking.
    /// </summary>
    public const int MinRanked = 2;

    public const string MemoriesPool = "memory_retrieve";
    public const string NoMemoriesPool = "recall_empty";
    public const string ForgotPool = "memory_forget";
    public const string NothingToForgetPool = "memory_forget_missing";

    /// <summary>
    /// How she feels about <paramref name="userId"/>, in her own words.
    /// </summary>
    /// <param name="askerId">
    /// Who ran the command. Asking about somebody else gets a deflection, not their tier — her
    /// state about another user is theirs (docs/06).
    /// </param>
    public async Task<string> DescribeRelationshipAsync(
        long guildId,
        long userId,
        long askerId,
        CancellationToken ct = default)
    {
        PersonaGraph graph = persona.Current;
        if (userId != askerId)
        {
            return Draw(graph, ThirdPartyPool, askerId, turn: 0) ?? "ask them yourself.";
        }

        Person? person = await people.GetAsync(guildId, userId, ct).ConfigureAwait(false);

        // No row means she has never spoken to you, which is exactly the lowest tier — the
        // baselines say so, so there is no separate "unknown" answer to author.
        double trust = person?.Registers.Trust
            ?? graph.Root.Personality.Baselines.GetValueOrDefault(Registers.Names.Trust);

        // Derived here rather than read from person.RelationshipTier: the stored column exists
        // for SQL, and a stale value would let her describe a tier her trust has left.
        string tier = ModeSelector.SelectTier(graph.Root, trust)?.Id ?? string.Empty;

        long turn = person?.LogicalClock ?? 0;
        string level = Draw(graph, TierPoolPrefix + tier, userId, turn)
            // A tier with no authored pool is an unfinished persona, not a broken command.
            ?? Draw(graph, TierPoolPrefix + graph.Root.Tiers[0].Id, userId, turn)
            ?? "no comment.";

        string? trend = person is null
            ? null
            : await TrendAsync(graph, guildId, userId, turn, ct).ConfigureAwait(false);

        string? standing = person is null
            ? null
            : await StandingAsync(graph, guildId, userId, trust, turn, ct).ConfigureAwait(false);

        return string.Join(' ', new[] { level, trend, standing }.Where(s => !string.IsNullOrEmpty(s)));
    }

    /// <summary>
    /// The line for holding the top or the bottom of the guild's trust ordering, or null for
    /// everyone in between — which is nearly everyone.
    /// </summary>
    /// <remarks>
    /// docs/10: "favorites/least-favorites, taking sides, rivalry commentary → queries over
    /// relationship_event + trust ordering, surfaced through authored lines". Only the extremes
    /// get a line: "you're fourth" is a leaderboard, and she does not hand out numbers.
    /// <para>Nobody else is ever named. Telling you who her favorite is would publish that
    /// person's standing to you (docs/06), so the line is about your position and no one
    /// else's — which is also the only rivalry commentary that survives the privacy rule.</para>
    /// <para>One query, and only for someone already at an extreme of their own: a person she
    /// feels nothing about cannot top either list, so the common case costs nothing.</para>
    /// </remarks>
    private async Task<string?> StandingAsync(
        PersonaGraph graph, long guildId, long userId, double trust, long turn, CancellationToken ct)
    {
        if (Math.Abs(trust) < StandingThreshold)
        {
            return null;
        }

        bool least = trust < 0;
        IReadOnlyList<long> ranked = await people
            .GetTrustRankedUsersAsync(guildId, MinRanked, least, ct)
            .ConfigureAwait(false);

        // Two, not one: topping a list nobody else is on is not a ranking, and "you're my
        // favorite" on a server where she likes exactly one person is a joke at her expense.
        return ranked.Count >= MinRanked && ranked[0] == userId
            ? Draw(graph, least ? BottomStandingPool : TopStandingPool, userId, turn)
            : null;
    }

    /// <summary>
    /// The trajectory line for the last week, or null when the week was flat.
    /// </summary>
    /// <remarks>
    /// docs/04: <c>chat.relationship_event</c> exists so she can answer about direction, not just
    /// level — "you're a regular" and "and it's getting worse" are both true at once. Trust is the
    /// register the tiers are built on, so it is the one a trend is about; summing the stored
    /// deltas is exactly the movement she caused, with decay excluded because decay is not
    /// something you did.
    /// <para>Flat weeks say nothing. A line about no movement is worse than no line.</para>
    /// </remarks>
    private async Task<string?> TrendAsync(
        PersonaGraph graph, long guildId, long userId, long turn, CancellationToken ct)
    {
        if (clock is null)
        {
            return null;
        }

        IReadOnlyList<RelationshipEvent> events = await people.GetRecentEventsAsync(
            guildId, userId, clock.UtcNow - TrendWindow, TrendEventLimit, ct).ConfigureAwait(false);

        double moved = events.Sum(e => Trust(e.Delta));
        if (Math.Abs(moved) < TrendThreshold)
        {
            return null;
        }

        return Draw(graph, moved > 0 ? TrendUpPool : TrendDownPool, userId, turn);
    }

    /// <summary>
    /// The trust component of one stored delta. A payload without one contributes nothing — the
    /// delta is jsonb, so a row written by an older shape must not break the command.
    /// </summary>
    private static double Trust(JsonObject delta) =>
        delta.TryGetPropertyValue(Registers.Names.Trust, out JsonNode? node)
            && node is JsonValue value
            && value.GetValueKind() == JsonValueKind.Number
            && double.TryParse(
                value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double moved)
            ? moved
            : 0;

    /// <summary>Her opener plus the facts she is holding, newest first.</summary>
    public async Task<MemoryReport> ListMemoriesAsync(
        long guildId,
        long userId,
        CancellationToken ct = default)
    {
        PersonaGraph graph = persona.Current;
        IReadOnlyList<Fact> facts = await people.GetFactsAsync(guildId, userId, ct).ConfigureAwait(false);

        string pool = facts.Count == 0 ? NoMemoriesPool : MemoriesPool;
        return new MemoryReport(
            Draw(graph, pool, userId, facts.Count) ?? string.Empty,
            [.. facts.Select(f => new RememberedFact(f.Predicate, f.Value, f.Confidence, f.LearnedAt))]);
    }

    /// <summary>
    /// Drops one fact. Returns her line either way — "I never had that" is also an answer.
    /// </summary>
    public async Task<string> ForgetAsync(
        long guildId,
        long userId,
        string predicate,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(predicate);

        bool forgotten = await people.ForgetFactAsync(guildId, userId, predicate, ct).ConfigureAwait(false);
        return Draw(
            persona.Current,
            forgotten ? ForgotPool : NothingToForgetPool,
            userId,
            (long)(StableHash.Of(predicate) % int.MaxValue)) ?? "fine.";
    }

    private static string? Draw(PersonaGraph graph, string poolId, long userId, long turn) =>
        new LinePicker(graph).Pick(
            poolId,
            modeId: null,
            new TurnSeededRandom(turn, StableHash.Of(userId.ToString(CultureInfo.InvariantCulture))),
            EmptySlots);

    private static readonly IReadOnlyDictionary<string, string> EmptySlots =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <param name="Opener">Her authored line introducing the list, or the "nothing here" line.</param>
public sealed record MemoryReport(string Opener, IReadOnlyList<RememberedFact> Facts);

/// <param name="Predicate">job, pet, favorite_food… — also the value <c>/memories forget</c> takes.</param>
public sealed record RememberedFact(
    string Predicate,
    string Value,
    float Confidence,
    DateTimeOffset LearnedAt);
