namespace Sonarr.Domain.Levels;

/// <summary>
/// Streak and first-message-of-day rules. Pure functions over the day boundary the caller
/// resolved (the guild's configured timezone), so a day rollover is testable without a clock.
/// </summary>
public static class StreakRules
{
    /// <summary>Result of touching a streak for <c>today</c>.</summary>
    /// <param name="Days">The streak length after the touch.</param>
    /// <param name="Changed">
    /// <c>true</c> when this touch was the first of <c>today</c> — i.e. the day rolled over,
    /// which is also what gates the first-message bonus.
    /// </param>
    public readonly record struct StreakTouch(int Days, bool Changed);

    /// <summary>
    /// Day-touch: same day is a no-op, yesterday extends, anything older restarts at 1.
    /// </summary>
    public static StreakTouch Touch(int currentDays, DateOnly? lastDay, DateOnly today)
    {
        if (lastDay == today)
        {
            // Already counted today. Keep the stored value even if it is 0 from a legacy row —
            // it gets fixed on the next rollover, and inflating it here would fake a day.
            return new StreakTouch(currentDays, Changed: false);
        }

        var extends = lastDay == today.AddDays(-1);
        return new StreakTouch(extends ? Math.Max(currentDays, 1) + 1 : 1, Changed: true);
    }

    /// <summary>Once-per-day bonus: granted when the stored bonus day is not today.</summary>
    public static bool FirstMessageBonusDue(DateOnly? bonusDay, DateOnly today) => bonusDay != today;

    /// <summary>
    /// The streak as it stands <em>now</em>, for display. The stored number is only true as of
    /// <paramref name="lastDay"/>: someone who last spoke three days ago has a broken streak that
    /// nothing has written yet.
    /// </summary>
    /// <remarks>
    /// Derived on read rather than swept nightly, deliberately. A sweep would be one UPDATE per
    /// stale row per guild per night to produce a number this computes for free, and it would be
    /// wrong between midnight and whenever the sweep ran — worse, a failed sweep leaves an inflated
    /// streak on someone's card. This is also why <c>DailyTick</c> has no streak duty.
    /// </remarks>
    public static int CurrentDays(int storedDays, DateOnly? lastDay, DateOnly today)
        => lastDay is { } day && (day == today || day == today.AddDays(-1))
            ? Math.Max(storedDays, 0)
            : 0;
}
