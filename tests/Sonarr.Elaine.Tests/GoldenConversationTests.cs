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
    public void EarningHerTrustCrossesATierAndSheSaysSoExactlyOnce()
    {
        // The payoff for the trust register: tiers are derived, so the only way to see one is to
        // actually earn it. Nothing here injects a tier or a trust value.
        Script chat = new();
        Assert.Equal("stranger", Tier(chat.State));

        List<string> moments = [];
        for (int i = 0; i < 40 && Tier(chat.State) != "regular"; i++)
        {
            string? text = chat.Say(i % 2 == 0 ? "good bot" : "thanks, you're the best").Text;
            if (text is not null && MomentLine(text) is { } moment)
            {
                moments.Add(moment);
            }
        }

        Assert.Equal("regular", Tier(chat.State));

        // Both crossings on the way up announced themselves, in order, once each.
        Assert.Equal(2, moments.Count);
        Assert.Contains(chat.State.Fired.Entries.Keys, k => k == ChatEngine.TierFiredKey("acquaintance"));
        Assert.Contains(chat.State.Fired.Entries.Keys, k => k == ChatEngine.TierFiredKey("regular"));

        // Trust decays back under the threshold and climbs again: a tier already announced
        // stays quiet, because the fired-log is persisted rather than per-message.
        chat.Say("hey", decaySteps: 400);
        Assert.Equal("stranger", Tier(chat.State));
        for (int i = 0; i < 40 && Tier(chat.State) != "regular"; i++)
        {
            string? text = chat.Say("good bot").Text;
            Assert.Null(text is null ? null : MomentLine(text));
        }

        Assert.Equal("regular", Tier(chat.State));
    }

    [Fact]
    public void SheNamesSomebodyWhoNeverGaveAName_OnceAndForAll()
    {
        // v1's bug: the nickname was redrawn wherever it was needed, so one person was
        // "trouble", then "rando", then "nobody", inside a single conversation.
        Script chat = new();
        Assert.Null(chat.State.AssignedNickname);

        for (int i = 0; i < 40 && chat.State.AssignedNickname is null; i++)
        {
            chat.Say("good bot");
        }

        string nickname = Assert.IsType<string>(chat.State.AssignedNickname);
        Assert.Contains(nickname, Graph.Pools[ChatEngine.NicknamePool].Lines);

        // It survives every later turn, including ones that cross more tiers.
        for (int i = 0; i < 40; i++)
        {
            chat.Say(i % 3 == 0 ? "hey" : "you're the best");
            Assert.Equal(nickname, chat.State.AssignedNickname);
        }

        Assert.Equal(nickname, chat.State.RenderSlots["nickname"]);
    }

    [Fact]
    public void AStoredNameBeatsANickname_SoSheNeverInventsOneForYou()
    {
        Script chat = new();
        chat.Say("my name is Hau");

        for (int i = 0; i < 40; i++)
        {
            chat.Say("good bot");
            Assert.Null(chat.State.AssignedNickname);
        }

        Assert.Equal("inner_circle", Tier(chat.State));
    }

    private static string Tier(ConversationState state) =>
        ModeSelector.SelectTier(Graph.Root, state.Registers[Registers.Names.Trust])!.Id;

    /// <summary>The authored tier line inside a composed reply, or null if none is present.</summary>
    /// <remarks>
    /// Matched on the literal text up to the first placeholder, since a moment line may carry
    /// <c>{nickname}</c> and the reply holds it substituted.
    /// </remarks>
    private static string? MomentLine(string text) =>
        Graph.Root.Tiers
            // stranger has no moment pool on purpose: nobody arrives there, they start there.
            .Where(t => Graph.Pools.ContainsKey($"{ChatEngine.TierMomentPoolPrefix}{t.Id}"))
            .SelectMany(t => Graph.Pools[$"{ChatEngine.TierMomentPoolPrefix}{t.Id}"].Lines)
            .Select(line => line.Split('{')[0].TrimEnd())
            .Where(prefix => prefix.Length > 8)
            .FirstOrDefault(prefix => text.Contains(prefix, StringComparison.Ordinal));

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
