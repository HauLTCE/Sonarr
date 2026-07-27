using System.Collections.Frozen;

namespace Sonarr.Elaine.Persona;

/// <summary>
/// A named bag of authored lines, optionally with per-mode variants.
/// </summary>
/// <remarks>
/// Every word Sonarr says comes from one of these. The engine picks an index with the
/// turn-seeded RNG and substitutes slots — it never generates text.
/// </remarks>
public sealed record PoolDef
{
    public required string Id { get; init; }

    /// <summary>Lines used when no mode variant applies. May be empty if all modes are covered.</summary>
    public required IReadOnlyList<string> Lines { get; init; }

    /// <summary>Mode id → lines used while that mood mode is active.</summary>
    public required FrozenDictionary<string, IReadOnlyList<string>> ByMode { get; init; }

    public required PersonaLocation Location { get; init; }

    /// <summary>Lines for <paramref name="modeId"/>, falling back to <see cref="Lines"/>.</summary>
    public IReadOnlyList<string> For(string? modeId) =>
        modeId is not null && ByMode.TryGetValue(modeId, out IReadOnlyList<string>? variant)
            ? variant
            : Lines;

    /// <summary>True when the pool can produce nothing at all — a load-blocking error.</summary>
    public bool IsEmpty => Lines.Count == 0 && ByMode.Count == 0;
}

/// <summary>
/// An opinion: topic → stance → the pool she argues it from (docs/10 stance registry).
/// </summary>
public sealed record StanceDef(
    string Topic,
    string Stance,
    string Pool,
    double Strength,
    PersonaLocation Location);

/// <summary>
/// A seasonal or daypart overlay: while active, these pool substitutions apply.
/// </summary>
/// <param name="Replaces">Base pool id → overlay pool id used in its place.</param>
/// <param name="Activation">
/// Opaque activation expression the adapter evaluates from wall-clock signals
/// (e.g. <c>month == 10</c>, <c>hour in 3..6</c>). The engine never reads a clock itself.
/// </param>
public sealed record OverlayDef(
    string Id,
    string Activation,
    FrozenDictionary<string, string> Replaces,
    int Priority,
    PersonaLocation Location);
