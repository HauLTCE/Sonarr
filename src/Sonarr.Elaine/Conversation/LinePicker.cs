using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Draws one line from a pool: overlay substitution, mode variant, seeded pick, template
/// substitution.
/// </summary>
/// <remarks>
/// The only place text enters a reply. Every line it can return was authored in
/// <c>persona/pools/</c>; the RNG picks an index and nothing else.
/// </remarks>
public sealed class LinePicker(PersonaGraph persona, IReadOnlyList<string>? activeOverlays = null)
{
    private readonly PersonaGraph _persona = persona
        ?? throw new ArgumentNullException(nameof(persona));

    /// <summary>
    /// Overlay ids the adapter has activated for this turn (october, latenight…). Highest
    /// priority wins a pool both replace — the validator has already refused personas where
    /// that tie is arbitrary.
    /// </summary>
    private readonly IReadOnlyList<OverlayDef> _overlays = [.. persona.Overlays
        .Where(o => activeOverlays?.Contains(o.Id, StringComparer.Ordinal) == true)
        .OrderByDescending(o => o.Priority)];

    /// <summary>
    /// One line from <paramref name="poolId"/>, or null when the pool cannot produce a
    /// renderable line for this state.
    /// </summary>
    public string? Pick(
        string poolId,
        string? modeId,
        IDeterministicRandom rng,
        IReadOnlyDictionary<string, string> slots,
        IReadOnlyDictionary<string, string>? captures = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolId);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(slots);

        string resolved = Resolve(poolId);
        if (!_persona.Pools.TryGetValue(resolved, out PoolDef? pool))
        {
            return null;
        }

        IReadOnlyList<string> lines = pool.For(modeId);
        if (lines.Count == 0)
        {
            return null;
        }

        // Walk from the drawn index rather than re-drawing: a line whose slot is not filled
        // yet must not cost her the reply, and re-drawing on the same (turn, purpose) seed
        // would return the same index forever.
        int start = rng.Next(resolved, lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[(start + i) % lines.Count];
            if (TemplateRenderer.TryRender(
                line, slots, captures ?? EmptyCaptures, out string rendered))
            {
                return rendered;
            }
        }

        return null;
    }

    /// <summary>The pool an active overlay substitutes for <paramref name="poolId"/>, if any.</summary>
    public string Resolve(string poolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolId);
        foreach (OverlayDef overlay in _overlays)
        {
            if (overlay.Replaces.TryGetValue(poolId, out string? replacement))
            {
                return replacement;
            }
        }

        return poolId;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyCaptures =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
