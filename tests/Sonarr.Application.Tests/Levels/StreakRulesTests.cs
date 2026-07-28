using Sonarr.Bot.Modules;
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

    /// <summary>
    /// The display side. Nothing rewrites the row when a day passes in silence, so a stored 12 is
    /// only true while the last day is today or yesterday — the streak breaks on read, which is why
    /// there is no nightly sweep.
    /// </summary>
    [Fact]
    public void A_streak_touched_today_still_counts()
        => Assert.Equal(12, StreakRules.CurrentDays(12, Today, Today));

    /// <summary>Yesterday is still alive: today has not been missed until it ends.</summary>
    [Fact]
    public void A_streak_touched_yesterday_still_counts()
        => Assert.Equal(12, StreakRules.CurrentDays(12, Today.AddDays(-1), Today));

    [Theory]
    [InlineData(-2)]
    [InlineData(-3)]
    [InlineData(-400)]
    public void A_streak_last_touched_before_yesterday_is_gone(int offset)
        => Assert.Equal(0, StreakRules.CurrentDays(12, Today.AddDays(offset), Today));

    [Fact]
    public void A_member_who_never_spoke_has_no_streak()
        => Assert.Equal(0, StreakRules.CurrentDays(0, null, Today));

    /// <summary>A legacy row with a negative count reads as none, not as a negative streak.</summary>
    [Fact]
    public void A_nonsense_stored_count_reads_as_none()
        => Assert.Equal(0, StreakRules.CurrentDays(-5, Today, Today));

    /// <summary>What <c>/rank</c> prints. Zero is a state, not a count.</summary>
    [Theory]
    [InlineData(0, "none")]
    [InlineData(1, "1 day")]
    [InlineData(9, "9 days")]
    public void The_rank_card_words_the_streak(int days, string expected)
        => Assert.Equal(expected, LevelsModule.Streak(days));
}
