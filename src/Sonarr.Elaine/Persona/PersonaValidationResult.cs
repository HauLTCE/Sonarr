using System.Text;

namespace Sonarr.Elaine.Persona;

public enum PersonaIssueSeverity
{
    Warning,
    Error,
}

/// <summary>
/// One validator finding. Content problems are always reported as data, never thrown —
/// callers decide what to do (boot refuses, hot-reload keeps the old graph, CI prints).
/// </summary>
/// <param name="Rule">Stable rule id, e.g. <c>dangling-pool-ref</c>. Tests assert on this.</param>
public sealed record PersonaIssue(
    PersonaIssueSeverity Severity,
    string Rule,
    string Message,
    PersonaLocation Location)
{
    public override string ToString() =>
        $"{Severity.ToString().ToLowerInvariant()} [{Rule}] {Location}: {Message}";
}

/// <summary>
/// Outcome of a load+validate. <see cref="Graph"/> is non-null exactly when
/// <see cref="IsValid"/> — that is the fail-fast contract from docs/10.
/// </summary>
public sealed record PersonaValidationResult(
    PersonaGraph? Graph,
    IReadOnlyList<PersonaIssue> Issues)
{
    public IEnumerable<PersonaIssue> Errors =>
        Issues.Where(i => i.Severity == PersonaIssueSeverity.Error);

    public IEnumerable<PersonaIssue> Warnings =>
        Issues.Where(i => i.Severity == PersonaIssueSeverity.Warning);

    public bool IsValid => Graph is not null && !Errors.Any();

    /// <summary>True if any finding carries <paramref name="rule"/>.</summary>
    public bool Has(string rule) => Issues.Any(i => i.Rule == rule);

    /// <summary>Multi-line report for logs and CI output.</summary>
    public string Report()
    {
        if (Issues.Count == 0)
        {
            return "persona: valid, no issues";
        }

        StringBuilder sb = new();
        sb.Append("persona: ").Append(Errors.Count()).Append(" error(s), ")
          .Append(Warnings.Count()).Append(" warning(s)");
        foreach (PersonaIssue issue in Issues)
        {
            sb.Append('\n').Append("  ").Append(issue);
        }

        return sb.ToString();
    }
}
