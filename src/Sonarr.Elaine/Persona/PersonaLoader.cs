using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Sonarr.Elaine.Persona.Yaml;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sonarr.Elaine.Persona;

/// <summary>
/// Turns persona files into a frozen <see cref="PersonaGraph"/>, then hands the graph to
/// <see cref="PersonaValidator"/>. Pure: same file contents in, same result out.
/// </summary>
public static partial class PersonaLoader
{
    public const string RootFile = "sonarr.yaml";
    public const string StancesFile = "stances.yaml";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Load and validate. Returns a result with <c>Graph == null</c> if anything is wrong;
    /// never throws for bad content (only for a null argument).
    /// </summary>
    public static PersonaValidationResult Load(IPersonaSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<PersonaIssue> issues = [];

        // Sorted so declaration order (the tie-breaker for scoring) is a function of the
        // file set, not of directory enumeration order.
        List<PersonaFile> files = [.. source.Read()
            .Select(f => new PersonaFile(f.RelativePath.Replace('\\', '/'), f.Text))
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)];

        PersonaRoot? root = ParseRoot(files, issues);
        FrozenDictionary<string, PoolDef> pools = ParsePools(files, issues);
        IReadOnlyList<IntentDef> intents = ParseIntents(files, issues);
        IReadOnlyList<StanceDef> stances = ParseStances(files, issues);
        IReadOnlyList<OverlayDef> overlays = ParseOverlays(files, issues);

        if (root is null || issues.Any(i => i.Severity == PersonaIssueSeverity.Error))
        {
            return new PersonaValidationResult(null, issues);
        }

        PersonaGraph graph = new()
        {
            Root = root,
            Intents = intents,
            Pools = pools,
            Stances = stances,
            Overlays = overlays,
        };

        issues.AddRange(PersonaValidator.Validate(graph));
        bool ok = !issues.Any(i => i.Severity == PersonaIssueSeverity.Error);
        return new PersonaValidationResult(ok ? graph : null, issues);
    }

    /// <summary>Index of <paramref name="path"/>, or -1. Needed because PersonaFile is a struct.</summary>
    private static int IndexOf(IReadOnlyList<PersonaFile> files, string path)
    {
        for (int i = 0; i < files.Count; i++)
        {
            if (string.Equals(files[i].RelativePath, path, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static T? Parse<T>(PersonaFile file, List<PersonaIssue> issues)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(file.Text))
        {
            // An empty file is not a syntax error, it just contributes nothing.
            return null;
        }

        try
        {
            return Yaml.Deserialize<T>(file.Text);
        }
        catch (YamlException ex)
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error,
                Rules.YamlSyntax,
                ex.Message,
                new PersonaLocation(file.RelativePath, $"line {ex.Start.Line}")));
            return null;
        }
    }

    private static PersonaRoot? ParseRoot(IReadOnlyList<PersonaFile> files, List<PersonaIssue> issues)
    {
        int at = IndexOf(files, RootFile);
        if (at < 0 || string.IsNullOrWhiteSpace(files[at].Text))
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.MissingRoot,
                $"{RootFile} is missing or empty", new PersonaLocation(RootFile, "")));
            return null;
        }

        RawRoot? raw = Parse<RawRoot>(files[at], issues);
        if (raw is null)
        {
            return null;
        }

        PersonaLocation loc = new(RootFile, "");
        if (string.IsNullOrWhiteSpace(raw.StartActivity))
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.MissingField,
                "start_activity is required", loc));
            return null;
        }

        List<ActivityDef> activities = [.. (raw.Activities ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Id))
            .Select((a, i) => new ActivityDef(
                a.Id!,
                a.Intents ?? [ActivityDef.AllIntents],
                a.FallbackPool ?? string.Empty,
                new PersonaLocation(RootFile, $"activities[{i}]")))];

        List<ModeDef> modes = [.. (raw.Modes ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select((m, i) => new ModeDef(
                m.Id!, m.When ?? [], m.Always,
                new PersonaLocation(RootFile, $"modes[{i}]")))];

        List<TierDef> tiers = [.. (raw.Tiers ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .Select((t, i) => new TierDef(
                t.Id!, t.MinTrust, new PersonaLocation(RootFile, $"tiers[{i}]")))];

        return new PersonaRoot
        {
            Version = raw.Version,
            StartActivity = raw.StartActivity!,
            Activities = activities,
            Personality = new PersonalityDef(
                (raw.Personality?.Baselines ?? []).ToFrozenDictionary(StringComparer.Ordinal),
                (raw.Personality?.Decay ?? []).ToFrozenDictionary(StringComparer.Ordinal)),
            Tiers = tiers,
            Modes = modes,
            ModeCoveragePools = raw.ModeCoveragePools ?? [],
            Slots = raw.Slots ?? [],
            Location = loc,
        };
    }
}
