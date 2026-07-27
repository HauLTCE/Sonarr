using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Chat;

/// <summary>What the adapter noticed about a message that the intent alone cannot express.</summary>
/// <param name="IntentId">Primary intent the engine picked, or null on a fallback.</param>
/// <param name="Style">Style flags from the normalizer (CAPS, wall-of-text, emoji-flood).</param>
/// <param name="RepeatPing">Same ping content again inside the <c>chat:lastping</c> window.</param>
/// <param name="TurnsSinceLastApology">
/// Logical turns since her last apology from this person, or null if she has none on record.
/// </param>
public sealed record AffectSignals(
    string? IntentId,
    TextStyle Style,
    bool RepeatPing,
    long? TurnsSinceLastApology);

/// <summary>
/// The adapter-side half of the affect engine: register movement that depends on
/// <em>context</em> rather than on which intent matched.
/// </summary>
/// <remarks>
/// Intent-authored <c>affect:</c> blocks stay in the persona — this only adds what YAML cannot
/// see: that this is the third apology in five turns, that the message is in caps, that the same
/// ping arrived twice. Deltas naming a register the persona does not declare are dropped, since
/// an undeclared register has no baseline and would never decay back down.
/// </remarks>
public static class ChatAffect
{
    /// <summary>Apologies closer together than this read as reflex, not remorse.</summary>
    public const int SincereApologyGapTurns = 8;

    /// <summary>Intent ids the sincerity and reaction checks key off. Authored in persona/intents/social.yaml.</summary>
    public static class Intents
    {
        public const string Apology = "APOLOGY";
        public const string Thanks = "THANKS";
    }

    /// <summary>Emoji she reacts with when thanked — a reaction costs no words.</summary>
    public const string ThanksReaction = "👌";

    public static IReadOnlyList<AffectDelta> Deltas(PersonaRoot root, AffectSignals signals)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(signals);

        List<AffectDelta> deltas = [];

        if (signals.IntentId == Intents.Apology)
        {
            deltas.AddRange(IsSincere(signals.TurnsSinceLastApology)
                ? [new AffectDelta(Registers.Names.Anger, -3.0), new AffectDelta(Registers.Names.Grudge, -1.0)]
                // A reflex apology does not buy anything back, and being handed one repeatedly
                // is its own small insult.
                : (AffectDelta[])[new AffectDelta(Registers.Names.Boredom, 1.0)]);
        }

        if (signals.RepeatPing)
        {
            deltas.Add(new AffectDelta(Registers.Names.Boredom, 1.5));
            deltas.Add(new AffectDelta(Registers.Names.Anger, 0.5));
        }

        if ((signals.Style & TextStyle.Caps) != 0)
        {
            deltas.Add(new AffectDelta(Registers.Names.Anger, 1.0));
        }

        if ((signals.Style & TextStyle.WallOfText) != 0)
        {
            deltas.Add(new AffectDelta(Registers.Names.Boredom, 1.0));
        }

        return [.. deltas.Where(d => root.Personality.Baselines.ContainsKey(d.Register))];
    }

    /// <summary>The emoji to react with, or null. Reactions are additive to the reply, not a substitute.</summary>
    public static string? Reaction(string? intentId) =>
        intentId == Intents.Thanks ? ThanksReaction : null;

    /// <summary>First apology on record counts as sincere; a quick repeat does not.</summary>
    public static bool IsSincere(long? turnsSinceLastApology) =>
        turnsSinceLastApology is null || turnsSinceLastApology >= SincereApologyGapTurns;
}
