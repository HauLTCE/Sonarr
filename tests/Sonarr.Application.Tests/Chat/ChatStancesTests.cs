using Sonarr.Application.Chat;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// Whose side you took: the pool is the join between <c>stances.yaml</c> and the topic intents,
/// and the persisted fired log is what makes "which opinion did she just voice" answerable.
/// </summary>
public sealed class ChatStancesTests
{
    /// <summary>FOOD draws social_food, which stances.yaml argues pineapple_pizza from.</summary>
    private const string StanceIntent = "FOOD";

    private const string StanceTopic = "pineapple_pizza";

    [Fact]
    public void Agreeing_right_after_she_voiced_an_opinion_takes_her_side()
    {
        StanceTaken taken = Assert.IsType<StanceTaken>(
            ChatStances.Taken(Build.Graph, StateAfter(StanceIntent, turn: 4), ChatStances.AgreeIntent));

        Assert.Equal(StanceTopic, taken.Stance.Topic);
        Assert.True(taken.Agreed);
    }

    [Fact]
    public void Disagreeing_takes_the_other_side_of_the_same_opinion()
    {
        StanceTaken taken = Assert.IsType<StanceTaken>(
            ChatStances.Taken(Build.Graph, StateAfter(StanceIntent, turn: 4), ChatStances.DisagreeIntent));

        Assert.Equal(StanceTopic, taken.Stance.Topic);
        Assert.False(taken.Agreed);
    }

    [Fact]
    public void Agreeing_with_something_she_has_no_opinion_about_is_not_a_side()
    {
        // GREET fires from a pool no stance is argued from, so there is nothing to agree with.
        Assert.Null(ChatStances.Taken(Build.Graph, StateAfter("GREET", turn: 4), ChatStances.AgreeIntent));
    }

    [Fact]
    public void Agreeing_two_topics_later_does_not_reach_back_to_the_old_opinion()
    {
        // The opinion fired on turn 2; we are now answering turn 4's reply, which was about
        // something else. Agreeing now agrees with whatever she said last.
        ConversationState state = StateAfter(StanceIntent, turn: 2) with { Turn = 4 };

        Assert.Null(ChatStances.Taken(Build.Graph, state, ChatStances.AgreeIntent));
    }

    [Fact]
    public void Any_other_intent_costs_no_lookup()
    {
        Assert.Null(ChatStances.Taken(Build.Graph, StateAfter(StanceIntent, turn: 4), "GREET"));
        Assert.Null(ChatStances.Taken(Build.Graph, StateAfter(StanceIntent, turn: 4), intentId: null));
    }

    [Fact]
    public void Every_authored_stance_points_at_a_pool_that_some_intent_draws_from()
    {
        // Otherwise the opinion is unreachable: she holds it but no turn can ever put it on the
        // table. The validator only warns about the reverse direction.
        string[] orphans = [.. Build.Graph.Stances
            .Select(s => s.Pool)
            .Where(pool => !Build.Graph.Intents.Any(i => i.Pool == pool))];

        Assert.Empty(orphans);
    }

    /// <summary>State as it is when her reply on <paramref name="turn"/> fired one intent.</summary>
    private static ConversationState StateAfter(string intentId, long turn)
        => ChatState.FromPerson(new Person(), Build.Graph.Root, 0) with
        {
            Turn = turn,
            Fired = FiredLog.Empty.Record(intentId, turn),
        };
}
