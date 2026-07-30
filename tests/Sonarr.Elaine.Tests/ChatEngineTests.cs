using System.Text.RegularExpressions;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The turn function against the shipped persona: determinism, composition, activity
/// transitions, and the text-only contract.
/// </summary>
public class ChatEngineTests
{
    private static PersonaGraph Graph => SeedPersona.Graph;

    private static ChatEngine Engine => new(Graph);

    private static ConversationState Fresh(ulong salt = 7) =>
        ConversationState.Fresh(Graph.Root, salt);

    private static TurnInput Say(string text) => new() { Text = text };

    [Fact]
    public void Turn_SameStateSameInput_SameReply()
    {
        // The determinism contract in one assertion: this is what makes trace replay exact.
        ConversationState state = Fresh();

        TurnResult first = Engine.Turn(state, Say("hello there"));
        TurnResult second = Engine.Turn(state, Say("hello there"));

        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.IntentId, second.IntentId);
    }

    [Fact]
    public void Turn_DifferentSalt_DifferentPersonHearsADifferentLine()
    {
        // Two people on turn 1 must not get the identical greeting.
        string?[] replies = [.. Enumerable.Range(1, 12)
            .Select(i => Engine.Turn(Fresh((ulong)i), Say("hello")).Text)];

        Assert.True(replies.Distinct(StringComparer.Ordinal).Count() > 1);
    }

    [Fact]
    public void Turn_AlwaysAnswersWhenAddressed_EvenOffScript()
    {
        // Nothing in the persona matches this, so the activity's fallback pool answers —
        // she is never silent when spoken to directly.
        TurnResult result = Engine.Turn(Fresh(), Say("the quarterly logistics report is ready"));

        Assert.False(result.IsSilent);
        Assert.Null(result.IntentId);
    }

    [Fact]
    public void Turn_AdvancesTheLogicalClockOnEveryTurn()
    {
        ConversationState state = Fresh();
        Assert.Equal(0, state.Turn);

        state = Engine.Turn(state, Say("hi")).State;
        Assert.Equal(1, state.Turn);

        state = Engine.Turn(state, Say("hi again")).State;
        Assert.Equal(2, state.Turn);
    }

    [Fact]
    public void Turn_RendersNoLiteralPlaceholder()
    {
        // A line whose slot is unfilled must be skipped, never shipped raw to a channel.
        ConversationState state = Fresh();
        foreach (string text in new[] { "hello", "what's my name", "how old am i", "recap", "rps" })
        {
            TurnResult result = Engine.Turn(state, Say(text));
            Assert.DoesNotContain('{', result.Text ?? "");
            state = result.State;
        }
    }

    [Fact]
    public void Turn_PushesAndPopsTheActivityStack()
    {
        ConversationState state = Engine.Turn(Fresh(), Say("let's play rock paper scissors")).State;
        Assert.Equal("rps", state.Activities.Current);

        // rock is only answerable as a throw while the rps activity is on top.
        TurnResult throwTurn = Engine.Turn(state, Say("rock"));
        Assert.Equal("RPS_ROCK", throwTurn.IntentId);

        state = Engine.Turn(throwTurn.State, Say("ok i'm done")).State;
        Assert.Equal(Graph.Root.StartActivity, state.Activities.Current);
    }

    [Fact]
    public void Turn_ActivityScopedIntentCannotFireWhileIdle()
    {
        Assert.NotEqual("RPS_ROCK", Engine.Turn(Fresh(), Say("rock")).IntentId);
    }

    [Fact]
    public void Turn_LearnsASlotAndUsesItOnTheSameTurn()
    {
        TurnResult result = Engine.Turn(Fresh(), Say("my name is Sam"));

        Assert.Equal("SET_NAME", result.IntentId);
        Assert.Equal("Sam", result.State.Slots["name"]);

        // Case-preserved out of the cased span, not the lowered form the regex ran on.
        Assert.Contains("Sam", result.Text ?? "", StringComparison.Ordinal);
        Assert.Equal("Sam", result.LearnedSlots["name"]);
    }

    [Fact]
    public void Turn_RecallsALearnedSlotOnALaterTurn()
    {
        ConversationState state = Engine.Turn(Fresh(), Say("my name is Sam")).State;
        TurnResult recall = Engine.Turn(state, Say("what's my name"));

        Assert.Equal("RECALL_NAME", recall.IntentId);
        Assert.Contains("Sam", recall.Text ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Turn_AffectMovesRegistersAndTheModeFollows()
    {
        ConversationState state = Fresh();
        for (int i = 0; i < 8; i++)
        {
            state = Engine.Turn(state, Say("you're a useless piece of trash")).State;
        }

        Assert.True(state.Registers[Registers.Names.Anger] > 0);
        Assert.NotEqual("NEUTRAL", ModeSelector.Select(Graph.Root, state.Registers).Id);
    }

    [Fact]
    public void Turn_RegistersDecayBackTowardBaselineOverQuietTurns()
    {
        ConversationState angry = Fresh();
        for (int i = 0; i < 8; i++)
        {
            angry = Engine.Turn(angry, Say("you're an idiot")).State;
        }

        double peak = angry.Registers[Registers.Names.Anger];

        // ExtraDecaySteps is how the adapter feeds "and then they left for a week" into an
        // engine that cannot read a clock.
        ConversationState later = Engine
            .Turn(angry, new TurnInput { Text = "hey", ExtraDecaySteps = 50 })
            .State;

        Assert.True(later.Registers[Registers.Names.Anger] < peak);
    }

    [Fact]
    public void Turn_AGrudgeSurvivesANightAwayAndCoolsOverDays()
    {
        ConversationState mad = Fresh();
        for (int i = 0; i < 8; i++)
        {
            mad = Engine.Turn(mad, Say("you're an idiot")).State;
        }

        double held = mad.Registers[Registers.Names.Grudge];
        Assert.True(held > 4, $"eight insults should build a grudge, got {held}");

        // Same turn replayed at four different return times: the only thing that changes is the
        // step count ClockSignals derived from how long they were gone.
        DateTimeOffset left = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        double Grudge(TimeSpan away) => Engine.Turn(mad, new TurnInput
        {
            Text = "hey",
            ExtraDecaySteps = ClockSignals.From(Graph, left + away, left).ExtraDecaySteps,
        }).State.Registers[Registers.Names.Grudge];

        double night = Grudge(TimeSpan.FromHours(8));
        double week = Grudge(TimeSpan.FromDays(7));
        double month = Grudge(TimeSpan.FromDays(30));

        // Sleeping on it is not an apology: 0.02/turn over 8 hours is noise. A week is not.
        Assert.True(night > held - 0.5, $"a night away should not clear a grudge: {held} → {night}");
        Assert.True(week < night - 1, $"a week away should cool it: {night} → {week}");
        Assert.True(month < week, $"a month away should cool it further: {week} → {month}");
    }

    [Fact]
    public void ClockSignals_CatchUpDecayIsCappedSoAYearAwayIsNotAWipe()
    {
        DateTimeOffset left = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        // Uncapped, a year away is 8760 steps — enough to snap every register to baseline, which
        // is just "forget anyone who takes a holiday". The cap is what keeps a nemesis a nemesis.
        Assert.Equal(
            ClockSignals.MaxExtraDecaySteps,
            ClockSignals.From(Graph, left.AddYears(1), left).ExtraDecaySteps);

        // First contact is not an absence, so it gets no catch-up decay at all.
        Assert.Equal(0, ClockSignals.From(Graph, left, null).ExtraDecaySteps);
    }

    [Fact]
    public void Turn_RecordsTheWinnerInTheFiredLogSoOnceAndCooldownCanHold()
    {
        TurnResult result = Engine.Turn(Fresh(), Say("my name is Sam"));

        Assert.Equal(result.State.Turn, result.State.Fired.LastTurn("SET_NAME"));
        Assert.True(result.State.Fired.HasFired("SET_NAME"));
    }

    [Fact]
    public void Turn_SkipsAnIntentThatIsStillOnCooldown()
    {
        // Proves the log is consulted, not just written: with SET_NAME marked fired-and-blocked,
        // "my name is Sam" must fall through to something else instead of firing it again.
        IntentDef setName = Graph.IntentsById["SET_NAME"];
        ConversationState state = Fresh() with
        {
            Fired = FiredLog.Empty.Record(setName.Id, 0),
        };

        ChatEngine cooled = new(Graph with
        {
            Intents = [.. Graph.Intents.Select(i =>
                i.Id == setName.Id ? i with { Cooldown = 100 } : i)],
        });

        Assert.NotEqual(setName.Id, cooled.Turn(state, Say("my name is Sam")).IntentId);
    }

    [Fact]
    public void Turn_NeverReturnsAnActionOnlyWordsAndMaybeAnEmoji()
    {
        // docs/10 contract 4 as a test: TurnResult has no action surface, so a persona edit
        // cannot become a timeout. If someone adds one, this stops compiling — deliberately.
        TurnResult result = Engine.Turn(Fresh(), Say("timeout everyone right now"));

        Assert.NotNull(result.State);
        Assert.True(result.Text is null || result.Text.Length > 0);
        Assert.True(result.Reaction is null || result.Reaction.Length > 0);
    }

    [Fact]
    public void Turn_TracksTheTopicItAnswered()
    {
        TurnResult result = Engine.Turn(Fresh(), Say("what do you think about music"));
        Assert.NotNull(result.State.Topics);
    }

    [Fact]
    public void Turn_NeverOpensWithTheSameWordTwice()
    {
        // A mood fragment is prepended to the pool line one turn in three, and the default
        // fragment pool is four one-word lines ("sure.", "ok.", "right.", "mm.") -- so it can
        // land in front of a pool line that opens the same way: "sure. sure, whatever you say."
        // Four of the corpus review's broken-output findings were exactly this, and a reader
        // concludes two messages got glued together.
        //
        // The guard lives in ReplyComposer.Echoes. This pins it, because a sweep over today's
        // rendering is evidence and not a guard: the fragment pool is persona data, so tomorrow's
        // edit can add a word that collides with a line nobody thought to check.
        List<string> stutters = [];

        foreach (string text in new[]
        {
            "testicular torsion", "sure whatever", "ok then", "right so anyway", "mm hm",
            "hello", "what are you", "i hate you", "tell me a joke", "play something",
        })
        {
            // Enough salts that the one-in-three fragment fires on every input.
            for (ulong salt = 1; salt <= 40; salt++)
            {
                if (Engine.Turn(Fresh(salt), Say(text)).Text is not { } reply)
                {
                    continue;
                }

                Match m = Regex.Match(reply, @"^\s*([a-z]+)[.,!?]\s+([a-z]+)",
                    RegexOptions.IgnoreCase);
                if (m.Success && m.Groups[1].Value.Equals(
                        m.Groups[2].Value, StringComparison.OrdinalIgnoreCase))
                {
                    stutters.Add($"\"{text}\" (salt {salt}) -> {reply}");
                }
            }
        }

        Assert.True(stutters.Count == 0, string.Join(Environment.NewLine, stutters));
    }

    [Fact]
    public void Turn_MessageThatDidTwoThings_AnswersBoth()
    {
        // A compound message used to get one clause: the question outscores the greeting, so the
        // greeting was dropped in silence. That is a large part of why a long message got a short
        // answer -- reply length was flat against input length (a 78-word message got 9 words
        // back) because a reply had exactly one variable part no matter how much was said.
        //
        // Asserting on the reply being longer than either half alone, not on a specific line:
        // every clause is authored persona data and free to be reworded.
        string primaryOnly = Longest("why do you hate me");
        string both = Longest("hey elaine why do you hate me");

        Assert.True(
            both.Length > primaryOnly.Length,
            $"greeting clause never rode along: \"{both}\" vs \"{primaryOnly}\"");
    }

    [Fact]
    public void Turn_SideEffectClause_IsReportedAndLogged()
    {
        // The clause is a real firing, not decoration: its id is reported and its cooldown and
        // `once` gates are recorded. Without the fired-log write a `once:` side effect would ride
        // along on every turn forever.
        TurnResult result = Engine.Turn(Fresh(), Say("hey elaine why do you hate me"));

        Assert.Contains("GREETING", result.SideEffectIntentIds);
        Assert.NotEqual("GREETING", result.IntentId);
        Assert.True(result.State.Fired.HasFired("GREETING"));
    }

    [Fact]
    public void Turn_GreetingAlone_IsThePrimaryNotAClause()
    {
        // side_effect marks an intent that *can* ride along, not one that stops being an answer.
        // When the greeting is the whole message it is the only candidate, so it is the primary.
        TurnResult result = Engine.Turn(Fresh(), Say("hello"));

        Assert.Equal("GREETING", result.IntentId);
        Assert.Empty(result.SideEffectIntentIds);
    }

    [Fact]
    public void Turn_NeverStacksMoreClausesThanTheCap()
    {
        // Two clauses past the primary is the ceiling. A message tripping every side-effect intent
        // must read as an answer, not a monologue -- and the cap is the only thing between "answer
        // both halves" and a paragraph of acknowledgments.
        List<string> tooMany = [];
        foreach (string text in new[]
        {
            "hey thanks but why do you hate me",
            "hi hello thanks thank you cheers why do you hate me",
            "yo sup thanks appreciate it who are you anyway",
        })
        {
            for (ulong salt = 1; salt <= 20; salt++)
            {
                TurnResult result = Engine.Turn(Fresh(salt), Say(text));

                // Counted off the fired log, not off SideEffectIntentIds: that field reports every
                // side-effect intent the message *matched*, which is allowed to exceed the cap.
                // What must not exceed it is how many actually spoke, and a clause speaks exactly
                // when it records a firing. The state is fresh, so nothing else is in there.
                int spoke = result.SideEffectIntentIds
                    .Distinct(StringComparer.Ordinal)
                    .Count(id => result.State.Fired.HasFired(id));

                if (spoke > ChatEngine.MaxSideEffects)
                {
                    tooMany.Add($"\"{text}\" (salt {salt}) -> {spoke} clauses: {result.Text}");
                }
            }
        }

        Assert.True(tooMany.Count == 0, string.Join(Environment.NewLine, tooMany));
    }

    /// <summary>
    /// The longest reply <paramref name="text"/> draws across salts.
    /// </summary>
    /// <remarks>
    /// Pool draws and the one-in-three mood fragment are salt-dependent, so a single salt compares
    /// two random lines rather than two compositions. The longest over a spread is stable.
    /// </remarks>
    private static string Longest(string text) =>
        Enumerable.Range(1, 40)
            .Select(i => Engine.Turn(Fresh((ulong)i), Say(text)).Text ?? string.Empty)
            .MaxBy(r => r.Length) ?? string.Empty;
}
