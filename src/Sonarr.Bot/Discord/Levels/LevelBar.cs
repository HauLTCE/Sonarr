using System.Globalization;

namespace Sonarr.Bot.Discord.Levels;

/// <summary>
/// The rank "card": a text progress bar, not an image.
/// </summary>
/// <remarks>
/// Deliberate: the host is a Pentium J2900 with no AVX (docs/02-architecture.md). SkiaSharp or
/// ImageSharp would add ~15 MB of native code and 100-300 ms of CPU per <c>/rank</c>, competing
/// with Lavalink for the same four cores. A block-character bar inside an embed costs nothing and
/// reads the same on mobile.
/// </remarks>
public static class LevelBar
{
    /// <summary>Bar width in characters — fits an embed line on mobile without wrapping.</summary>
    public const int Width = 18;

    private const char Filled = '█';
    private const char Empty = '░';

    /// <summary>Renders <paramref name="fraction"/> (0.0-1.0) as a fixed-width bar.</summary>
    public static string Render(double fraction)
    {
        var clamped = double.IsNaN(fraction) ? 0d : Math.Clamp(fraction, 0d, 1d);
        var filled = (int)Math.Round(clamped * Width, MidpointRounding.ToZero);

        // A member with any progress at all should see at least one block, and only a full level
        // should look full.
        if (filled == 0 && clamped > 0)
        {
            filled = 1;
        }
        else if (filled == Width && clamped < 1)
        {
            filled = Width - 1;
        }

        return new string(Filled, filled) + new string(Empty, Width - filled);
    }

    /// <summary>Percentage as a short label, e.g. "42%".</summary>
    public static string Percent(double fraction)
        => ((int)(Math.Clamp(fraction, 0d, 1d) * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>Voice time as "3h 20m" / "12m" — <c>0</c> reads as "none".</summary>
    public static string Duration(long seconds)
    {
        if (seconds < 60)
        {
            return "none";
        }

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m";
    }
}
