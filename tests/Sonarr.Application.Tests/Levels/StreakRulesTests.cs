using Sonarr.Domain.Levels;

namespace Sonarr.Application.Tests.Levels;

/// <summary>
/// Day rollover: a streak extends on consecutive days, resets after a gap, and a second message
/// the same day changes nothing. The date comes from the caller (the guild's clock), so these are
/// pure calendar rules with no <c>DateTime.Now</c> anywhere.
/// </summary>
public sealed class StreakRulesTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    [Fact]
    public void First_ever_message_starts_the_streak_at_one()
    {
        StreakRules.StreakTouch touch = StreakRules.Touch(0, null, Today);

        Assert.Equal(1, touch.Days);
        Assert.True(touch.Changed);
    }

    [Fact]
    public void Second_message_the_same_day_changes_nothing()
    {
        StreakRules.StreakTouch touch = StreakRules.Touch(4, Today, Today);

        Assert.Equal(4, touch.Days);
        Assert.False(touch.Changed);
    }

    [Fact]
    public void Yesterday_extends_the_streak()
    {
        StreakRules.StreakTouch touch = StreakRules.Touch(4, Today.AddDays(-1), Today);

        Assert.Equal(5, touch.Days);
        Assert.True(touch.Changed);
    }

    [Fact]
    public void A_skipped_day_resets_the_streak_to_one()
    {
        StreakRules.StreakTouch touch = StreakRules.Touch(30, Today.AddDays(-2), Today);

        Assert.Equal(1, touch.Days);
        Assert.True(touch.Changed);
    }

    [Fact]
    public void A_month_long_gap_also_resets_to_one()
        => Assert.Equal(1, StreakRules.Touch(30, Today.AddDays(-45), Today).Days);

    [Fact]
    public void Extending_across_a_month_boundary_still_counts()
    {
        DateOnly first = new(2026, 8, 1);
        StreakRules.StreakTouch touch = StreakRules.Touch(3, new DateOnly(2026, 7, 31), first);

        Assert.Equal(4, touch.Days);
    }

    [Fact]
    public void Fourteen_consecutive_days_count_fourteen()
    {
        var days = 0;
        DateOnly? last = null;

        for (var i = 0; i < 14; i++)
        {
            DateOnly day = Today.AddDays(i);

            // Two messages on the same day, to prove the second one is a no-op.
            days = StreakRules.Touch(days, last, day).Days;
            days = StreakRules.Touch(days, day, day).Days;
            last = day;
        }

        Assert.Equal(14, days);
    }

    [Fact]
    public void First_message_bonus_is_due_once_a_day()
    {
        Assert.True(StreakRules.FirstMessageBonusDue(null, Today));
        Assert.True(StreakRules.FirstMessageBonusDue(Today.AddDays(-1), Today));
        Assert.False(StreakRules.FirstMessageBonusDue(Today, Today));
    }
}
