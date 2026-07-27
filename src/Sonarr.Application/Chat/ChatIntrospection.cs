using System.Globalization;
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
public sealed class ChatIntrospection(PersonaHolder persona, IPersonRepository people)
{
    /// <summary>Pool id prefix; the suffix is the tier id from <c>sonarr.yaml</c>.</summary>
    public const string TierPoolPrefix = "relationship_";

    public const string ThirdPartyPool = "relationship_third_party";
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

        return Draw(graph, TierPoolPrefix + tier, userId, person?.LogicalClock ?? 0)
            // A tier with no authored pool is an unfinished persona, not a broken command.
            ?? Draw(graph, TierPoolPrefix + graph.Root.Tiers[0].Id, userId, person?.LogicalClock ?? 0)
            ?? "no comment.";
    }

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
