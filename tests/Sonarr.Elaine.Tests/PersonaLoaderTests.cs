using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Round-trip: what the YAML says is what ends up in the frozen graph, and loading is a
/// pure function of file contents (no filesystem, no enumeration order dependence).
/// </summary>
public class PersonaLoaderTests
{
    private const string Root = """
        version: 2
        start_activity: idle
        activities:
          - id: idle
            intents: ["*"]
            fallback_pool: filler
          - id: argument
            intents: [ANGRY]
            fallback_pool: filler
        personality:
          baselines:
            anger: 0
            trust: 2
          decay:
            anger: 1.5
        tiers:
          - id: stranger
            min_trust: 0
          - id: regular
            min_trust: 15
        modes:
          - id: ANNOYED
            when: ["anger >= 4"]
          - id: NEUTRAL
            always: true
        mode_coverage_pools: [mood]
        slots: [name, age]
        """;

    private const string Pools = """
        pools:
          filler: [whatever]
          mood:
            default: [hm]
            ANNOYED: [what now, "spit it out"]
        """;

    private const string Intents = """
        intents:
          - id: ANGRY
            match:
              keyword: [mad, angry]
              all_keywords: [really, mad]
              fuzzy: [furious]
              max_distance: 2
              min_length: 5
              regex: ["i(?:'?m| am) (?<how>\\w+) mad"]
            pool: filler
            # No {$how} here on purpose: only the regex can produce that capture, and the
            # validator rightly rejects a template the keyword patterns cannot fill.
            template: "sure, {name}."
            topic: feelings
            push: argument
            once: true
            cooldown: 3
            affect:
              anger: 2.5
              trust: -1
            guards:
              - kind: min_tier
                value: regular
                bonus: 0.25
          # side_effect lives on its own intent rather than on ANGRY with the rest: a clause that
          # rides along applies affect and nothing else, so declaring it beside `push` and `topic`
          # is an error the validator now names. Everything else still round-trips on one intent.
          - id: ASIDE
            match:
              keyword: [aside]
            pool: filler
            side_effect: true
            affect:
              trust: 0.5
        """;

    private static PersonaGraph Load()
    {
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", Root),
            ("pools/p.yaml", Pools),
            ("intents/i.yaml", Intents),
            ("stances.yaml", "stances:\n  - topic: pineapple_pizza\n    stance: against\n    pool: filler\n    strength: 2\n"),
            ("overlays/october.yaml", "activation: \"month == 10\"\npriority: 5\nreplaces:\n  filler: mood\n")));

        Assert.True(result.IsValid, result.Report());
        return result.Graph!;
    }

    [Fact]
    public void Root_RoundTrips()
    {
        PersonaRoot root = Load().Root;

        Assert.Equal(2, root.Version);
        Assert.Equal("idle", root.StartActivity);
        Assert.Equal(["idle", "argument"], root.Activities.Select(a => a.Id));
        Assert.Equal(2, root.Personality.Baselines["trust"]);
        Assert.Equal(1.5, root.Personality.Decay["anger"]);
        Assert.Equal(15, root.Tiers.Single(t => t.Id == "regular").MinTrust);
        Assert.True(root.Modes.Single(m => m.Id == "NEUTRAL").Always);
        Assert.Equal(["anger >= 4"], root.Modes.Single(m => m.Id == "ANNOYED").When);
        Assert.Equal(["name", "age"], root.Slots);
    }

    [Fact]
    public void ActivityWildcard_AllowsEverythingAndAnAllowListDoesNot()
    {
        PersonaRoot root = Load().Root;

        Assert.True(root.Activities.Single(a => a.Id == "idle").Allows("ANYTHING"));
        Assert.True(root.Activities.Single(a => a.Id == "argument").Allows("ANGRY"));
        Assert.False(root.Activities.Single(a => a.Id == "argument").Allows("ANYTHING"));
    }

    [Fact]
    public void Intent_RoundTripsEveryField()
    {
        IntentDef intent = Load().IntentsById["ANGRY"];

        Assert.Equal("filler", intent.Pool);
        Assert.Equal("sure, {name}.", intent.Template);
        Assert.Equal("feelings", intent.Topic);
        Assert.Equal("argument", intent.PushActivity);
        Assert.True(intent.Once);
        Assert.Equal(3, intent.Cooldown);
        Assert.True(Load().IntentsById["ASIDE"].SideEffect);
        Assert.Contains(intent.Affect, a => a.Register == "anger" && a.Delta == 2.5);
        Assert.Contains(intent.Affect, a => a.Register == "trust" && a.Delta == -1);

        GuardDef guard = Assert.Single(intent.Guards);
        Assert.Equal(GuardDef.MinTier, guard.Kind);
        Assert.Equal("regular", guard.Value);
        Assert.Equal(0.25, guard.Bonus);
    }

    [Fact]
    public void MatchBlock_BecomesOnePatternPerKindAndOnePerRegex()
    {
        IntentDef intent = Load().IntentsById["ANGRY"];

        // A word list is one pattern holding many words (any-of), not one pattern per word:
        // matching two keywords from the same list is not corroboration, it is one signal.
        LexicalPattern keyword = Assert.Single(intent.Patterns, p => p.Kind == MatchKind.Keyword);
        Assert.Equal(["mad", "angry"], keyword.Words);

        Assert.Single(intent.Patterns, p => p.Kind == MatchKind.AllKeywords);
        Assert.Single(intent.Patterns, p => p.Kind == MatchKind.Regex);

        LexicalPattern fuzzy = Assert.Single(intent.Patterns, p => p.Kind == MatchKind.Fuzzy);
        Assert.Equal(2, fuzzy.MaxDistance);
        Assert.Equal(5, fuzzy.MinLength);

        LexicalPattern regex = intent.Patterns.Single(p => p.Kind == MatchKind.Regex);
        Assert.Equal(["how"], regex.CaptureNames);
        Assert.NotNull(regex.Regex);
    }

    [Fact]
    public void PoolModeVariants_RoundTripAndFallBack()
    {
        PoolDef mood = Load().Pools["mood"];

        Assert.Equal(["hm"], mood.Lines);
        Assert.Equal(["what now", "spit it out"], mood.For("ANNOYED"));
        Assert.Equal(["hm"], mood.For("NEUTRAL"));
        Assert.Equal(["hm"], mood.For(null));
        Assert.False(mood.IsEmpty);
    }

    [Fact]
    public void StancesAndOverlays_RoundTrip()
    {
        PersonaGraph graph = Load();

        StanceDef stance = Assert.Single(graph.Stances);
        Assert.Equal("pineapple_pizza", stance.Topic);
        Assert.Equal("against", stance.Stance);
        Assert.Equal(2, stance.Strength);

        OverlayDef overlay = Assert.Single(graph.Overlays);
        Assert.Equal("october", overlay.Id); // defaults to the filename
        Assert.Equal("month == 10", overlay.Activation);
        Assert.Equal(5, overlay.Priority);
        Assert.Equal("mood", overlay.Replaces["filler"]);
    }

    [Fact]
    public void DeclarationOrder_FollowsSortedPathsNotSourceOrder()
    {
        // Declaration order is the scoring tie-breaker, so it must not depend on the order a
        // directory happens to enumerate in.
        (string, string) a = ("intents/a.yaml", "intents:\n  - id: FIRST\n    match: { keyword: [x] }\n    pool: filler\n");
        (string, string) b = ("intents/b.yaml", "intents:\n  - id: SECOND\n    match: { keyword: [y] }\n    pool: filler\n");
        (string, string) root = ("sonarr.yaml", "version: 2\nstart_activity: idle\nactivities:\n  - id: idle\n    intents: [\"*\"]\n    fallback_pool: filler\nmodes:\n  - id: NEUTRAL\n    always: true\n");
        (string, string) pools = ("pools/p.yaml", "pools:\n  filler: [whatever]\n");

        PersonaValidationResult forward = PersonaLoader.Load(new InMemoryPersonaSource(root, pools, a, b));
        PersonaValidationResult reversed = PersonaLoader.Load(new InMemoryPersonaSource(b, a, pools, root));

        Assert.Equal(
            forward.Graph!.Intents.Select(i => i.Id),
            reversed.Graph!.Intents.Select(i => i.Id));
        Assert.Equal(0, forward.Graph.IntentsById["FIRST"].DeclarationIndex);
    }

    [Fact]
    public void BackslashPaths_AreNormalized()
    {
        // Windows hosts hand back "pools\p.yaml"; the loader must still find sonarr.yaml.
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", Root), ("pools\\p.yaml", Pools), ("intents\\i.yaml", Intents)));

        Assert.True(result.IsValid, result.Report());
        Assert.Contains("ANGRY", result.Graph!.IntentsById.Keys);
    }

    [Fact]
    public void InvalidPersona_RefusesToBoot()
    {
        PersonaHolder? holder = PersonaHolder.TryCreate(
            new InMemoryPersonaSource(("sonarr.yaml", "version: 2\n")), out PersonaValidationResult result);

        Assert.Null(holder);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RejectedHotReload_KeepsThePreviouslyValidPersonaLive()
    {
        IPersonaSource good = new InMemoryPersonaSource(
            ("sonarr.yaml", Root), ("pools/p.yaml", Pools), ("intents/i.yaml", Intents));

        PersonaHolder? holder = PersonaHolder.TryCreate(good, out _);
        Assert.NotNull(holder);
        PersonaGraph before = holder.Current;

        bool swapped = holder.TryReload(
            new InMemoryPersonaSource(("sonarr.yaml", "version: 2\n")), out PersonaValidationResult failed);

        Assert.False(swapped);
        Assert.False(failed.IsValid);
        Assert.Same(before, holder.Current);
        Assert.Equal(1, holder.RejectedReloads);

        Assert.True(holder.TryReload(good, out _));
    }
}
