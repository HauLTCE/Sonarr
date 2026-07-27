namespace Sonarr.Elaine.Conversation;

/// <summary>
/// One inbound message plus the wall-clock signals the adapter derived from it.
/// </summary>
/// <remarks>
/// The engine has no clock, so anything time-shaped arrives here already reduced to a
/// number or an id: elapsed absence becomes <see cref="ExtraDecaySteps"/>, the hour and the
/// month become <see cref="ActiveOverlays"/>, the day becomes part of
/// <see cref="ConversationState.Salt"/>. That is what keeps a turn replayable — the trace
/// stores this record, not "now".
/// </remarks>
public sealed record TurnInput
{
    /// <summary>Raw message text, as typed.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Overlay ids active for this turn (october, latenight), from the adapter's clock.
    /// </summary>
    public IReadOnlyList<string> ActiveOverlays { get; init; } = [];

    /// <summary>
    /// Extra register-decay steps for time that passed since the last turn, so a grudge cools
    /// over days away rather than only over messages.
    /// </summary>
    public int ExtraDecaySteps { get; init; }

    /// <summary>
    /// An authored callback tail from a relevance-gated episode lookup, or null. The adapter
    /// runs the pgvector query; the engine only decides whether to use the line.
    /// </summary>
    public string? Callback { get; init; }
}
