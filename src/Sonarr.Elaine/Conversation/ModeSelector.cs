using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Derives the active mood mode from the registers: first mode whose <c>when</c> clauses
/// all hold, else the single <c>always</c> mode.
/// </summary>
/// <remarks>
/// Derived, never stored. Storing a mode would let it disagree with the registers that
/// produced it, and the validator already guarantees exactly one <c>always</c> mode exists
/// — so this is total, and pool lookups never have to handle "no mode".
/// </remarks>
public static class ModeSelector
{
    public static ModeDef Select(PersonaRoot root, Registers registers)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registers);

        foreach (ModeDef mode in root.Modes)
        {
            if (mode.Always)
            {
                continue;
            }

            if (mode.When.Count > 0
                && mode.When.All(w => RegisterExpression.Holds(w, registers.Values)))
            {
                return mode;
            }
        }

        return root.Modes.FirstOrDefault(m => m.Always)
            ?? throw new InvalidOperationException(
                "persona has no 'always' mode; the validator should have refused to load it");
    }

    /// <summary>Tier for a trust value: the highest tier whose <c>min_trust</c> is met.</summary>
    /// <remarks>
    /// Nemesis sits at negative trust, so "highest met" naturally picks it — being disliked
    /// is a tier, not the absence of one.
    /// <para>Below every threshold the bottom tier still wins. Trust clamps at
    /// <see cref="Registers.Min"/>, which is well under the lowest authored <c>min_trust</c>, so
    /// "highest met" alone would leave the people who hate her hardest with no tier — and a
    /// tierless person has no authored description and no guard that admits them.</para>
    /// </remarks>
    public static TierDef? SelectTier(PersonaRoot root, double trust)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.Tiers
            .Where(t => trust >= t.MinTrust)
            .OrderByDescending(t => t.MinTrust)
            .FirstOrDefault()
            ?? root.Tiers.OrderBy(t => t.MinTrust).FirstOrDefault();
    }
}
