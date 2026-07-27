using System.Globalization;

namespace Sonarr.Bot.Discord.Moderation;

/// <summary>
/// Parses and formats the short durations moderation commands take ("30m", "2h", "7d",
/// "1h30m"). Controller-layer input handling: a bad string never reaches the service.
/// </summary>
// ponytail: units are s/m/h/d/w only, no natural language. Upgrade path: the reminder parser
// from the utility slice, once it exists — /remind needs "next monday 9am", /tempban does not.
public static class DurationText
{
    /// <summary>
    /// <c>true</c> and <paramref name="duration"/> set, or <c>false</c> and
    /// <paramref name="problem"/> holding a sentence the user can act on.
    /// </summary>
    public static bool TryParse(string? input, out TimeSpan duration, out string problem)
    {
        duration = TimeSpan.Zero;
        problem = string.Empty;

        var text = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
        {
            problem = "Give me a duration like `30m`, `2h` or `7d`.";
            return false;
        }

        var total = TimeSpan.Zero;
        var digits = 0;
        var sawUnit = false;

        foreach (var ch in text)
        {
            if (char.IsAsciiDigit(ch))
            {
                // Cap the accumulator well below overflow; nobody tempbans for 10^9 days.
                if (digits > 100_000)
                {
                    problem = $"`{input!.Trim()}` is an absurd duration — try something like `7d`.";
                    return false;
                }

                digits = (digits * 10) + (ch - '0');
                continue;
            }

            if (digits == 0)
            {
                problem = $"I can't read `{input!.Trim()}` as a duration — try `30m`, `2h` or `7d`.";
                return false;
            }

            TimeSpan? unit = ch switch
            {
                's' => TimeSpan.FromSeconds(digits),
                'm' => TimeSpan.FromMinutes(digits),
                'h' => TimeSpan.FromHours(digits),
                'd' => TimeSpan.FromDays(digits),
                'w' => TimeSpan.FromDays(digits * 7),
                _ => null,
            };

            if (unit is not { } span)
            {
                problem = $"`{ch}` isn't a unit I know — use s, m, h, d or w.";
                return false;
            }

            total += span;
            digits = 0;
            sawUnit = true;
        }

        if (digits != 0 || !sawUnit)
        {
            problem = $"`{input!.Trim()}` needs a unit — s, m, h, d or w (like `30m`).";
            return false;
        }

        duration = total;
        return true;
    }

    /// <summary>Human-readable, coarse: "7d", "2h 30m", "45s".</summary>
    public static string Describe(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "0s";
        }

        List<string> parts = [];
        if (duration.Days > 0)
        {
            parts.Add($"{duration.Days}d");
        }

        if (duration.Hours > 0)
        {
            parts.Add($"{duration.Hours}h");
        }

        if (duration.Minutes > 0)
        {
            parts.Add($"{duration.Minutes}m");
        }

        if (parts.Count == 0)
        {
            parts.Add($"{duration.Seconds}s");
        }

        return string.Join(" ", parts);
    }

    /// <summary>Discord's relative timestamp, so every reader sees it in their own timezone.</summary>
    public static string Relative(DateTimeOffset when)
        => $"<t:{when.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}:R>";
}
