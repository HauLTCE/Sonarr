using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The rules that catch persona bugs a human would never spot by reading YAML: unreachable
/// intents, captures that only some patterns produce, and shadowed intents.
/// </summary>
public class PersonaValidatorContentTests
{
    private const string Root = """
        version: 2
        start_activity: idle
        activities:
          - id: idle
            intents: ["*"]
            fallback_pool: filler
          - id: rps
            intents: [RPS_ROCK]
            fallback_pool: filler
        personality:
          baselines: { confidence: 6, anger: 0 }
          decay: { confidence: 0.1, anger: 0.5 }
        modes:
          - id: SMUG
            when: ["confidence >= 8"]
          - id: NEUTRAL
            always: true
        mode_coverage_pools: [mood]
        slots: [name]
        """;

    private const string Pools = """
        pools:
          filler: [whatever]
          mood:
            default: [hm]
            SMUG: [obviously]
        """;

    private static PersonaValidationResult Load(params (string Path, string Text)[] extra) =>
        PersonaLoader.Load(new InMemoryPersonaSource(
            [("sonarr.yaml", Root), ("pools/p.yaml", Pools), .. extra]));

    [Fact]
    public void Baseline_IsValid()
    {
        PersonaValidationResult result = Load(("intents/i.yaml", """
            intents:
              - id: RPS_ROCK
                match: { keyword: [rock] }
                pool: filler
            """));

        Assert.True(result.IsValid, result.Report());
    }

    [Fact]
    public void CaptureUsedByTemplate_MustBeProducedByEveryPattern()
    {
        // "my name is Sam" fills {$nm}; the bare keyword does not, so one in every N replies
        // would render an empty name. The old engine shipped this class of bug silently.
        PersonaValidationResult result = Load(("intents/i.yaml", """
            intents:
              - id: RPS_ROCK
                match:
                  regex: ["my name is (?<nm>\\w+)"]
                  keyword: [name]
                pool: filler
                template: "{$nm}. noted."
            """));

        Assert.True(result.Has(Rules.UnsafeCapture), result.Report());
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CaptureProducedByAllPatterns_IsFine()
    {
        PersonaValidationResult result = Load(("intents/i.yaml", """
            intents:
              - id: RPS_ROCK
                match:
                  regex:
                    - "my name is (?<nm>\\w+)"
                    - "call me (?<nm>\\w+)"
                pool: filler
                template: "{$nm}. noted."
            """));

        Assert.True(result.IsValid, result.Report());
    }

    [Fact]
    public void UndeclaredSlotInAPoolLine_IsAnError()
    {
        PersonaValidationResult result = Load(
            ("pools/extra.yaml", "pools:\n  oops: [\"nice try, {nickname}\"]\n"),
            ("intents/i.yaml", """
                intents:
                  - id: RPS_ROCK
                    match: { keyword: [rock] }
                    pool: oops
                """));

        Assert.True(result.Has(Rules.UnknownSlot), result.Report());
    }

    [Fact]
    public void IntentNoActivityAdmits_IsUnreachable()
    {
        // idle allows everything, so shrink it: now nothing can ever reach GHOST.
        string root = Root.Replace("""intents: ["*"]""", "intents: [RPS_ROCK]", StringComparison.Ordinal);
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", root),
            ("pools/p.yaml", Pools),
            ("intents/i.yaml", """
                intents:
                  - id: RPS_ROCK
                    match: { keyword: [rock] }
                    pool: filler
                  - id: GHOST
                    match: { keyword: [boo] }
                    pool: filler
                """)));

        Assert.True(result.Has(Rules.Unreachable), result.Report());
    }

    [Fact]
    public void ModeCoveragePool_MissingAModeVariant_IsAnError()
    {
        // mood must answer for every non-fallback mode; drop SMUG and she has no smug voice.
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", Root),
            ("pools/p.yaml", "pools:\n  filler: [whatever]\n  mood:\n    default: [hm]\n"),
            ("intents/i.yaml", "intents:\n  - id: RPS_ROCK\n    match: { keyword: [rock] }\n    pool: filler\n")));

        Assert.True(result.Has(Rules.ModeCoverage), result.Report());
    }

    [Fact]
    public void NoAlwaysMode_IsAnError()
    {
        string root = Root.Replace("    always: true", "    when: [\"anger >= 1\"]", StringComparison.Ordinal);
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", root), ("pools/p.yaml", Pools)));

        Assert.True(result.Has(Rules.NoFallbackMode), result.Report());
    }

    [Fact]
    public void TwoAlwaysModes_IsAnError()
    {
        string root = Root.Replace("""when: ["confidence >= 8"]""", "always: true", StringComparison.Ordinal);
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", root), ("pools/p.yaml", Pools)));

        Assert.True(result.Has(Rules.MultipleFallbackModes), result.Report());
    }

    [Fact]
    public void OverlaysAtSamePriorityReplacingTheSamePool_Collide()
    {
        PersonaValidationResult result = Load(
            ("intents/i.yaml", "intents:\n  - id: RPS_ROCK\n    match: { keyword: [rock] }\n    pool: filler\n"),
            ("pools/o.yaml", "pools:\n  mood_a: [spooky]\n  mood_b: [sleepy]\n"),
            ("overlays/a.yaml", "activation: \"month == 10\"\npriority: 5\nreplaces:\n  mood: mood_a\n"),
            ("overlays/b.yaml", "activation: \"hour in 3..6\"\npriority: 5\nreplaces:\n  mood: mood_b\n"));

        Assert.True(result.Has(Rules.OverlayCollision), result.Report());
    }

    [Fact]
    public void UnreferencedPool_IsOnlyAWarning()
    {
        // Authored lines waiting for an intent are fine; they just should not go unnoticed.
        PersonaValidationResult result = Load(
            ("pools/spare.yaml", "pools:\n  unused_lines: [\"nobody asked\"]\n"),
            ("intents/i.yaml", "intents:\n  - id: RPS_ROCK\n    match: { keyword: [rock] }\n    pool: filler\n"));

        Assert.True(result.IsValid, result.Report());
        Assert.Contains(result.Warnings, w => w.Rule == Rules.OrphanPool);
    }

    /// <summary>
    /// A pool the host draws by name is reachable, and saying so is what <c>host_pools</c> is for.
    /// </summary>
    /// <remarks>
    /// Sonarr.Application references the engine and not the reverse, so <c>ChatIntrospection</c>'s
    /// <c>const string ForgotPool = "memory_forget"</c> is invisible to the validator. Before the
    /// declaration existed, 22 of the 78 reported orphans were pools behind working slash commands
    /// — which is worse than a wrong number, because it buried the pools nothing really reaches.
    /// </remarks>
    [Fact]
    public void PoolDeclaredInHostPools_IsNotAnOrphan()
    {
        PersonaValidationResult result = WithHostPools(
            "[drawn_by_command]",
            ("pools/spare.yaml", "pools:\n  drawn_by_command: [\"ask them yourself.\"]\n"));

        Assert.DoesNotContain(
            result.Warnings,
            w => w.Rule == Rules.OrphanPool && w.Message.Contains("drawn_by_command", StringComparison.Ordinal));
    }

    /// <summary>
    /// The failure mode the list introduces: a name that matches no pool would silence an orphan
    /// warning for something that does not exist, so a rename fails loudly instead.
    /// </summary>
    [Fact]
    public void HostPoolsNamingAMissingPool_IsAnError()
    {
        PersonaValidationResult result = WithHostPools("[renamed_away]");

        Assert.True(result.Has(Rules.DanglingPool), result.Report());
    }

    /// <summary>
    /// Loads the baseline persona with a <c>host_pools</c> list spliced into its root.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Load"/> with an extra <c>sonarr.yaml</c>: that helper already supplies one,
    /// and a second file at the same path does not override it — the first wins and the spliced
    /// list is silently never parsed, which is how both of these tests first passed for the wrong
    /// reason and then failed for it.
    /// </remarks>
    private static PersonaValidationResult WithHostPools(
        string list, params (string Path, string Text)[] extra) =>
        PersonaLoader.Load(new InMemoryPersonaSource(
        [
            new PersonaFile("sonarr.yaml", $"{Root}\nhost_pools: {list}\n"),
            new PersonaFile("pools/p.yaml", Pools),
            new PersonaFile(
                "intents/i.yaml",
                "intents:\n  - id: RPS_ROCK\n    match: { keyword: [rock] }\n    pool: filler\n"),
            .. extra.Select(e => new PersonaFile(e.Path, e.Text)),
        ]));

    /// <summary>
    /// A side-effect intent may only add text and affect. Anything else it declares would be
    /// silently dropped when it rides along as a clause.
    /// </summary>
    /// <remarks>
    /// The engine applies a clause's affect and fired-log and nothing else, on purpose: a thing
    /// mentioned in passing must not open a pending question she then waits on. So an author who
    /// writes <c>learns</c> here gets a line that says "noted, Sam" while nothing records the
    /// name — right-looking reply, wrong state, no runtime symptom. Error, not warning.
    /// </remarks>
    [Theory]
    [InlineData("learns:\n      name: nm")]
    [InlineData("asks: [name]")]
    [InlineData("push: rps")]
    [InlineData("pop: true")]
    [InlineData("topic: feelings")]
    public void SideEffectIntentThatDoesMoreThanTalk_IsAnError(string extra)
    {
        PersonaValidationResult result = Load(("intents/i.yaml", $$"""
            intents:
              - id: RPS_ROCK
                match: { regex: ["rock (?<nm>\\w+)"] }
                pool: filler
                side_effect: true
                {{extra}}
            """));

        Assert.True(result.Has(Rules.SideEffectSideEffects), result.Report());
    }

    /// <summary>
    /// The other half: affect is the one thing a clause does apply, so it must stay legal.
    /// </summary>
    [Fact]
    public void SideEffectIntentWithOnlyAffect_IsFine()
    {
        PersonaValidationResult result = Load(("intents/i.yaml", """
            intents:
              - id: RPS_ROCK
                match: { keyword: [rock] }
                pool: filler
                side_effect: true
                affect:
                  anger: 1.0
            """));

        Assert.True(result.IsValid, result.Report());
    }

    [Fact]
    public void AffectDeltaOnAnUndeclaredRegister_IsAnError()
    {
        // The real bug this rule was written for: the migrated persona moved a 'relationship'
        // register nothing declared, so it never decayed and no mode or guard could read it.
        PersonaValidationResult result = Load(("intents/i.yaml", """
            intents:
              - id: RPS_ROCK
                match: { keyword: [rock] }
                pool: filler
                affect:
                  relationship: -2.0
            """));

        Assert.True(result.Has(Rules.UnknownRegister), result.Report());
    }

    [Fact]
    public void RegisterGuardOnAnUndeclaredRegister_IsAnError()
    {
        PersonaValidationResult result = Load(("intents/i.yaml", """
            intents:
              - id: RPS_ROCK
                match: { keyword: [rock] }
                pool: filler
                guards:
                  - kind: register
                    value: "grudge >= 3"
            """));

        Assert.True(result.Has(Rules.UnknownRegister), result.Report());
    }

    [Fact]
    public void MalformedRegisterExpression_IsAnError()
    {
        string root = Root.Replace(
            """when: ["confidence >= 8"]""", """when: ["very confident"]""", StringComparison.Ordinal);
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", root), ("pools/p.yaml", Pools)));

        Assert.True(result.Has(Rules.BadRegisterExpression), result.Report());
    }

    [Fact]
    public void RegisterWithNoDecayRate_IsAWarning()
    {
        // A baseline with no decay is a register that never comes home: one insult and she is
        // angry forever. Only a warning, because a deliberately sticky register is legitimate.
        string root = Root.Replace(
            "decay: { confidence: 0.1, anger: 0.5 }", "decay: { confidence: 0.1 }", StringComparison.Ordinal);
        PersonaValidationResult result = PersonaLoader.Load(new InMemoryPersonaSource(
            ("sonarr.yaml", root), ("pools/p.yaml", Pools),
            ("intents/i.yaml", "intents:\n  - id: RPS_ROCK\n    match: { keyword: [rock] }\n    pool: filler\n")));

        Assert.True(result.IsValid, result.Report());
        Assert.Contains(result.Warnings, w => w.Rule == Rules.MissingField);
    }

    [Fact]
    public void BroadIntentThatAlwaysOutscoresANarrowOne_IsReportedAsShadowing()
    {
        // The exact v1 bug: keyword [mom] declared first ate regex "your mom" forever. Here
        // the specific one still wins at runtime, but a truly unwinnable intent gets reported.
        PersonaValidationResult result = Load(("intents/i.yaml", """
            intents:
              - id: RPS_ROCK
                match: { keyword: [rock] }
                pool: filler
              - id: ROCK_SPECIFIC
                match: { all_keywords: [rock] }
                pool: filler
            """));

        Assert.True(result.IsValid, result.Report());
        Assert.Contains(
            result.Warnings,
            w => w.Rule == Rules.Shadowed && w.Message.Contains("ROCK_SPECIFIC", StringComparison.Ordinal));
    }
}
