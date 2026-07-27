namespace Sonarr.Domain.Levels;

/// <summary>
/// The XP → level curve. Pure math, no state: the same numbers the old Python bot used
/// (<c>5·L² + 50·L + 100</c> as the cumulative threshold to leave level L), so migrated
/// rows keep the level their owner already earned (docs/04-database.md#data-migration-map).
/// </summary>
/// <remarks>
/// XP is cumulative and never reset on level-up, so <see cref="ThresholdFor"/> is a total,
/// not a per-level cost. Levels start at 1.
/// </remarks>
public static class LevelCurve
{
    /// <summary>Everyone starts here — the legacy table seeded level 1, xp 0.</summary>
    public const int FirstLevel = 1;

    /// <summary>Total XP a member must reach to leave <paramref name="level"/> behind.</summary>
    public static long ThresholdFor(int level)
    {
        var l = Math.Max(level, FirstLevel);
        return (5L * l * l) + (50L * l) + 100L;
    }

    /// <summary>The level a given total XP is worth.</summary>
    public static int LevelFor(long xp)
    {
        // Monotonic thresholds, so a walk is exact and terminates: level 50 is ~15k XP, i.e.
        // ~50 iterations at the very top of the curve. Cheaper than a float square root and
        // it cannot drift by one at a boundary.
        var level = FirstLevel;
        while (xp >= ThresholdFor(level))
        {
            level++;
        }

        return level;
    }

    /// <summary>
    /// Progress inside the current level: XP earned since entering it, and the span of the
    /// level. <paramref name="into"/> is always &lt; <paramref name="span"/>.
    /// </summary>
    public static void Progress(long xp, int level, out long into, out long span)
    {
        var floor = level <= FirstLevel ? 0L : ThresholdFor(level - 1);
        var ceiling = ThresholdFor(level);

        into = Math.Max(0L, xp - floor);
        span = Math.Max(1L, ceiling - floor);
    }

    /// <summary>0.0–1.0 fraction of the current level completed.</summary>
    public static double Fraction(long xp, int level)
    {
        Progress(xp, level, out var into, out var span);
        return Math.Clamp((double)into / span, 0d, 1d);
    }
}
