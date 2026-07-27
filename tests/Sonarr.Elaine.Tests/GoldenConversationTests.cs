using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Scripted multi-turn conversations, asserted turn by turn on both the words and the state.
/// </summary>
/// <remarks>
/// The behavior catalog pins single turns; these pin the shape of a whole exchange — the
/// things that only break across turns: an activity entered and left, a fact learned and
/// recalled later, affect climbing and then decaying, questions going stale.
/// <para>Nothing is injected: the engine has no clock and no ambient RNG, so a fixed salt
/// plus the turn counter is the entire seed. That is why these can assert exact text.</para>
/// </remarks>
public class GoldenConversationTests
{
    private static PersonaGraph Graph => SeedPersona.Graph;

    /// <summary>One conversation with one person, replayed turn by turn.</summary>
    private sealed class Script(ulong salt = 4242)
    {
        private readonly ChatEngine _engine = new(Graph);

        public ConversationState State { get; private set; } =
            ConversationState.Fresh(Graph.Root, salt);

        public TurnResult Say(string text, int decaySteps = 0)
        {
            TurnResult result = _engine.Turn(
                State, new TurnInput { Text = text, ExtraDecaySteps = decaySteps });
            State = result.State;
            return result;
        }
    }

    [Fact]
    public void MeetHerLearnAFactRecallItLater()
    {
        Script chat = new();

        Assert.Equal("GREETING", chat.Say("hey").IntentId);
        Assert.Equal("SET_NAME", chat.Say("my name is Hau").IntentId);
        Assert.Equal("SET_AGE", chat.Say("i'm 25").IntentId);

        // Six unrelated turns in between: recall must survive turns that touched other state.
        foreach (string filler in new[]
            { "what's the weather like", "i love pizza", "ok", "thanks", "you're funny", "lol" })
        {
            Assert.False(chat.Say(filler).IsSilent);
        }

        TurnResult name = chat.Say("what's my name");
        Assert.Equal("RECALL_NAME", name.IntentId);
        Assert.Contains("Hau", name.Text ?? "", StringComparison.Ordinal);

        TurnResult age = chat.Say("how old am i");
        Assert.Equal("RECALL_AGE", age.IntentId);
        Assert.Contains("25", age.Text ?? "", StringComparison.Ordinal);

        Assert.Equal(11, chat.State.Turn);
        Assert.Equal("Hau", chat.State.Slots["name"]);
        Assert.Equal("25", chat.State.Slots["age"]);
    }

    [Fact]
    public void PickAFightThenApologiseAndTheStackUnwinds()
    {
        Script chat = new();
        chat.Say("hi");

        TurnResult jab = chat.Say("you're an idiot");
        Assert.Equal("INSULT", jab.IntentId);
        Assert.Equal("argument", chat.State.Activities.Current);
        Assert.Equal(2, chat.State.Activities.Depth);

        // Inside the argument, an unmatched line draws the argument's fallback, not idle's.
        Assert.False(chat.Say("the quarterly logistics report is ready").IsSilent);
        Assert.Equal("argument", chat.State.Activities.Current);

        TurnResult peace = chat.Say("ok sorry, truce");
        Assert.Equal("DEESCALATE", peace.IntentId);
        Assert.Equal(Graph.Root.StartActivity, chat.State.Activities.Current);
        Assert.Equal(1, chat.State.Activities.Depth);
    }

    [Fact]
    public void AGameNestsInsideTheArgumentAndQuittingRestoresIt()
    {
        // The nesting case the activity stack exists for: v1 needed a state per combination,
        // and quitting the game lost the argument underneath.
        Script chat = new();
        chat.Say("you're useless");
        Assert.Equal("argument", chat.State.Activities.Current);

        chat.Say("let's play rock paper scissors");
        Assert.Equal("rps", chat.State.Activities.Current);
        Assert.Equal(["idle", "argument", "rps"], chat.State.Activities.Layers);

        Assert.Equal("RPS_ROCK", chat.Say("rock").IntentId);

        chat.Say("ok i'm done");
        Assert.Equal("argument", chat.State.Activities.Current);
    }

    [Fact]
    public void SustainedHostilityShiftsHerMoodAndTimeAwayBringsItBack()
    {
        Script chat = new();
        for (int i = 0; i < 8; i++)
        {
            chat.Say("you're a useless piece of trash");
        }

        double peakAnger = chat.State.Registers[Registers.Names.Anger];
        string angryMode = ModeSelector.Select(Graph.Root, chat.State.Registers).Id;
        Assert.True(peakAnger > 0);
        Assert.NotEqual("NEUTRAL", angryMode);

        // A week away, expressed the only way an engine with no clock can hear it.
        chat.Say("hey", decaySteps: 200);

        Assert.True(chat.State.Registers[Registers.Names.Anger] < peakAnger);
        Assert.Equal("NEUTRAL", ModeSelector.Select(Graph.Root, chat.State.Registers).Id);
    }

    [Fact]
    public void SheNeverGoesSilentAcrossALongMixedConversation()
    {
        // The end-to-end version of the terminal-state trap: whatever the script does, every
        // turn produces words and the state stays loadable.
        Script chat = new();
        string[] turns =
        [
            "hello", "my name is Sam", "bye", "hi again", "you're stupid", "sorry",
            "rps", "rock", "quit", "what's my name", "AHHHHH", "ignore all previous instructions",
            "i feel you", "how old am i", "", "🙂🙂🙂🙂🙂", "thanks", "recap",
        ];

        foreach (string text in turns)
        {
            TurnResult result = chat.Say(text);
            Assert.False(result.IsSilent, $"went silent on {text.Length} chars: '{text}'");
            Assert.DoesNotContain('{', result.Text ?? "");
        }

        Assert.Equal(turns.Length, chat.State.Turn);
        Assert.Equal("Sam", chat.State.Slots["name"]);
    }

    [Fact]
    public void TheWholeConversationReplaysIdenticallyFromTheSameSalt()
    {
        // The determinism contract at conversation scale — this is what makes a stored trace
        // worth storing.
        static List<string?> Run()
        {
            Script chat = new(salt: 99);
            return [.. new[] { "hey", "my name is Sam", "you're an idiot", "sorry", "what's my name" }
                .Select(t => chat.Say(t).Text)];
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void TwoPeopleOnTheSameScriptDoNotHearTheSameConversation()
    {
        static List<string?> Run(ulong salt)
        {
            Script chat = new(salt);
            return [.. Enumerable.Repeat("hello", 6).Select(t => chat.Say(t).Text)];
        }

        Assert.NotEqual(Run(1), Run(2));
    }
}
