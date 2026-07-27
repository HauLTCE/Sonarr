using System.Text;
using System.Text.RegularExpressions;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Substitutes <c>{slot}</c> and <c>{$capture}</c> references in an authored line.
/// </summary>
/// <remarks>
/// Substitution only — there is no expression language and no generation. The validator has
/// already proven every reference resolves, so the only failure left at runtime is a slot
/// that is declared but not yet learned; <see cref="Render"/> reports that instead of
/// shipping a literal <c>{name}</c> to a channel, and the caller re-picks a line.
/// </remarks>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{(\$?)([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();

    /// <summary>True when <paramref name="line"/> has no references at all — the common case.</summary>
    public static bool IsLiteral(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return !line.Contains('{', StringComparison.Ordinal);
    }

    /// <summary>
    /// Renders <paramref name="line"/>, or returns false when a reference has no value.
    /// </summary>
    public static bool TryRender(
        string line,
        IReadOnlyDictionary<string, string> slots,
        IReadOnlyDictionary<string, string> captures,
        out string rendered)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(captures);

        if (IsLiteral(line))
        {
            rendered = line;
            return true;
        }

        StringBuilder builder = new(line.Length);
        int at = 0;
        foreach (Match m in ReferenceRegex().Matches(line))
        {
            IReadOnlyDictionary<string, string> source =
                m.Groups[1].Value == "$" ? captures : slots;
            if (!source.TryGetValue(m.Groups[2].Value, out string? value)
                || string.IsNullOrWhiteSpace(value))
            {
                rendered = string.Empty;
                return false;
            }

            builder.Append(line, at, m.Index - at).Append(value);
            at = m.Index + m.Length;
        }

        rendered = builder.Append(line, at, line.Length - at).ToString();
        return true;
    }
}
