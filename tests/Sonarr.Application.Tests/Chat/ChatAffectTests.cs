using Sonarr.Application.Chat;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// The context half of the affect engine: the movement the persona's <c>affect:</c> blocks cannot
/// express because it depends on what happened before this message, not on what it says.
/// </summary>
public sealed class ChatAffectTests
{
    private static PersonaRoot Root => Build.Graph.Root;

    [Fact]
    public void A_first_apology_counts_as_sincere()
    {
        Assert.True(ChatAffect.IsSincere(null));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(ChatAffect.SincereApologyGapTurns - 1, false)]
    [InlineData(ChatAffect.SincereApologyGapTurns, true)]
    [InlineData(50, true)]
    public void Sincerity_is_the_gap_since_the_last_apology(long gap, bool sincere)
    {
        Assert.Equal(sincere, ChatAffect.IsSincere(gap));
    }

    [Fact]
    public void A_sincere_apology_cools_anger_and_chips_at_the_grudge()
    {
        IReadOnlyList<AffectDelta> deltas = Apology(turnsSince: null);

        Assert.True(Delta(deltas, Registers.Names.Anger) < 0);
        Assert.True(Delta(deltas, Registers.Names.Grudge) < 0);
    }

    [Fact]
    public void A_reflex_apology_buys_nothing_back()
    {
        IReadOnlyList<AffectDelta> deltas = Apology(turnsSince: 1);

        Assert.Equal(0, Delta(deltas, Registers.Names.Anger));
        Assert.Equal(0, Delta(deltas, Registers.Names.Grudge));
        Assert.True(Delta(deltas, Registers.Names.Boredom) > 0);
    }

    [Fact]
    public void A_repeat_ping_bores_and_mildly_annoys_her()
    {
        IReadOnlyList<AffectDelta> deltas = ChatAffect.Deltas(
            Root, new AffectSignals(null, TextStyle.None, RepeatPing: true, null));

        Assert.True(Delta(deltas, Registers.Names.Boredom) > 0);
        Assert.True(Delta(deltas, Registers.Names.Anger) > 0);
    }

    [Fact]
    public void Shouting_raises_anger_and_a_wall_of_text_raises_boredom()
    {
        IReadOnlyList<AffectDelta> deltas = ChatAffect.Deltas(
            Root,
            new AffectSignals(null, TextStyle.Caps | TextStyle.WallOfText, RepeatPing: false, null));

        Assert.True(Delta(deltas, Registers.Names.Anger) > 0);
        Assert.True(Delta(deltas, Registers.Names.Boredom) > 0);
    }

    [Fact]
    public void An_ordinary_message_moves_nothing()
    {
        Assert.Empty(ChatAffect.Deltas(
            Root, new AffectSignals("GREET", TextStyle.None, RepeatPing: false, null)));
    }

    [Fact]
    public void Every_delta_names_a_register_the_persona_declares()
    {
        // An undeclared register has no baseline and no decay, so anything pushed into it would
        // stay there forever — the filter is what keeps that impossible.
        IReadOnlyList<AffectDelta> deltas = ChatAffect.Deltas(
            Root,
            new AffectSignals(
                ChatAffect.Intents.Apology,
                TextStyle.Caps | TextStyle.WallOfText,
                RepeatPing: true,
                null));

        Assert.NotEmpty(deltas);
        Assert.All(deltas, d => Assert.True(
            Root.Personality.Baselines.ContainsKey(d.Register),
            $"{d.Register} is not declared in personality.baselines"));
    }

    [Fact]
    public void Deltas_are_dropped_when_the_persona_does_not_declare_the_register()
    {
        PersonaRoot stripped = Root with
        {
            Personality = new PersonalityDef(
                System.Collections.Frozen.FrozenDictionary<string, double>.Empty,
                Root.Personality.Decay),
        };

        Assert.Empty(ChatAffect.Deltas(
            stripped,
            new AffectSignals(ChatAffect.Intents.Apology, TextStyle.Caps, RepeatPing: true, null)));
    }

    [Fact]
    public void Being_thanked_earns_a_reaction_and_nothing_else_does()
    {
        Assert.Equal(ChatAffect.ThanksReaction, ChatAffect.Reaction(ChatAffect.Intents.Thanks));
        Assert.Null(ChatAffect.Reaction(ChatAffect.Intents.Apology));
        Assert.Null(ChatAffect.Reaction(null));
    }

    private static IReadOnlyList<AffectDelta> Apology(long? turnsSince) => ChatAffect.Deltas(
        Root,
        new AffectSignals(ChatAffect.Intents.Apology, TextStyle.None, RepeatPing: false, turnsSince));

    private static double Delta(IReadOnlyList<AffectDelta> deltas, string register)
        => deltas.Where(d => d.Register == register).Sum(d => d.Delta);
}
