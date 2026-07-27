using System.Globalization;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Evaluates an overlay's <c>activation</c> expression against wall-clock components the
/// caller supplies.
/// </summary>
/// <remarks>
/// The expression language is deliberately tiny — <c>hour in 3..6</c>, <c>month == 10</c> —
/// because it exists so an overlay can say when it applies without the engine reading a clock.
/// The adapter passes in the hour and month; this decides. An expression it cannot parse is
/// never active, and the validator reports it rather than letting a typo silently disable
/// a seasonal overlay.
/// </remarks>
public static class OverlayActivation
{
    /// <summary>Fields an activation expression may read.</summary>
    public static class Fields
    {
        public const string Hour = "hour";
        public const string Month = "month";
        public const string DayOfWeek = "dow";
        public const string Day = "day";
    }

    /// <summary>True when <paramref name="expression"/> parses and holds for these values.</summary>
    public static bool Holds(string expression, IReadOnlyDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!IsWellFormed(expression, out string field, out string op, out int low, out int high))
        {
            return false;
        }

        if (!values.TryGetValue(field, out int actual))
        {
            return false;
        }

        return op switch
        {
            ".." => actual >= low && actual <= high,
            "==" => actual == low,
            "!=" => actual != low,
            ">=" => actual >= low,
            ">" => actual > low,
            "<=" => actual <= low,
            "<" => actual < low,
            _ => false,
        };
    }

    /// <summary>
    /// Overlay ids active for these values, strongest priority first. Two overlays may claim
    /// the same base pool; the caller applies them in this order so the higher priority wins.
    /// </summary>
    public static IReadOnlyList<string> Active(
        PersonaGraph persona,
        IReadOnlyDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(persona);
        return
        [
            .. persona.Overlays
                .Where(o => Holds(o.Activation, values))
                .OrderByDescending(o => o.Priority)
                .Select(o => o.Id)
        ];
    }

    /// <summary>
    /// True when the expression is one this evaluator understands. The validator uses it to
    /// refuse a persona whose overlay could never activate.
    /// </summary>
    public static bool IsWellFormed(string? expression) =>
        IsWellFormed(expression, out _, out _, out _, out _);

    private static bool IsWellFormed(
        string? expression,
        out string field,
        out string op,
        out int low,
        out int high)
    {
        field = string.Empty;
        op = string.Empty;
        low = high = 0;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        string[] parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        field = parts[0];
        op = parts[1] == "in" ? ".." : parts[1];

        if (op == "..")
        {
            string[] bounds = parts[2].Split("..", StringSplitOptions.TrimEntries);
            return bounds.Length == 2
                && int.TryParse(bounds[0], CultureInfo.InvariantCulture, out low)
                && int.TryParse(bounds[1], CultureInfo.InvariantCulture, out high)
                && low <= high;
        }

        return int.TryParse(parts[2], CultureInfo.InvariantCulture, out low);
    }
}
