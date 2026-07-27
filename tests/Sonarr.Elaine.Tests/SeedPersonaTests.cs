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

    [Fact]
    public void ShippedPersona_HasSubstantialAuthoredContent()
    {
        PersonaGraph graph = SeedPersona.Graph;
        int lines = graph.Pools.Values.Sum(p => p.Lines.Count + p.ByMode.Values.Sum(v => v.Count));

        Assert.True(graph.Intents.Count >= 80, $"only {graph.Intents.Count} intents");
        Assert.True(graph.Pools.Count >= 150, $"only {graph.Pools.Count} pools");
        Assert.True(lines >= 3000, $"only {lines} authored lines");
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
    // Behavior floor carried over from _bot_legacy/tests/test_logical_response.py: these
    // are the pinned recognitions, asserted against the new scored matcher.
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
