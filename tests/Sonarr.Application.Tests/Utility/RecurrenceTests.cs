using Sonarr.Domain.Jobs;

namespace Sonarr.Application.Tests.Utility;

/// <summary>
/// Recurring reminders re-arm by asking <see cref="Recurrence.Next"/> for the following occurrence,
/// so this is the maths <c>/remind every day 9am</c> depends on for the rest of its life.
/// </summary>
/// <remarks>
/// Zones here are either UTC or built with <see cref="TimeZoneInfo.CreateCustomTimeZone"/>. No IANA
/// id is resolved, because ICU is stripped (<c>InvariantGlobalization=true</c>) and lookups only
/// work in the container.
/// </remarks>
public sealed class RecurrenceTests
{
    private static readonly DateTimeOffset Nine = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_non_recurring_row_has_no_next_occurrence()
    {
        Assert.Null(Recurrence.Next(null, Nine, Nine, TimeZoneInfo.Utc));
        Assert.Null(Recurrence.Next("fortnightly", Nine, Nine, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData(Recurrence.Hourly, 1, 0)]
    [InlineData(Recurrence.Daily, 0, 1)]
    [InlineData(Recurrence.Weekly, 0, 7)]
    public void Steps_one_period_past_the_previous_run(string recurrence, int hours, int days)
    {
        DateTimeOffset? next = Recurrence.Next(recurrence, Nine, Nine.AddSeconds(1), TimeZoneInfo.Utc);

        Assert.Equal(Nine.AddHours(hours).AddDays(days), next);
    }

    [Fact]
    public void Monthly_lands_on_the_same_day_next_month()
        => Assert.Equal(
            new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero),
            Recurrence.Next(Recurrence.Monthly, Nine, Nine.AddSeconds(1), TimeZoneInfo.Utc));

    [Fact]
    public void Monthly_clamps_the_thirty_first_into_a_shorter_month()
    {
        DateTimeOffset january31 = new(2026, 1, 31, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset? next = Recurrence.Next(
            Recurrence.Monthly, january31, january31.AddSeconds(1), TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 2, 28, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void A_missed_window_catches_up_to_the_first_occurrence_after_now()
    {
        // The bot was down for three days: the next daily run is tomorrow, not three runs ago.
        DateTimeOffset now = Nine.AddDays(3).AddMinutes(30);

        DateTimeOffset? next = Recurrence.Next(Recurrence.Daily, Nine, now, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero), next);
        Assert.True(next > now);
    }

    [Fact]
    public void A_daily_reminder_keeps_its_wall_clock_time_across_a_dst_shift()
    {
        // A zone that springs forward at 02:00 on the last Sunday in March, like Europe does. Built
        // by hand rather than looked up, so the assertion holds on Windows and in the container.
        TimeZoneInfo zone = SpringForwardZone();

        // 09:00 local on the Saturday before the shift.
        DateTimeOffset previous = new(2026, 3, 28, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset? next = Recurrence.Next(Recurrence.Daily, previous, previous.AddSeconds(1), zone);

        // Still 09:00 local the next day, even though the offset changed — so the instant moves by
        // 23 hours, not 24.
        Assert.Equal(9, TimeZoneInfo.ConvertTime(next!.Value, zone).Hour);
        Assert.Equal(TimeSpan.FromHours(23), next.Value - previous);
    }

    [Fact]
    public void Describe_gives_the_user_facing_phrase()
    {
        Assert.Equal("every hour", Recurrence.Describe(Recurrence.Hourly));
        Assert.Equal("every day", Recurrence.Describe(Recurrence.Daily));
        Assert.Equal("every week", Recurrence.Describe(Recurrence.Weekly));
        Assert.Equal("every month", Recurrence.Describe(Recurrence.Monthly));
        Assert.Equal("once", Recurrence.Describe(null));
    }

    [Theory]
    [InlineData("day", Recurrence.Daily)]
    [InlineData("daily", Recurrence.Daily)]
    [InlineData("hour", Recurrence.Hourly)]
    [InlineData("week", Recurrence.Weekly)]
    [InlineData("month", Recurrence.Monthly)]
    [InlineData("tuesday", Recurrence.Weekly)]
    [InlineData("sat", Recurrence.Weekly)]
    public void FromWords_maps_what_people_type(string word, string expected)
        => Assert.Equal(expected, Recurrence.FromWords(word));

    [Theory]
    [InlineData("fortnight")]
    [InlineData("")]
    [InlineData(null)]
    public void FromWords_returns_null_for_anything_else(string? word)
        => Assert.Null(Recurrence.FromWords(word));

    /// <summary>UTC+0 in winter, UTC+1 from the last Sunday in March — Europe's rule, hand-built.</summary>
    private static TimeZoneInfo SpringForwardZone()
    {
        TimeZoneInfo.TransitionTime start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 1, 0, 0), 3, 5, DayOfWeek.Sunday);
        TimeZoneInfo.TransitionTime end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 10, 5, DayOfWeek.Sunday);

        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);

        return TimeZoneInfo.CreateCustomTimeZone(
            "test/dst", TimeSpan.Zero, "test dst", "standard", "daylight", [rule]);
    }
}
