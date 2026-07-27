using System.Globalization;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// Evaluates a <c>register op number</c> expression, e.g. <c>anger &gt;= 7</c>.
/// </summary>
/// <remarks>
/// One implementation, two callers: intent <c>register</c> guards and mode <c>when</c>
/// clauses use the same syntax in the persona files, so they must not drift apart. Unknown
/// registers and malformed expressions are false — the persona validator's job is to keep
/// them out, and a mood mode that silently fails to activate beats one that throws
/// mid-reply.
/// </remarks>
public static class RegisterExpression
{
    /// <summary>
    /// The register this expression reads, or <see langword="null"/> if it is malformed.
    /// Lets the validator check register names against <c>personality.baselines</c> without
    /// re-implementing the split.
    /// </summary>
    public static string? RegisterOf(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Split(expression) is { } parts ? parts[0] : null;
    }

    public static bool Holds(string expression, IReadOnlyDictionary<string, double>? registers)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (Split(expression) is not { } parts)
        {
            return false;
        }

        double threshold = double.Parse(parts[2], CultureInfo.InvariantCulture);
        if (registers?.TryGetValue(parts[0], out double actual) != true)
        {
            return false;
        }

        return parts[1] switch
        {
            ">=" => actual >= threshold,
            ">" => actual > threshold,
            "<=" => actual <= threshold,
            "<" => actual < threshold,
            "==" => actual == threshold,
            "!=" => actual != threshold,
            _ => false,
        };
    }

    /// <summary>The three tokens of a well-formed expression, or null.</summary>
    private static string[]? Split(string expression)
    {
        string[] parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && double.TryParse(parts[2], CultureInfo.InvariantCulture, out _)
                ? parts
                : null;
    }
}
