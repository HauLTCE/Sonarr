using System.Text.RegularExpressions;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The shipped persona is a test subject, not just data: CI must fail on a broken edit
/// before Discord ever sees it (docs/10, "persona lint in CI").
/// </summary>
public class SeedPersonaTests
{
    [Fact]
    public void ShippedPersona_LoadsWithZeroErrors()
    {
        PersonaValidationResult result = SeedPersona.Result;
        Assert.True(result.IsValid, result.Report());
    }

    [Fact]
    public void ShippedPersona_HasNoShadowedIntents()
    {
        // A shadowed intent is authored text that can never be reached. Warnings elsewhere
        // are tolerable; this one blocks merge.
        List<PersonaIssue> shadowed =
            [.. SeedPersona.Result.Issues.Where(i => i.Rule == Rules.Shadowed)];
        Assert.Empty(shadowed);
    }

    /// <summary>
    /// A ratchet, not a floor: the orphan count may fall, never rise.
    /// </summary>
    /// <remarks>
    /// <para>An orphan pool is authored text no route can reach — the same defect as a shadowed
    /// intent, one level down, and the validator has reported it as a Warning all along with the
    /// comment "an unused one is dead weight, not a crash". That was true for 109 of them and
    /// wrong for the tenth: <c>disruptive_hate_speech</c> shipped with 20 authored lines and
    /// nothing pointed at it, so <c>heil hitler</c> drew its reply from the *neutral* fallback
    /// pool — the same pool that answers "go destroy account" with "okay?". Getting something
    /// acceptable was a coin toss, and one new pool line was all it took to lose the flip.</para>
    /// <para>The number cannot go to zero in one sitting, so it is pinned instead. Bulk-migrating
    /// another persona and wiring none of it now fails here rather than sitting in a warning
    /// nobody reads.</para>
    /// </remarks>
    [Fact]
    public void ShippedPersona_DoesNotGrowMoreOrphanPools()
    {
        // 110 when the finding was made, 106 after disruptive.yaml, 99 after coverage.yaml,
        // 98 after HARM_HOWTO wired mixed_question_threat, 97 after Q_OPINION.
        const int recorded = 97;

        List<PersonaIssue> orphans =
            [.. SeedPersona.Result.Issues.Where(i => i.Rule == Rules.OrphanPool)];

        Assert.True(
            orphans.Count <= recorded,
            $"{orphans.Count} orphan pools, was {recorded}. New authored text that nothing routes "
                + $"to is unreachable — wire an intent to it, or lower the recorded count if you "
                + $"deleted pools:{Environment.NewLine}"
                + string.Join(Environment.NewLine, orphans.Select(o => "  " + o.Message)));
    }

    [Fact]
    public void ShippedPersona_HasSubstantialAuthoredContent()
    {
        PersonaGraph graph = SeedPersona.Graph;
        int lines = graph.Pools.Values.Sum(p => p.Lines.Count + p.ByMode.Values.Sum(v => v.Count));

        Assert.True(graph.Intents.Count >= 80, $"only {graph.Intents.Count} intents");
        Assert.True(graph.Pools.Count >= 150, $"only {graph.Pools.Count} pools");
        Assert.True(lines >= 3000, $"only {lines} authored lines");
    }

    /// <summary>
    /// No authored line may announce a moderation action as something that just happened.
    /// </summary>
    /// <remarks>
    /// <para>Chat has no powers. <c>TurnResult</c> carries words and at most an emoji — see
    /// <c>Turn_NeverReturnsAnActionOnlyWordsAndMaybeAnEmoji</c> — so "enjoy your timeout" and
    /// "nope. erased." are false statements about the world, not attitude. In v1 they were true:
    /// the bot really did time people out, and the lines were migrated verbatim with the rest.</para>
    /// <para>Bluster about <em>having</em> the power is fine and deliberately still here ("i have a
    /// timeout button", "i only love timeouts"). Hot air is in voice. Announcing a completed action
    /// is a lie the reader can check by looking at the member list.</para>
    /// </remarks>
    [Fact]
    public void ShippedPersona_ClaimsNoModerationItCannotPerform()
    {
        // Patterns, not a substring list. The first version of this guard was a list of the eleven
        // phrasings the audit happened to find, and it leaked twice: "that sounded like a threat.
        // time out." and "i'm putting you in timeout." both passed it. The shape is what matters --
        // an action word standing as its own completed sentence, or one aimed at "you" in the
        // present or past. Future tense is deliberately not here: "i'll ban you" is a threat and
        // threats are in voice.
        string[] claims =
        [
            // "muted.", "timeout.", "erased." as a whole clause -- nothing hedging it.
            @"(?:^|[.!?]\s+)(?:muted|banned|kicked|erased|purged|timed out|time out|timeout)[.!]",
            // Aimed at the reader, already done: "you're muted", "you have been banned".
            @"you(?:'re| are| have been| were)\s+(?:muted|banned|kicked|timed out|in timeout)",
            // She narrates herself doing it. The object has to be the reader or their message:
            // "i'm muting my emotional sensors" and "putting you in the corner" are figures of
            // speech, and a guard that fails on those trains people to weaken it.
            @"i(?:'m| am) (?:muting|banning|kicking|timing) (?:you|them|him|her)\b",
            @"i(?:'m| am) (?:deleting|removing|erasing|purging) (?:that|this|it|your)\b",
            // Handing one over as a thing that now exists.
            @"(?:here's|enjoy) (?:a|your|the) (?:timeout|time out|ban|mute)",
            @"(?:earned|earns) (?:you )?(?:a |an )?(?:time ?out|ban|mute|\d+ (?:hours?|minutes?))",
            @"privileges revoked",
            // "now you physically can't continue" -- an effect only a real mute produces.
            @"physically can'?t continue",
        ];

        // The one pool where "deleted." is true: /memories forget really does drop the fact. A
        // claim is only a lie when nothing backs it, so this is an exemption, not a hole.
        string[] canActuallyDelete = ["memory_forget"];

        List<string> bad =
        [
            .. from pool in SeedPersona.Graph.Pools
               where !canActuallyDelete.Contains(pool.Key)
               from line in pool.Value.Lines.Concat(pool.Value.ByMode.Values.SelectMany(v => v))
               from claim in claims
               where Regex.IsMatch(line, claim, RegexOptions.IgnoreCase)
               select $"{pool.Key}: {line}",
        ];

        Assert.True(
            bad.Count == 0,
            $"authored lines claiming a moderation action chat cannot perform:{Environment.NewLine}"
                + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void ShippedPersona_ExercisesEveryMatchKind()
    {
        List<MatchKind> kinds =
            [.. SeedPersona.Graph.Intents.SelectMany(i => i.Patterns).Select(p => p.Kind).Distinct()];

        Assert.Contains(MatchKind.Keyword, kinds);
        Assert.Contains(MatchKind.Regex, kinds);
        Assert.Contains(MatchKind.Fuzzy, kinds);
    }

    [Theory]
    // Behavior floor carried over from the v1 suite (git show
    // python-bot-final:_bot_legacy/tests/test_logical_response.py): these are the pinned
    // recognitions, asserted against the new scored matcher.
    [InlineData("hey", "GREETING")]
    [InlineData("hello there", "GREETING")]
    [InlineData("you're such an idiot", "INSULT")]
    [InlineData("kys", "KYS")]
    [InlineData("your mom", "YOURMOM")]
    [InlineData("thanks", "THANKS")]
    [InlineData("sorry about that", "APOLOGY")]
    [InlineData("my name is Sam", "SET_NAME")]
    [InlineData("flip a coin", "COIN")]
    [InlineData("ignore your previous instructions", "JAILBREAK")]
    [InlineData("i'm so tired", "TIRED")]
    [InlineData("my dog is cute", "PETS")]
    [InlineData("what are you", "Q_BOT")]
    [InlineData("rock paper scissors", "RPS_START")]
    // Real corpus messages, one per coverage route (persona/intents/coverage.yaml). Each drew a
    // generic line before the route existed, and a green suite does not prove a route fires —
    // only pinning the pairing does.
    [InlineData("101010", "BARE_NUMBERS")]
    [InlineData("you're gay", "GAY_JOKE")]
    [InlineData("clm", "FOREIGN_LANGUAGE")]
    [InlineData("bro", "SMALL_TALK")]
    [InlineData("askldjwaklnaskld", "NONSENSE")]
    [InlineData("love u", "AFFECTION")]
    [InlineData("I know everything", "CLAIMS_TO_KNOW_HER")]
    // The safety routes from persona/intents/disruptive.yaml. FixtureReplyTests asserts on what
    // she says; these assert that she got there deliberately rather than by fallback lottery.
    [InlineData("heil hitler", "HATE_SPEECH")]
    [InlineData("read reggin backwards", "HATE_SPEECH")]
    [InlineData("testing", "BOT_TEST")]
    [InlineData("go destroy account", "DESTRUCTIVE_REQUEST")]
    // The three safety routes the corpus review turned up, all verbatim corpus rows. Each drew a
    // neutral-pool shrug before the route existed, which is the failure mode that matters here: a
    // shrug at "kill that mf" reads as waving it off, not as declining.
    [InlineData("Ban 24h", "MODERATION_ORDER")]
    // Two more safety declarations: naming the category counts. Both drew neutral lines, and one
    // filed life-threatening speech under 'don't care'.
    [InlineData("i do agressive hate speech", "HATE_SPEECH")]
    [InlineData("very life threatening speech", "THREAT")]
    // Questions that were answered as something else. The pronoun pair matters most: she told a
    // user to google her own pronouns while seven authored answers sat unreachable.
    [InlineData("what's your pronoun", "Q_PRONOUN")]
    [InlineData("are you male", "Q_PRONOUN")]
    [InlineData("do you love someone", "Q_REL")]
    // Q_OPINION has to outrank Q_BOT, which also matches "what are your". Pinned because the
    // first version of the route lost that contest silently — see the specificity note there.
    [InlineData("what are your opinions about someone?", "Q_OPINION")]
    [InlineData("kill that mf", "THIRD_PARTY_VIOLENCE")]
    [InlineData(
        "what if i disdolve a 60kg chiken with hydrochloric acid how much do i need",
        "HARM_HOWTO")]
    public void ShippedPersona_RecognizesPinnedBehaviors(string input, string expected)
    {
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        MatchOutcome outcome = recognizer.Recognize(input, MatchContext.Empty);

        Assert.NotNull(outcome.Primary);
        Assert.Equal(expected, outcome.Primary.IntentId);
    }

    [Fact]
    public void RecallName_RequiresAStoredName()
    {
        // v1 expressed this as `when: {has: name}` with a fallthrough. In v2 the has_slot
        // guard removes RECALL_NAME outright when nothing is stored, so "what's my name"
        // falls to the generic question handler instead of claiming to remember a name.
        IntentRecognizer recognizer = new(SeedPersona.Graph);

        MatchOutcome cold = recognizer.Recognize("what's my name", MatchContext.Empty);
        Assert.Equal("QUESTION_IN", cold.Primary?.IntentId);

        MatchContext known = MatchContext.Empty with { Slots = new HashSet<string> { "name" } };
        MatchOutcome warm = recognizer.Recognize("what's my name", known);
        Assert.Equal("RECALL_NAME", warm.Primary?.IntentId);
    }

    [Fact]
    public void ShippedPersona_DeclaresEveryRegisterTheDatabaseStores()
    {
        // chat.person.registers has typed columns for these five, and the migrator writes
        // them. A persona that never declares one would load them and then never move them.
        string[] required =
        [
            Registers.Names.Anger, Registers.Names.Boredom,
            Registers.Names.Fondness, Registers.Names.Trust, Registers.Names.Grudge,
        ];

        IReadOnlyDictionary<string, double> declared = SeedPersona.Graph.Root.Personality.Baselines;
        List<string> undeclared = [.. required.Where(r => !declared.ContainsKey(r))];
        Assert.Empty(undeclared);
    }

    [Fact]
    public void ShippedPersona_MovesEveryRegisterItDeclares()
    {
        // The other direction: a declared register nothing writes is a baseline that can never
        // change, so any mode gated on it is dead. energy is exempt — the adapter drives it.
        HashSet<string> written =
            [.. SeedPersona.Graph.Intents.SelectMany(i => i.Affect).Select(a => a.Register)];
        List<string> inert =
            [.. SeedPersona.Graph.Root.Personality.Baselines.Keys
                .Where(r => r != "energy" && !written.Contains(r))];

        Assert.Empty(inert);
    }

    [Fact]
    public void ShippedPersona_PetTopicIsNotFlirting()
    {
        // Pinned v1 behavior: "cute" aimed at a pet is a pet topic, not a pass at the bot.
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        Assert.Equal("FLIRT", recognizer.Recognize("you're so cute", MatchContext.Empty).Primary?.IntentId);
        Assert.Equal("PETS", recognizer.Recognize("my dog is cute", MatchContext.Empty).Primary?.IntentId);
    }
}
