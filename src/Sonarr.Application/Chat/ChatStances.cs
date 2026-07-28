using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Chat;

/// <summary>One of her opinions, and whether you took her side on it.</summary>
/// <param name="Agreed">True when they agreed with her, false when they said she was wrong.</param>
public sealed record StanceTaken(StanceDef Stance, bool Agreed);

/// <summary>
/// Reads the stance registry: which opinion was on the table when somebody agreed or disagreed.
/// </summary>
/// <remarks>
/// docs/04: <c>chat.stance</c> is her opinions registry and <c>chat.stance_agreement</c> is
/// "she remembers whose side you took". The registry itself is authored in
/// <c>persona/stances.yaml</c> — the table is a projection of it, so the FK has something to
/// point at.
/// <para>The join is the pool, not a second set of topic labels: <c>stances.yaml</c> says
/// pineapple_pizza is argued from <c>social_food</c>, and the FOOD intent draws from
/// <c>social_food</c>, so a turn that drew that pool put that opinion on the table. Nothing has
/// to be authored twice, and a new topic intent inherits the stance for free.</para>
/// <para>This lives in the adapter rather than the engine because it is a lookup across turns:
/// the engine sees one turn and has no business knowing what a database row means.</para>
/// </remarks>
public static class ChatStances
{
    /// <summary>Intent ids that are a side being taken. Authored in persona/intents/social.yaml.</summary>
    public const string AgreeIntent = "AGREE";

    public const string DisagreeIntent = "DISAGREE";

    /// <summary>
    /// The opinion this turn took a side on, or null — which is nearly every turn.
    /// </summary>
    /// <param name="before">
    /// State as it was <em>before</em> this turn, whose fired log still has her last reply's intent
    /// on its newest turn. That log is persisted, so agreeing across a restart still counts.
    /// </param>
    public static StanceTaken? Taken(PersonaGraph graph, ConversationState before, string? intentId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(before);

        bool agreed = string.Equals(intentId, AgreeIntent, StringComparison.Ordinal);
        if (!agreed && !string.Equals(intentId, DisagreeIntent, StringComparison.Ordinal))
        {
            return null;
        }

        // "you're wrong" is only a side taken if she had just said something to be wrong about.
        // Only the immediately previous turn counts: agreeing two topics later is agreeing with
        // whatever she said last, not with the opinion you have both moved on from.
        StanceDef? stance = before.Fired.Entries
            .Where(e => e.Value == before.Turn)
            .Select(e => graph.IntentsById.GetValueOrDefault(e.Key))
            .Where(i => i is not null)
            .Select(i => graph.StancesByPool.GetValueOrDefault(i!.Pool))
            .FirstOrDefault(s => s is not null);

        return stance is null ? null : new StanceTaken(stance, agreed);
    }
}
