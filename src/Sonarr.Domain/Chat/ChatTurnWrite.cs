using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Domain.Chat;

/// <summary>
/// Everything one chat turn changed, as one unit of work.
/// </summary>
/// <remarks>
/// docs/10: person, facts, episodes and the relationship event commit in <b>one</b>
/// transaction. Splitting them is how the old bot ended up with a person whose registers said
/// she was furious and no event explaining why — and with facts learned in a turn she does not
/// remember having.
/// </remarks>
public sealed record ChatTurnWrite
{
    /// <summary>The person row after the turn. Inserted when it is new.</summary>
    public required Person Person { get; init; }

    /// <summary>Slots learned this turn, upserted as facts (repeats reinforce confidence).</summary>
    public IReadOnlyList<FactWrite> Facts { get; init; } = [];

    /// <summary>New episodic rows. Embeddings are backfilled later by a job.</summary>
    public IReadOnlyList<Episode> Episodes { get; init; } = [];

    /// <summary>Register movement, with the intent that caused it.</summary>
    public IReadOnlyList<RelationshipEvent> Events { get; init; } = [];

    /// <summary>The side they took on one of her opinions this turn, when they took one.</summary>
    public StanceWrite? Stance { get; init; }
}

/// <summary>
/// Whose side you took, as one turn produced it.
/// </summary>
/// <remarks>
/// Carries the opinion itself, not just the topic: <c>chat.stance</c> is a projection of the
/// authored registry in <c>persona/stances.yaml</c>, so the row is upserted from this on the way
/// past rather than synced by a startup service. One less moving part, and an opinion reworded in
/// YAML corrects the table the next time it comes up.
/// </remarks>
/// <param name="Topic">Registry key: <c>pineapple_pizza</c>, <c>crypto</c>, …</param>
/// <param name="Position">The position she holds, verbatim from the persona.</param>
/// <param name="PoolRef">Pool she argues it from.</param>
/// <param name="Agreed">True when they took her side.</param>
public sealed record StanceWrite(string Topic, string Position, string PoolRef, bool Agreed);

/// <summary>
/// A fact to remember. Not a <see cref="Fact"/> because the repository decides whether this
/// is an insert, a reinforcement of the same value, or a supersede of an older one.
/// </summary>
/// <param name="Predicate">job, pet, favorite_food, name, …</param>
/// <param name="LearnedAtTurn">Person logical clock at learn time.</param>
public sealed record FactWrite(string Predicate, string Value, long LearnedAtTurn);
