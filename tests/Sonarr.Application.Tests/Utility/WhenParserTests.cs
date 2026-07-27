using Sonarr.Domain.Jobs;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Tests.Utility;

/// <summary>
/// The <c>when</c> parser behind <c>/remind</c>, <c>/announce</c> and <c>/timestamp</c>.
/// </summary>
/// <remarks>
/// Every case runs against <see cref="TimeZoneInfo.Utc"/> and a fixed <c>now</c>. Nothing here
/// resolves an IANA id: <c>InvariantGlobalization=true</c> strips ICU, so
/// <c>FindSystemTimeZoneById("Europe/London")</c> fails on a Windows dev box and succeeds in the
/// container. A test that depended on it would only pass on one of the two.
/// </remarks>
public sealed class WhenParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("10m", 10)]
    [InlineData("in 10m", 10)]
    [InlineData("10 minutes", 10)]
    [InlineData("90 seconds", 1.5)]
    [InlineData("2h", 120)]
    [InlineData("2h30m", 150)]
    [InlineData("2 h 30 m", 150)]
    [InlineData("1d", 1440)]
    [InlineData("3 days", 4320)]
    [InlineData("1w", 10080)]
    public void Reads_a_duration_as_an_offset_from_now(string when, double minutes)
    {
        Assert.True(WhenParser.TryParse(when, Now, TimeZoneInfo.Utc, out WhenResult? result, out var error), error);
        Assert.Equal(Now.AddMinutes(minutes), result!.RunAt);
        Assert.Null(result.Recurrence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("soon")]
    [InlineData("later today maybe")]
    [InlineData("10")]
    [InlineData("m")]
    [InlineData("10 furlongs")]
    [InlineData("25:00")]
    [InlineData("13am")]
    [InlineData("9:99")]
    public void Rejects_what_it_cannot_read_with_a_sentence_for_the_user(string when)
    {
        Assert.False(WhenParser.TryParse(when, Now, TimeZoneInfo.Utc, out WhenResult? result, out var error));
        Assert.Null(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Rejects_a_time_that_has_already_gone_by()
    {
        Assert.False(WhenParser.TryParse("2020-01-01 09:00", Now, TimeZoneInfo.Utc, out _, out var error));
        Assert.Contains("gone by", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_horizon_past_two_years_instead_of_overflowing()
    {
        // 999999999w would throw out of TimeSpan arithmetic if the guard were missing.
        Assert.False(WhenParser.TryParse("999999999w", Now, TimeZoneInfo.Utc, out _, out var error));
        Assert.Contains("two years", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clock_time_still_to_come_today_stays_today()
    {
        Assert.True(WhenParser.TryParse("9pm", Now, TimeZoneInfo.Utc, out WhenResult? result, out var error), error);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 21, 0, 0, TimeSpan.Zero), result!.RunAt);
    }

    [Fact]
    public void A_clock_time_already_past_today_rolls_to_tomorrow()
    {
        Assert.True(WhenParser.TryParse("9am", Now, TimeZoneInfo.Utc, out WhenResult? result, out var error), error);
        Assert.Equal(new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero), result!.RunAt);
    }

    [Theory]
    [InlineData("tomorrow 8am", 2026, 7, 28, 8, 0)]
    [InlineData("tomorrow", 2026, 7, 28, 9, 0)]
    [InlineData("tonight", 2026, 7, 27, 20, 0)]
    [InlineData("21:30", 2026, 7, 27, 21, 30)]
    [InlineData("at 21:30", 2026, 7, 27, 21, 30)]
    [InlineData("2026-08-01 18:00", 2026, 8, 1, 18, 0)]
    [InlineData("2026-08-01", 2026, 8, 1, 0, 0)]
    public void Reads_the_clock_and_calendar_shapes(
        string when, int year, int month, int day, int hour, int minute)
    {
        Assert.True(WhenParser.TryParse(when, Now, TimeZoneInfo.Utc, out WhenResult? result, out var error), error);
        Assert.Equal(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero), result!.RunAt);
    }

    [Fact]
    public void A_weekday_name_means_the_next_one_not_today()
    {
        // Now is a Monday; "monday" must mean the Monday after, never five minutes ago.
        Assert.True(WhenParser.TryParse("monday 9am", Now, TimeZoneInfo.Utc, out WhenResult? result, out var error), error);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero), result!.RunAt);
    }

    [Theory]
    [InlineData("every hour", Recurrence.Hourly)]
    [InlineData("every day 9am", Recurrence.Daily)]
    [InlineData("every daily", Recurrence.Daily)]
    [InlineData("every week 09:00", Recurrence.Weekly)]
    [InlineData("every month 09:00", Recurrence.Monthly)]
    [InlineData("every monday 09:00", Recurrence.Weekly)]
    [InlineData("every fri 18:00", Recurrence.Weekly)]
    public void Reads_a_repeat_into_the_recurrence_vocabulary(string when, string expected)
    {
        Assert.True(WhenParser.TryParse(when, Now, TimeZoneInfo.Utc, out WhenResult? result, out var error), error);
        Assert.Equal(expected, result!.Recurrence);
        Assert.True(result.RunAt > Now, "the first occurrence is in the future");
    }

    [Fact]
    public void A_repeat_whose_time_today_has_passed_starts_at_the_next_period()
    {
        Assert.True(
            WhenParser.TryParse("every day 9am", Now, TimeZoneInfo.Utc, out WhenResult? result, out var error), error);

        Assert.Equal(new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero), result!.RunAt);
    }

    [Fact]
    public void An_unknown_repeat_unit_is_rejected_rather_than_silently_daily()
    {
        Assert.False(WhenParser.TryParse("every fortnight", Now, TimeZoneInfo.Utc, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Parsing_is_deterministic_for_a_given_now_and_zone()
    {
        // The determinism boundary from docs/02: now and the zone are arguments, never ambient.
        Assert.True(WhenParser.TryParse("2h", Now, TimeZoneInfo.Utc, out WhenResult? first, out _));
        Assert.True(WhenParser.TryParse("2h", Now, TimeZoneInfo.Utc, out WhenResult? second, out _));
        Assert.Equal(first!.RunAt, second!.RunAt);
    }

    [Fact]
    public void A_fixed_offset_zone_shifts_the_wall_clock_answer()
    {
        // A custom zone, not a lookup: this is how the parser's zone handling is tested without ICU.
        TimeZoneInfo plusSeven = TimeZoneInfo.CreateCustomTimeZone("test/+7", TimeSpan.FromHours(7), "+7", "+7");

        Assert.True(WhenParser.TryParse("9pm", Now, plusSeven, out WhenResult? result, out var error), error);

        // 21:00 at +07:00 on the local day (2026-07-27 17:00 local) is 14:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.Zero), result!.RunAt.ToUniversalTime());
    }
}
