using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// One test per failure class. Each builds the smallest broken persona that trips exactly
/// one rule, so a rule that stops firing shows up here and not as a silent runtime surprise.
/// </summary>
public class PersonaValidatorTests
{
    private const string MinimalRoot = """
        version: 2
        start_activity: idle
        activities:
          - id: idle
            intents: ["*"]
            fallback_pool: filler
        modes:
          - id: NEUTRAL
            always: true
        slots: [name]
        """;

    private static PersonaValidationResult Load(params (string Path, string Text)[] files) =>
        PersonaLoader.Load(new InMemoryPersonaSource(files));

    private static PersonaValidationResult LoadWithRoot(params (string Path, string Text)[] files) =>
        Load([("sonarr.yaml", MinimalRoot), .. files]);

    private static void AssertOnlyIssue(PersonaValidationResult result, string rule)
    {
        Assert.True(result.Has(rule), result.Report());
        Assert.False(result.IsValid, result.Report());
    }

    [Fact]
    public void MissingRoot_IsAnError()
    {
        PersonaValidationResult result = Load(("pools/p.yaml", "pools:\n  filler: [hi]\n"));
        AssertOnlyIssue(result, Rules.MissingRoot);
    }

    [Fact]
    public void UnparseableYaml_IsReportedNotThrown()
    {
        PersonaValidationResult result = Load(("sonarr.yaml", "version: 2\n  bad: [indent\n"));
        AssertOnlyIssue(result, Rules.YamlSyntax);
    }

    [Fact]
    public void MissingStartActivity_IsAnError()
    {
        PersonaValidationResult result = Load(("sonarr.yaml", "version: 2\n"));
        AssertOnlyIssue(result, Rules.MissingField);
    }

    [Fact]
    public void DanglingPoolRef_IsAnError()
    {
        PersonaValidationResult result = LoadWithRoot(
            ("pools/p.yaml", "pools:\n  filler: [hi]\n"),
            ("intents/i.yaml", """
                intents:
                  - id: HI
                    match: { keyword: [hi] }
                    pool: does_not_exist
                """));

        AssertOnlyIssue(result, Rules.DanglingPool);
    }

    [Fact]
    public void DanglingTierRef_IsAnError()
    {
        PersonaValidationResult result = LoadWithRoot(
            ("pools/p.yaml", "pools:\n  filler: [hi]\n"),
            ("intents/i.yaml", """
                intents:
                  - id: HI
                    match: { keyword: [hi] }
                    pool: filler
                    guards:
                      - kind: min_tier
                        value: nonexistent_tier
                """));

        AssertOnlyIssue(result, Rules.DanglingTier);
    }

    [Fact]
    public void UnknownGuardKind_IsAnError()
    {
        PersonaValidationResult result = LoadWithRoot(
            ("pools/p.yaml", "pools:\n  filler: [hi]\n"),
            ("intents/i.yaml", """
                intents:
                  - id: HI
                    match: { keyword: [hi] }
                    pool: filler
                    guards:
                      - kind: vibe_check
                        value: "yes"
                """));

        AssertOnlyIssue(result, Rules.UnknownGuardKind);
    }

    [Fact]
    public void DuplicateIntentId_IsAnError()
    {
        PersonaValidationResult result = LoadWithRoot(
            ("pools/p.yaml", "pools:\n  filler: [hi]\n"),
            ("intents/i.yaml", """
                intents:
                  - id: HI
                    match: { keyword: [hi] }
                    pool: filler
                  - id: HI
                    match: { keyword: [hello] }
                    pool: filler
                """));

        AssertOnlyIssue(result, Rules.DuplicateIntent);
    }

    [Fact]
    public void DuplicatePoolAcrossFiles_IsAnError()
    {
        PersonaValidationResult result = LoadWithRoot(
            ("pools/a.yaml", "pools:\n  filler: [hi]\n"),
            ("pools/b.yaml", "pools:\n  filler: [hello]\n"));

        AssertOnlyIssue(result, Rules.DuplicatePool);
    }

    [Fact]
    public void EmptyPool_IsAnError()
    {
        // An empty pool is a reply that renders as nothing. Better to refuse to boot.
        PersonaValidationResult result = LoadWithRoot(("pools/p.yaml", "pools:\n  filler: []\n"));
        AssertOnlyIssue(result, Rules.EmptyPool);
    }

    [Fact]
    public void EmptyMatchBlock_IsAnError()
    {
        PersonaValidationResult result = LoadWithRoot(
            ("pools/p.yaml", "pools:\n  filler: [hi]\n"),
            ("intents/i.yaml", "intents:\n  - id: HI\n    match: {}\n    pool: filler\n"));

        AssertOnlyIssue(result, Rules.EmptyMatch);
    }

    [Fact]
    public void UncompilableRegex_IsAnError()
    {
        PersonaValidationResult result = LoadWithRoot(
            ("pools/p.yaml", "pools:\n  filler: [hi]\n"),
            ("intents/i.yaml", """
                intents:
                  - id: HI
                    match: { regex: ["(unclosed"] }
                    pool: filler
                """));

        AssertOnlyIssue(result, Rules.BadRegex);
    }
}
