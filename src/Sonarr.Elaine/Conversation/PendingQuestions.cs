using System.Collections.Immutable;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Questions she asked that nobody has answered yet, oldest first.
/// </summary>
/// <remarks>
/// docs/10: "she notices unanswered questions". The queue is what makes that possible — a
/// question is enqueued when she asks it, dequeued when the next message plausibly answers
/// it, and dropped once it has gone <see cref="StaleAfterTurns"/> turns unanswered so she
/// never litigates something from last week.
/// </remarks>
public sealed record PendingQuestions
{
    /// <summary>Two open questions is already pushy; more is an interrogation.</summary>
    public const int Capacity = 2;

    /// <summary>Turns a question stays live before she lets it go.</summary>
    public const int StaleAfterTurns = 3;

    public static readonly PendingQuestions Empty = new([]);

    private readonly ImmutableList<PendingQuestion> _queue;

    private PendingQuestions(ImmutableList<PendingQuestion> queue) => _queue = queue;

    public static PendingQuestions Restore(IEnumerable<PendingQuestion>? questions) =>
        new([.. (questions ?? [])
            .Where(q => !string.IsNullOrWhiteSpace(q.Slot))
            .OrderBy(q => q.AskedAtTurn)
            .Take(Capacity)]);

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<PendingQuestion> Queue => _queue;

    public bool Any => _queue.Count > 0;

    /// <summary>The question an answer would be answering: the one she asked most recently.</summary>
    public PendingQuestion? Newest => _queue.Count > 0 ? _queue[^1] : null;

    /// <summary>Records a question. A repeat of the same slot re-dates it rather than stacking.</summary>
    public PendingQuestions Ask(string slot, long turn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ImmutableList<PendingQuestion> next = _queue
            .RemoveAll(q => q.Slot == slot)
            .Add(new PendingQuestion(slot, turn));
        return new PendingQuestions(next.Count > Capacity
            ? next.RemoveRange(0, next.Count - Capacity)
            : next);
    }

    /// <summary>Marks <paramref name="slot"/> answered.</summary>
    public PendingQuestions Answer(string slot) =>
        new(_queue.RemoveAll(q => q.Slot == slot));

    /// <summary>
    /// Anything asked more than <see cref="StaleAfterTurns"/> turns ago, dropped from the
    /// queue and handed back so the caller can decide whether to call it out.
    /// </summary>
    public (PendingQuestions Remaining, IReadOnlyList<PendingQuestion> Ignored) Expire(long turn)
    {
        List<PendingQuestion> ignored =
            [.. _queue.Where(q => turn - q.AskedAtTurn > StaleAfterTurns)];
        return ignored.Count == 0
            ? (this, [])
            : (new PendingQuestions(_queue.RemoveRange(ignored)), ignored);
    }
}

/// <param name="Slot">Memory slot the answer would fill (<c>name</c>, <c>age</c>, …).</param>
/// <param name="AskedAtTurn">Logical turn she asked, for staleness.</param>
public sealed record PendingQuestion(string Slot, long AskedAtTurn);
