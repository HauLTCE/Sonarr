using System.Collections.Immutable;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Which intents have fired for this person and when — intent id → last logical turn.
/// </summary>
/// <remarks>
/// This is the fix for the bug docs/10 calls out: in the old engine the fired-log lived on
/// the per-message context, so it was empty again on the next message and <c>once:</c> meant
/// "once per message" (i.e. nothing) while <c>cooldown:</c> never held. Persisting it to
/// <c>chat.person.fired_log</c> is the whole fix; the rest of this type is bookkeeping.
/// <para>Turn numbers, not timestamps: cooldowns are authored in turns and the engine has no
/// clock.</para>
/// </remarks>
public sealed record FiredLog
{
    /// <summary>
    /// Ceiling on tracked intents. Cooldowns only need the recent past, and an unbounded map
    /// would grow one jsonb key per intent per person forever.
    /// </summary>
    public const int MaxEntries = 128;

    public static readonly FiredLog Empty = new(ImmutableDictionary<string, long>.Empty);

    private readonly ImmutableDictionary<string, long> _lastFired;

    private FiredLog(ImmutableDictionary<string, long> lastFired) => _lastFired = lastFired;

    public static FiredLog Restore(IEnumerable<KeyValuePair<string, long>>? entries) =>
        new((entries ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Key))
            .OrderByDescending(e => e.Value)
            .Take(MaxEntries)
            .ToImmutableDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal));

    /// <summary>Intent id → logical turn it last fired. Persist this verbatim.</summary>
    public IReadOnlyDictionary<string, long> Entries => _lastFired;

    public bool HasFired(string intentId) => _lastFired.ContainsKey(intentId);

    public long? LastTurn(string intentId) =>
        _lastFired.TryGetValue(intentId, out long turn) ? turn : null;

    /// <summary>
    /// True when <paramref name="intent"/> is allowed to fire on <paramref name="turn"/> —
    /// its <c>once</c> and <c>cooldown</c> constraints both satisfied.
    /// </summary>
    public bool IsEligible(IntentDef intent, long turn)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (LastTurn(intent.Id) is not { } last)
        {
            return true;
        }

        // once wins over cooldown when both are authored: "never again" is the stronger claim.
        return !intent.Once && turn - last >= intent.Cooldown;
    }

    /// <summary>Records a firing, evicting the least-recent entry when full.</summary>
    public FiredLog Record(string intentId, long turn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ImmutableDictionary<string, long> next = _lastFired.SetItem(intentId, turn);
        if (next.Count <= MaxEntries)
        {
            return new FiredLog(next);
        }

        // Evicting the oldest can resurrect a `once:` intent. Accepted: MaxEntries is far
        // above how many once-intents a persona has, and an extra authored line beats
        // unbounded growth.
        // ponytail: LRU eviction; if a persona ever exceeds MaxEntries once-intents, keep
        // once-entries unconditionally and evict only cooldown ones.
        string oldest = next.OrderBy(e => e.Value).First().Key;
        return new FiredLog(next.Remove(oldest));
    }
}
