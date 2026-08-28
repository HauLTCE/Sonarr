using System.Text.RegularExpressions;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona.Yaml;

namespace Sonarr.Elaine.Persona;

public static partial class PersonaLoader
{
    private const string IntentsDir = "intents/";

    private static IReadOnlyList<IntentDef> ParseIntents(
        IReadOnlyList<PersonaFile> files, List<PersonaIssue> issues)
    {
        List<IntentDef> intents = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (PersonaFile file in files.Where(f => f.RelativePath.StartsWith(IntentsDir, StringComparison.Ordinal)))
        {
            RawIntentFile? raw = Parse<RawIntentFile>(file, issues);
            if (raw?.Intents is null)
            {
                continue;
            }

            for (int i = 0; i < raw.Intents.Count; i++)
            {
                RawIntent r = raw.Intents[i];
                PersonaLocation loc = new(file.RelativePath, $"intents[{i}]");
                if (string.IsNullOrWhiteSpace(r.Id))
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.MissingField, "intent id is required", loc));
                    continue;
                }

                if (!seen.Add(r.Id))
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.DuplicateIntent,
                        $"intent '{r.Id}' is declared more than once", loc));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(r.Pool))
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.MissingField,
                        $"intent '{r.Id}' has no pool", loc));
                    continue;
                }

                intents.Add(new IntentDef
                {
                    Id = r.Id,
                    DeclarationIndex = intents.Count,
                    Patterns = BuildPatterns(r.Match, loc, issues),
                    SemanticExamples = r.Examples ?? [],
                    Guards = BuildGuards(r.Guards, loc),
                    Pool = r.Pool!,
                    Template = r.Template,
                    Topic = r.Topic,
                    Learns = (r.Learns ?? []).ToDictionary(
                        kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                    Asks = r.Asks ?? [],
                    PushActivity = r.Push,
                    PopActivity = r.Pop,
                    Affect = [.. (r.Affect ?? []).Select(kv => new AffectDelta(kv.Key, kv.Value))],
                    SideEffect = r.SideEffect,
                    Family = r.Family,
                    Once = r.Once,
                    Cooldown = r.Cooldown,
                    Specificity = r.Specificity,
                    Location = loc,
                });
            }
        }

        return intents;
    }

    private static IReadOnlyList<GuardDef> BuildGuards(
        List<RawGuard>? raw, PersonaLocation loc) =>
        [.. (raw ?? [])
            .Where(g => !string.IsNullOrWhiteSpace(g.Kind))
            .Select((g, i) => new GuardDef(
                g.Kind!, g.Value ?? string.Empty, g.Bonus,
                new PersonaLocation(loc.File, $"{loc.Path}.guards[{i}]")))];

    private static IReadOnlyList<LexicalPattern> BuildPatterns(
        RawMatch? match, PersonaLocation loc, List<PersonaIssue> issues)
    {
        if (match is null)
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.MissingField,
                "intent has no match block", loc));
            return [];
        }

        List<LexicalPattern> patterns = [];
        AddWordPattern(patterns, MatchKind.Keyword, match.Keyword, match, loc, "keyword");
        AddWordPattern(patterns, MatchKind.AllKeywords, match.AllKeywords, match, loc, "all_keywords");
        AddWordPattern(patterns, MatchKind.Fuzzy, match.Fuzzy, match, loc, "fuzzy");
        AddStylePattern(patterns, match.Style, loc, issues);

        for (int i = 0; i < (match.Regex?.Count ?? 0); i++)
        {
            string source = match.Regex![i];
            PersonaLocation rloc = new(loc.File, $"{loc.Path}.match.regex[{i}]");
            Regex? compiled = TryCompile(source, rloc, issues);
            if (compiled is null)
            {
                continue;
            }

            patterns.Add(new LexicalPattern
            {
                Kind = MatchKind.Regex,
                Words = [],
                Regex = compiled,
                RegexSource = source,
                CaptureNames = [.. compiled.GetGroupNames().Where(n => !int.TryParse(n, out _))],
                Location = rloc,
            });
        }

        if (patterns.Count == 0)
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.EmptyMatch,
                "intent match block produced no usable patterns", loc));
        }

        return patterns;
    }

    private static void AddWordPattern(
        List<LexicalPattern> into, MatchKind kind, List<string>? words,
        RawMatch match, PersonaLocation loc, string field)
    {
        if (words is null || words.Count == 0)
        {
            return;
        }

        into.Add(new LexicalPattern
        {
            Kind = kind,
            // YAML 1.1 turns bare yes/no/on/off into bools; ToString-then-lower puts them
            // back to the text the author wrote.
            Words = [.. words.Select(w => w.ToLowerInvariant())],
            MaxDistance = match.MaxDistance,
            MinLength = match.MinLength,
            Location = new PersonaLocation(loc.File, $"{loc.Path}.match.{field}"),
        });
    }

    /// <summary>
    /// <c>style: [caps, elongated]</c> → one pattern matching any of those normalizer flags.
    /// </summary>
    private static void AddStylePattern(
        List<LexicalPattern> into, List<string>? names, PersonaLocation loc,
        List<PersonaIssue> issues)
    {
        if (names is null || names.Count == 0)
        {
            return;
        }

        PersonaLocation sloc = new(loc.File, $"{loc.Path}.match.style");
        TextStyle flags = TextStyle.None;
        foreach (string name in names)
        {
            if (Enum.TryParse(name.Replace("_", string.Empty, StringComparison.Ordinal),
                    ignoreCase: true, out TextStyle flag)
                && flag is not TextStyle.None)
            {
                flags |= flag;
                continue;
            }

            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.UnknownStyle,
                $"style '{name}' is not a known style flag", sloc));
        }

        if (flags is not TextStyle.None)
        {
            into.Add(new LexicalPattern
            {
                Kind = MatchKind.Style,
                Words = [.. names.Select(n => n.ToLowerInvariant())],
                Style = flags,
                Location = sloc,
            });
        }
    }

    private static Regex? TryCompile(
        string source, PersonaLocation loc, List<PersonaIssue> issues)
    {
        try
        {
            // No implicit flags: patterns are authored lowercase and matched against the
            // lowered form, so IgnoreCase would only hide authoring mistakes.
            return new Regex(source, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        }
        catch (ArgumentException ex)
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.BadRegex,
                $"regex '{source}' does not compile: {ex.Message}", loc));
            return null;
        }
    }
}
