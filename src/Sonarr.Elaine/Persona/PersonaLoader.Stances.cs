using System.Collections.Frozen;
using Sonarr.Elaine.Persona.Yaml;

namespace Sonarr.Elaine.Persona;

public static partial class PersonaLoader
{
    private const string OverlaysDir = "overlays/";

    private static IReadOnlyList<StanceDef> ParseStances(
        IReadOnlyList<PersonaFile> files, List<PersonaIssue> issues)
    {
        // PersonaFile is a struct, so FirstOrDefault cannot report absence — ask explicitly.
        int at = IndexOf(files, StancesFile);
        if (at < 0)
        {
            return [];
        }

        RawStanceFile? raw = Parse<RawStanceFile>(files[at], issues);
        List<StanceDef> stances = [];
        for (int i = 0; i < (raw?.Stances?.Count ?? 0); i++)
        {
            RawStance s = raw!.Stances![i];
            PersonaLocation loc = new(StancesFile, $"stances[{i}]");
            if (string.IsNullOrWhiteSpace(s.Topic) || string.IsNullOrWhiteSpace(s.Pool))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.MissingField,
                    "stance needs both topic and pool", loc));
                continue;
            }

            stances.Add(new StanceDef(s.Topic!, s.Stance ?? "neutral", s.Pool!, s.Strength, loc));
        }

        return stances;
    }

    private static IReadOnlyList<OverlayDef> ParseOverlays(
        IReadOnlyList<PersonaFile> files, List<PersonaIssue> issues)
    {
        List<OverlayDef> overlays = [];
        foreach (PersonaFile file in files.Where(f => f.RelativePath.StartsWith(OverlaysDir, StringComparison.Ordinal)))
        {
            RawOverlayFile? raw = Parse<RawOverlayFile>(file, issues);
            if (raw is null)
            {
                continue;
            }

            PersonaLocation loc = new(file.RelativePath, "");
            string id = raw.Id
                ?? Path.GetFileNameWithoutExtension(file.RelativePath);
            if (string.IsNullOrWhiteSpace(raw.Activation))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.MissingField,
                    $"overlay '{id}' has no activation expression", loc));
                continue;
            }

            overlays.Add(new OverlayDef(
                id,
                raw.Activation!,
                (raw.Replaces ?? []).ToFrozenDictionary(StringComparer.Ordinal),
                raw.Priority,
                loc));
        }

        return overlays;
    }
}
