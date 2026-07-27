using System.Collections.Frozen;
using Sonarr.Elaine.Persona.Yaml;

namespace Sonarr.Elaine.Persona;

public static partial class PersonaLoader
{
    private const string PoolsDir = "pools/";
    private const string DefaultVariant = "default";

    private static FrozenDictionary<string, PoolDef> ParsePools(
        IReadOnlyList<PersonaFile> files, List<PersonaIssue> issues)
    {
        Dictionary<string, PoolDef> pools = new(StringComparer.Ordinal);
        foreach (PersonaFile file in files.Where(f => f.RelativePath.StartsWith(PoolsDir, StringComparison.Ordinal)))
        {
            RawPoolFile? raw = Parse<RawPoolFile>(file, issues);
            if (raw?.Pools is null)
            {
                continue;
            }

            foreach ((string id, object? body) in raw.Pools)
            {
                PersonaLocation loc = new(file.RelativePath, $"pools.{id}");
                if (pools.TryGetValue(id, out PoolDef? existing))
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.DuplicatePool,
                        $"pool '{id}' is already defined in {existing.Location.File}", loc));
                    continue;
                }

                PoolDef? pool = BuildPool(id, body, loc, issues);
                if (pool is not null)
                {
                    pools[id] = pool;
                }
            }
        }

        return pools.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static PoolDef? BuildPool(
        string id, object? body, PersonaLocation loc, List<PersonaIssue> issues)
    {
        // Two authored shapes: a bare list of lines, or a map of mode -> lines where the
        // `default` key holds the unmoded lines.
        switch (body)
        {
            case List<object> flat:
                return new PoolDef
                {
                    Id = id,
                    Lines = ToLines(flat),
                    ByMode = FrozenDictionary<string, IReadOnlyList<string>>.Empty,
                    Location = loc,
                };

            case Dictionary<object, object> map:
            {
                List<string> lines = [];
                Dictionary<string, IReadOnlyList<string>> byMode = new(StringComparer.Ordinal);
                foreach ((object key, object? value) in map)
                {
                    string variant = key.ToString() ?? string.Empty;
                    if (value is not List<object> items)
                    {
                        issues.Add(new PersonaIssue(
                            PersonaIssueSeverity.Error, Rules.PoolShape,
                            $"pool '{id}' variant '{variant}' must be a list of lines", loc));
                        continue;
                    }

                    if (variant == DefaultVariant)
                    {
                        lines = ToLines(items);
                    }
                    else
                    {
                        byMode[variant] = ToLines(items);
                    }
                }

                return new PoolDef
                {
                    Id = id,
                    Lines = lines,
                    ByMode = byMode.ToFrozenDictionary(StringComparer.Ordinal),
                    Location = loc,
                };
            }

            default:
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.PoolShape,
                    $"pool '{id}' must be a list of lines or a map of mode -> lines", loc));
                return null;
        }
    }

    private static List<string> ToLines(List<object> items) =>
        [.. items.Select(i => i?.ToString() ?? string.Empty)
                 .Where(s => !string.IsNullOrWhiteSpace(s))];
}
