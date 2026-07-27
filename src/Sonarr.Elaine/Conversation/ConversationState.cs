using System.Collections.Immutable;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Everything the engine knows about one person, as one immutable value.
/// </summary>
/// <remarks>
/// The whole determinism contract rests on this being a value: a turn is
/// <c>(state, input) → (state, reply)</c> with nothing hidden, so a stored trace replays
/// exactly. It maps field-for-field onto <c>chat.person</c> — the adapter loads a row into
/// one of these and writes the returned one back.
/// </remarks>
public sealed record ConversationState
{
    /// <summary>Monotonic per-person turn counter. Also the RNG seed.</summary>
    public required long Turn { get; init; }

    public required ActivityStack Activities { get; init; }

    public required Registers Registers { get; init; }

    public required TopicStack Topics { get; init; }

    public required PendingQuestions Pending { get; init; }

    public required FiredLog Fired { get; init; }

    /// <summary>Remembered slot values templates substitute (<c>name</c>, <c>age</c>, …).</summary>
    public ImmutableDictionary<string, string> Slots { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>The name she picked for you; null until she picks one.</summary>
    public string? AssignedNickname { get; init; }

    /// <summary>
    /// Slots as a template sees them: the stored facts plus <c>nickname</c>.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored in <see cref="Slots"/>, because the nickname has its own
    /// column — writing it into the slots payload too would give it two homes that can disagree.
    /// A line using <c>{nickname}</c> simply does not render until she has picked one, which is
    /// what <see cref="LinePicker"/> already does with any unfilled slot.
    /// </remarks>
    public ImmutableDictionary<string, string> RenderSlots =>
        string.IsNullOrWhiteSpace(AssignedNickname)
            ? Slots
            : Slots.SetItem("nickname", AssignedNickname);

    /// <summary>
    /// Per-conversation RNG salt, supplied by the adapter (hashed user id mixed with the
    /// mood-of-the-day seed) so two people on the same turn do not hear the same line.
    /// </summary>
    public ulong Salt { get; init; }

    /// <summary>State for someone she has never spoken to.</summary>
    public static ConversationState Fresh(PersonaRoot root, ulong salt = 0)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new ConversationState
        {
            Turn = 0,
            Activities = ActivityStack.StartingAt(root.StartActivity),
            Registers = Registers.FromBaselines(root),
            Topics = TopicStack.Empty,
            Pending = PendingQuestions.Empty,
            Fired = FiredLog.Empty,
            Salt = salt,
        };
    }

    /// <summary>What the matcher needs, derived — so it can never disagree with this state.</summary>
    public MatchContext ToMatchContext(PersonaRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new MatchContext(
            Activities.Current,
            ModeSelector.Select(root, Registers).Id,
            ModeSelector.SelectTier(root, Registers[Conversation.Registers.Names.Trust])?.Id,
            Topics.Current,
            Slots.Keys.ToHashSet(StringComparer.Ordinal),
            Registers.Values);
    }
}
