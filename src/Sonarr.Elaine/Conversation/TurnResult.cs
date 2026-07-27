namespace Sonarr.Elaine.Conversation;

/// <summary>
/// What one turn produced: the next state, the words, and nothing else.
/// </summary>
/// <remarks>
/// docs/10 contract 4, text-only: there is deliberately no field here for a timeout, a role,
/// a delete, or any other server action. The engine can only ever say something or react with
/// an emoji, so a persona edit can never turn into a moderation action.
/// </remarks>
public sealed record TurnResult
{
    /// <summary>State to persist. Always present, even when she says nothing.</summary>
    public required ConversationState State { get; init; }

    /// <summary>The reply, or null when she chose not to speak.</summary>
    public string? Text { get; init; }

    /// <summary>An emoji to react with, on its own or alongside the text.</summary>
    public string? Reaction { get; init; }

    /// <summary>Intent that produced <see cref="Text"/>, for tracing and metrics.</summary>
    public string? IntentId { get; init; }

    /// <summary>Mood mode active this turn, for tracing.</summary>
    public string? ModeId { get; init; }

    /// <summary>
    /// Side-effect acknowledgments that fired alongside the primary — the second half of
    /// "hi, I'm Sam and why do you hate me".
    /// </summary>
    public IReadOnlyList<string> SideEffectIntentIds { get; init; } = [];

    /// <summary>Slots learned this turn, for the adapter to persist as facts.</summary>
    public IReadOnlyDictionary<string, string> LearnedSlots { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Questions that went <see cref="PendingQuestions.StaleAfterTurns"/> unanswered.</summary>
    public IReadOnlyList<PendingQuestion> IgnoredQuestions { get; init; } = [];

    public bool IsSilent => Text is null && Reaction is null;
}
