using System.Collections.Immutable;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// The activity stack: what she is currently doing, innermost last.
/// </summary>
/// <remarks>
/// This replaces the old single-FSM-state design (docs/10). A nested activity — "we were
/// arguing, then started a game of rps" — used to need a state per combination, and quitting
/// the game lost the argument. Here rps sits on top of argument and popping it restores what
/// was underneath.
/// <para>Persisted verbatim to <c>chat.person.activity_stack</c>, so a restart mid-game
/// resumes mid-game.</para>
/// </remarks>
public sealed record ActivityStack
{
    /// <summary>
    /// Deep enough for the nesting anyone actually authors (idle → argument → rps), shallow
    /// enough that a push-happy loop cannot grow the jsonb column without bound.
    /// </summary>
    public const int MaxDepth = 4;

    private readonly ImmutableList<string> _layers;

    private ActivityStack(ImmutableList<string> layers) => _layers = layers;

    /// <summary>A fresh stack holding only the persona's <c>start_activity</c>.</summary>
    public static ActivityStack StartingAt(string activityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        return new ActivityStack([activityId]);
    }

    /// <summary>
    /// Rebuilds a stack from storage. Empty or over-deep input falls back to
    /// <paramref name="startActivity"/> rather than throwing: a hand-edited row must not be
    /// able to stop her from replying.
    /// </summary>
    public static ActivityStack Restore(IEnumerable<string>? layers, string startActivity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startActivity);
        ImmutableList<string> restored = [.. (layers ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Take(MaxDepth)];
        return restored.IsEmpty ? StartingAt(startActivity) : new ActivityStack(restored);
    }

    /// <summary>Top of the stack — the activity that decides which intents are eligible.</summary>
    public string Current => _layers[^1];

    public int Depth => _layers.Count;

    /// <summary>Bottom-first, as persisted.</summary>
    public IReadOnlyList<string> Layers => _layers;

    /// <summary>
    /// Pushes <paramref name="activityId"/>. Re-pushing the current activity is a no-op, and
    /// a push at <see cref="MaxDepth"/> replaces the top instead of growing.
    /// </summary>
    public ActivityStack Push(string activityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        if (Current == activityId)
        {
            return this;
        }

        return _layers.Count >= MaxDepth
            ? new ActivityStack(_layers.SetItem(_layers.Count - 1, activityId))
            : new ActivityStack(_layers.Add(activityId));
    }

    /// <summary>Pops the top layer. The bottom layer never pops — she is always doing something.</summary>
    public ActivityStack Pop() =>
        _layers.Count <= 1 ? this : new ActivityStack(_layers.RemoveAt(_layers.Count - 1));

    public bool Contains(string activityId) => _layers.Contains(activityId);
}
