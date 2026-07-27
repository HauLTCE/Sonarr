using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// Evaluates a <see cref="GuardDef"/> against a <see cref="MatchContext"/>.
/// </summary>
/// <remarks>
/// Guards are state gates, not text matchers. A guard that fails removes its intent from
/// the running; a guard that holds contributes its bonus to the score. Unknown kinds fail
/// closed — the validator has already rejected them at load time, so reaching one here
/// means the persona was built by something other than the loader.
/// </remarks>
public static class GuardEvaluator
{
    /// <param name="root">
    /// Needed for the tier guards only: <c>min_tier</c> is a floor, not an equality, so the
    /// guard's tier and the person's tier both have to be placed on the persona's own
    /// trust ordering.
    /// </param>
    public static bool Holds(GuardDef guard, MatchContext context, PersonaRoot root)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(root);

        return guard.Kind switch
        {
            GuardDef.HasSlot => context.Slots?.Contains(guard.Value) == true,
            GuardDef.Mode => context.ModeId == guard.Value,
            GuardDef.Activity => context.ActivityId == guard.Value,
            GuardDef.MinTier => Tier(root, context.TierId) >= Tier(root, guard.Value),
            GuardDef.MaxTier => Tier(root, context.TierId) <= Tier(root, guard.Value),
            GuardDef.Register => RegisterExpression.Holds(guard.Value, context.Registers),
            _ => false,
        };
    }

    /// <summary>
    /// A tier's position on the trust ladder. An unknown or absent tier sits below every
    /// authored one, so <c>min_tier</c> fails closed for a person with no tier — she does not
    /// hand out inner-circle lines to someone she cannot place.
    /// </summary>
    private static double Tier(PersonaRoot root, string? tierId) =>
        tierId is null
            ? double.NegativeInfinity
            : root.Tiers.FirstOrDefault(t => t.Id == tierId)?.MinTrust ?? double.NegativeInfinity;
}
