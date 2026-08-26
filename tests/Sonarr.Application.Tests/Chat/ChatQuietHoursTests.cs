using Sonarr.Application.Chat;
using Sonarr.Domain.Chat;
using Sonarr.Domain.Configuration;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// Sleep mode and the midday break — the Python bot's <c>SLEEP_MODE_ENABLED</c> and
/// <c>MIDDAY_BREAK_ENABLED</c>, restored as global config rather than env vars nobody could change
/// without a restart.
/// </summary>
/// <remarks>
/// <para>
/// Two things are being pinned. The <b>windows</b> are the legacy ones (22:00–06:00 and the 12:00
/// hour) read in the guild's zone, and the <b>polarity</b> is the legacy one: both off unless
/// somebody turns them on. The second is the easier of the two to break, because
/// <c>FeatureGate</c>'s other eleven names default the other way.
/// </para>
/// <para>
/// All times below are UTC with no configured zone, so they are also the local times — the zone
/// conversion has its own coverage in <see cref="ChatCalendarTests"/>, and a Windows dev box cannot
/// resolve an IANA id anyway (<c>InvariantGlobalization</c>).
/// </para>
/// </remarks>
public sealed class ChatQuietHoursTests
{
    private static DateTimeOffset At(int hour) => new(2026, 7, 27, hour, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Off_by_default_she_answers_at_three_in_the_morning()
    {
        // The whole polarity question in one assertion: no rows anywhere, deep inside the sleep
        // window, and she still talks. A restriction that defaulted on would silence this.
        ChatDecision decision = await Build.Pipeline(now: At(3)).HandleAsync(Build.Request("hello"));

        Assert.Null(decision.Skipped);
        Assert.NotNull(decision.Text);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task With_sleep_mode_on_she_says_nothing_between_ten_and_six(int hour)
    {
        ChatDecision decision = await Build
            .Pipeline(features: new FakeFeatureGate().On(FeatureNames.Sleep), now: At(hour))
            .HandleAsync(Build.Request("hello"));

        Assert.Equal(ChatDecision.SkipReasons.Asleep, decision.Skipped);
        Assert.Null(decision.Text);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(13)]
    [InlineData(21)]
    public async Task Sleep_mode_ends_at_six_and_does_not_start_before_ten(int hour)
    {
        // The window wraps midnight, so it is written as an `or` — and an `or` with the wrong
        // boundary swallows the whole day without failing anything else.
        ChatDecision decision = await Build
            .Pipeline(features: new FakeFeatureGate().On(FeatureNames.Sleep), now: At(hour))
            .HandleAsync(Build.Request("hello"));

        Assert.Null(decision.Skipped);
    }

    [Fact]
    public async Task With_the_midday_break_on_she_takes_the_twelve_oclock_hour_off()
    {
        ChatDecision decision = await Build
            .Pipeline(features: new FakeFeatureGate().On(FeatureNames.Midday), now: At(12))
            .HandleAsync(Build.Request("hello"));

        Assert.Equal(ChatDecision.SkipReasons.MiddayBreak, decision.Skipped);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    public async Task The_midday_break_is_one_hour_not_an_afternoon(int hour)
    {
        ChatDecision decision = await Build
            .Pipeline(features: new FakeFeatureGate().On(FeatureNames.Midday), now: At(hour))
            .HandleAsync(Build.Request("hello"));

        Assert.Null(decision.Skipped);
    }

    [Fact]
    public async Task Each_toggle_only_covers_its_own_window()
    {
        // Two flags, not one with two windows: turning on the lunch break must not make her sleep.
        FakeFeatureGate midday = new FakeFeatureGate().On(FeatureNames.Midday);

        Assert.Null((await Build.Pipeline(features: midday, now: At(3))
            .HandleAsync(Build.Request("hello"))).Skipped);

        FakeFeatureGate sleep = new FakeFeatureGate().On(FeatureNames.Sleep);

        Assert.Null((await Build.Pipeline(features: sleep, now: At(12))
            .HandleAsync(Build.Request("hello"))).Skipped);
    }

    /// <summary>
    /// Quiet hours are a gate, not a silent reply: nothing is written, so a night of mentions
    /// leaves her memory exactly as it was.
    /// </summary>
    [Fact]
    public async Task Being_asleep_costs_no_database_write()
    {
        FakePersonRepository people = new();

        ChatDecision decision = await Build
            .Pipeline(people, features: new FakeFeatureGate().On(FeatureNames.Sleep), now: At(2))
            .HandleAsync(Build.Request("hello"));

        Assert.Equal(ChatDecision.SkipReasons.Asleep, decision.Skipped);
        Assert.Empty(people.Writes);
    }

    /// <summary>
    /// One config read on the quiet path too. The gate and her calendar share the zone the turn
    /// already resolved, which is what <c>ChatCalendarTests</c> pins from the other direction.
    /// </summary>
    [Fact]
    public async Task Turning_the_toggles_on_does_not_add_a_config_read()
    {
        FakeChatConfig config = new(null);

        await Build
            .Pipeline(features: new FakeFeatureGate().On(FeatureNames.Sleep), now: At(2), config: config)
            .HandleAsync(Build.Request("hello"));

        Assert.Equal(1, config.ZoneReads);
    }

    /// <summary>
    /// An edit call-out is her talking, so it obeys the same windows. Without this she sleeps
    /// through mentions and then snarks at a stealth edit at three in the morning.
    /// </summary>
    [Fact]
    public async Task She_does_not_call_out_an_edit_while_asleep()
    {
        FakeSessionCache cache = new();
        await cache.MarkRepliedAsync(Build.Channel, Build.Message, "some-old-hash");

        ChatDecision decision = await Build
            .EditWatcher(cache, new FakeFeatureGate().On(FeatureNames.Sleep), At(2))
            .HandleAsync(new ChatEdit(Build.Guild, Build.Channel, Build.User, Build.Message, "edited"));

        Assert.Equal(ChatDecision.SkipReasons.Asleep, decision.Skipped);
    }

    [Fact]
    public async Task An_edit_outside_the_windows_is_still_called_out()
    {
        FakeSessionCache cache = new();
        await cache.MarkRepliedAsync(Build.Channel, Build.Message, "some-old-hash");

        ChatDecision decision = await Build
            .EditWatcher(cache, new FakeFeatureGate().On(FeatureNames.Sleep), At(15))
            .HandleAsync(new ChatEdit(Build.Guild, Build.Channel, Build.User, Build.Message, "edited"));

        Assert.Null(decision.Skipped);
        Assert.NotNull(decision.Text);
    }

    [Fact]
    public void The_windows_are_the_legacy_ones()
    {
        // Named constants rather than magic numbers in the pipeline, and these are the figures
        // _bot_legacy/utils/checks.py used (tag python-bot-final).
        Assert.Equal(22, QuietHours.SleepStartHour);
        Assert.Equal(6, QuietHours.SleepEndHour);
        Assert.Equal(12, QuietHours.MiddayHour);
    }

    [Fact]
    public void Both_toggles_are_restrictions_and_the_modules_are_not()
    {
        Assert.Contains(FeatureNames.Sleep, FeatureNames.Restrictions);
        Assert.Contains(FeatureNames.Midday, FeatureNames.Restrictions);
        Assert.DoesNotContain(FeatureNames.Chat, FeatureNames.Restrictions);
    }
}
