using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The point of the scored matcher: the most specific intent wins on merit, so authoring
/// order stops being load-bearing. Several tests below run the same persona twice with the
/// declaration order flipped and assert the same winner.
/// </summary>
public class LexicalMatcherTests
{
    private const string Root = """
        version: 2
        start_activity: idle
        activities:
          - id: idle
            intents: ["*"]
            fallback_pool: filler
        modes:
          - id: NEUTRAL
            always: true
        tiers:
          - id: stranger
            min_trust: 0
          - id: regular
            min_trust: 15
        slots: [name]
        """;

    private const string Pools = "pools:\n  filler: [whatever]\n";

    private static PersonaGraph Graph(string intentsYaml, string root = Root)
    {
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", root), ("pools/p.yaml", Pools), ("intents/i.yaml", intentsYaml)));
        Assert.True(result.IsValid, result.Report());
        return result.Graph!;
    }

    private static MatchOutcome Match(
        string intentsYaml, string input, MatchContext? context = null, string root = Root) =>
        new LexicalMatcher(Graph(intentsYaml, root))
            .Match(Normalizer.Normalize(input), context ?? MatchContext.Empty);

    // Same two intents, declared both ways round. "your mom" must win either way — this is
    // exactly the v1 bug where keyword [mom] declared first permanently ate the regex.
    private const string BroadFirst = """
        intents:
          - id: BROAD
            match: { keyword: [mom] }
            pool: filler
          - id: SPECIFIC
            match: { regex: ["your mom"] }
            pool: filler
        """;

    private const string SpecificFirst = """
        intents:
          - id: SPECIFIC
            match: { regex: ["your mom"] }
            pool: filler
          - id: BROAD
            match: { keyword: [mom] }
            pool: filler
        """;

    [Theory]
    [InlineData(BroadFirst)]
    [InlineData(SpecificFirst)]
    public void SpecificRegex_BeatsBroadKeyword_RegardlessOfDeclarationOrder(string yaml)
    {
        MatchOutcome outcome = Match(yaml, "your mom is calling");
        Assert.Equal("SPECIFIC", outcome.Primary?.IntentId);

        // Both still ranked — the loser is available as context, not discarded.
        Assert.Equal(2, outcome.Ranked.Count);
    }

    [Fact]
    public void EveryEligibleIntentIsEvaluated_NoEarlyExit()
    {
        MatchOutcome outcome = Match(BroadFirst, "your mom");
        Assert.Equal(["SPECIFIC", "BROAD"], outcome.Ranked.Select(c => c.IntentId));
    }

    [Fact]
    public void AllKeywords_GetsMoreSpecificWithEachWord()
    {
        const string Yaml = """
            intents:
              - id: BROAD
                match: { keyword: [pizza] }
                pool: filler
              - id: SPECIFIC
                match: { all_keywords: [pizza, pineapple, opinion] }
                pool: filler
            """;

        Assert.Equal("SPECIFIC", Match(Yaml, "my pineapple pizza opinion").Primary?.IntentId);
        Assert.Equal("BROAD", Match(Yaml, "just pizza").Primary?.IntentId);
    }

    [Fact]
    public void Fuzzy_IsTheLoosestMatchAndLosesToAnExactKeyword()
    {
        const string Yaml = """
            intents:
              - id: FUZZY
                match: { fuzzy: [pathetic], max_distance: 2 }
                pool: filler
              - id: EXACT
                match: { keyword: [pathetic] }
                pool: filler
            """;

        Assert.Equal("EXACT", Match(Yaml, "you are pathetic").Primary?.IntentId);
        Assert.Equal("FUZZY", Match(Yaml, "you are pathetc").Primary?.IntentId);
    }

    [Fact]
    public void ShortTokens_DoNotFuzzMatch()
    {
        // "no" is one edit from "go", "so", "do" — below min_length only exact hits count.
        const string Yaml = """
            intents:
              - id: FUZZY
                match: { fuzzy: [go], min_length: 4 }
                pool: filler
            """;

        Assert.Null(Match(Yaml, "no").Primary);
        Assert.NotNull(Match(Yaml, "go").Primary);
    }

    [Fact]
    public void CorroborationBreaksTiesBetweenEquallySpecificIntents()
    {
        const string Yaml = """
            intents:
              - id: ONE
                match: { keyword: [hello] }
                pool: filler
              - id: TWO
                match: { keyword: [hello], regex: ["hello there"] }
                pool: filler
            """;

        MatchOutcome outcome = Match(Yaml, "hello there");
        Assert.Equal("TWO", outcome.Primary?.IntentId);
        Assert.Equal(2, outcome.Primary!.MatchedPatternCount);
    }

    [Fact]
    public void TopicAffinity_DecidesBetweenComparablyBroadIntents()
    {
        // "it's overrated" is ambiguous on its own; whichever intent is about the topic
        // already on the table wins. Note this does not outrank a specific full-sentence
        // regex — affinity nudges ties, it does not override real pattern evidence.
        const string Yaml = """
            intents:
              - id: GENERIC
                match: { keyword: [overrated] }
                pool: filler
              - id: ON_TOPIC
                match: { keyword: [overrated] }
                topic: pizza
                pool: filler
            """;

        Assert.Equal("GENERIC", Match(Yaml, "it's overrated").Primary?.IntentId);
        Assert.Equal(
            "ON_TOPIC",
            Match(Yaml, "it's overrated", MatchContext.Empty with { Topic = "pizza" })
                .Primary?.IntentId);
    }

    [Fact]
    public void TopicAffinity_DoesNotOverrideStrongPatternEvidence()
    {
        const string Yaml = """
            intents:
              - id: GENERIC
                match: { regex: ["what do you think"] }
                pool: filler
              - id: ON_TOPIC
                match: { keyword: [think] }
                topic: pizza
                pool: filler
            """;

        Assert.Equal(
            "GENERIC",
            Match(Yaml, "what do you think", MatchContext.Empty with { Topic = "pizza" })
                .Primary?.IntentId);
    }

    private const string TierGated = """
        intents:
          - id: GATED
            match: { keyword: [secret] }
            pool: filler
            guards:
              - kind: min_tier
                value: regular
                bonus: 0.5
        """;

    [Fact]
    public void FailedGuard_RemovesTheIntentEntirely()
    {
        Assert.Null(Match(TierGated, "tell me the secret").Primary);
        Assert.NotNull(Match(
            TierGated, "tell me the secret", MatchContext.Empty with { TierId = "regular" })
            .Primary);
    }

    [Fact]
    public void MinTierIsAFloorNotAnEquality()
    {
        // The bug this pins: comparing tier ids for equality made `min_tier: regular` exclude
        // everyone she likes MORE than a regular, so a tier-up silently took lines away.
        const string FourTiers = """
            version: 2
            start_activity: idle
            activities:
              - id: idle
                intents: ["*"]
                fallback_pool: filler
            modes:
              - id: NEUTRAL
                always: true
            tiers:
              - id: nemesis
                min_trust: -10
              - id: stranger
                min_trust: 0
              - id: regular
                min_trust: 15
              - id: favorite
                min_trust: 30
            slots: [name]
            """;

        MatchCandidate? For(string tier) => Match(
            TierGated, "tell me the secret",
            MatchContext.Empty with { TierId = tier }, FourTiers).Primary;

        Assert.NotNull(For("regular"));
        Assert.NotNull(For("favorite"));
        Assert.Null(For("stranger"));
        Assert.Null(For("nemesis"));
    }

    [Fact]
    public void MaxTierIsACeilingSoStrangerOnlyLinesStopAtTheThreshold()
    {
        const string StrangerOnly = """
            intents:
              - id: SMALL_TALK
                match: { keyword: [secret] }
                pool: filler
                guards:
                  - kind: max_tier
                    value: stranger
                    bonus: 0.5
            """;

        Assert.NotNull(Match(
            StrangerOnly, "tell me the secret",
            MatchContext.Empty with { TierId = "stranger" }).Primary);
        Assert.Null(Match(
            StrangerOnly, "tell me the secret",
            MatchContext.Empty with { TierId = "regular" }).Primary);
    }

    [Fact]
    public void ATierGuardFailsClosedForSomeoneWithNoTier()
    {
        // A person the adapter could not place must not inherit the top tier's lines.
        Assert.Null(Match(
            TierGated, "tell me the secret", MatchContext.Empty with { TierId = null }).Primary);
    }

    [Fact]
    public void ActivityAllowList_LimitsEligibility()
    {
        // Inside an activity only its listed intents are eligible, so an RPS round cannot be
        // derailed by an unrelated topic intent that happens to share a word.
        const string RpsRoot = """
            version: 2
            start_activity: idle
            activities:
              - id: idle
                intents: ["*"]
                fallback_pool: filler
              - id: rps
                intents: [INSIDER]
                fallback_pool: filler
            modes:
              - id: NEUTRAL
                always: true
            """;

        const string Yaml = """
            intents:
              - id: INSIDER
                match: { keyword: [rock] }
                pool: filler
              - id: OUTSIDER
                match: { keyword: [rock] }
                pool: filler
            """;

        Assert.Equal(2, Match(Yaml, "rock", root: RpsRoot).Ranked.Count);
        Assert.Equal(
            ["INSIDER"],
            Match(Yaml, "rock", new MatchContext("rps"), RpsRoot).Ranked.Select(c => c.IntentId));
    }

    [Fact]
    public void RegexCaptures_ArePreservedInOriginalCasing()
    {
        const string Yaml = """
            intents:
              - id: SET_NAME
                match: { regex: ["my name is (?<nm>[a-z]+)"] }
                pool: filler
                template: "{$nm}. noted."
            """;

        MatchCandidate? primary = Match(Yaml, "my name is McKayla").Primary;
        Assert.NotNull(primary);
        Assert.Equal("McKayla", primary.Captures["nm"]);
        Assert.Equal("my name is McKayla", primary.Captures["0"]);
    }

    [Fact]
    public void EmptyInput_MatchesNothing()
    {
        Assert.Same(MatchOutcome.None, Match(BroadFirst, "   "));
    }

    [Fact]
    public void SideEffectIntents_AreCollectedBesideThePrimary()
    {
        // "hi, i'm Sam" answers both halves: the greeting is acknowledged as a side effect.
        const string Yaml = """
            intents:
              - id: SET_NAME
                match: { regex: ["i'?m (?<nm>[a-z]+)"] }
                pool: filler
                template: "{$nm}. noted."
              - id: GREETING
                match: { keyword: [hi] }
                pool: filler
                side_effect: true
            """;

        MatchOutcome outcome = Match(Yaml, "hi i'm sam");
        Assert.Equal("SET_NAME", outcome.Primary?.IntentId);
        Assert.Equal(["GREETING"], outcome.SideEffects.Select(c => c.IntentId));
    }

    [Fact]
    public void HigherScoringSideEffectStillDefersToTheSubstantiveIntent()
    {
        const string Yaml = """
            intents:
              - id: QUESTION
                match: { keyword: [why] }
                pool: filler
              - id: APOLOGY
                match: { regex: ["^sorry but why"] }
                pool: filler
                side_effect: true
            """;

        MatchOutcome outcome = Match(Yaml, "sorry but why");
        Assert.Equal("QUESTION", outcome.Primary?.IntentId);
        Assert.Equal(["APOLOGY"], outcome.SideEffects.Select(c => c.IntentId));
    }

    [Fact]
    public void ConfidenceThreshold_SeparatesRealMatchesFromWeakOnes()
    {
        const string Yaml = """
            intents:
              - id: WEAK
                match: { fuzzy: [pathetic], max_distance: 2 }
                pool: filler
            """;

        MatchOutcome outcome = Match(Yaml, "pathetc");
        Assert.NotNull(outcome.Primary);
        Assert.False(outcome.IsConfident);
    }
}
