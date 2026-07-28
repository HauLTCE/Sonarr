using Sonarr.Application.Chat;
using Sonarr.Domain.Configuration;
using Sonarr.Elaine.Conversation;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// Mood of the day and seasonal overlays: derived from the turn's own instant in the guild's
/// zone, not reseeded on a schedule.
/// </summary>
/// <remarks>
/// docs/08 lists "mood reseed, overlay check" as <c>DailyTick</c> duties, which is one way to
/// build it and the worse one: a scheduled reseed makes the reply depend on when the tick last
/// ran, so a stored turn cannot be replayed and a missed tick leaves her in yesterday. Deriving
/// both from <c>now</c> is the same behaviour with no service, no state and no failure mode —
/// so these tests pin the behaviour docs/08 asked for, at the seam that actually produces it.
/// <see cref="ClockSignals"/> is where the calendar is read; the pipeline is the only thing
/// that knows what time it is.
/// </remarks>
public sealed class ChatCalendarTests
{
    /// <summary>UTC+7 — the server's actual zone, and the reason this bug was worth fixing.</summary>
    private const string Saigon = "Asia/Ho_Chi_Minh";

    /// <summary>
    /// False on a box with no tz database. <c>InvariantGlobalization</c> strips ICU, so IANA ids
    /// only resolve where the OS ships zoneinfo — the container, not a Windows dev box (see
    /// <c>ZoneResolver</c>). The zone-specific cases assert the UTC fallback there instead.
    /// </summary>
    private static readonly bool HasTzData = TimeZoneInfo.TryFindSystemTimeZoneById(Saigon, out _);

    [Fact]
    public void The_same_person_asking_the_same_thing_hears_a_different_line_tomorrow()
    {
        // The mood of the day is the day seed, and it is what stops her having one fixed answer
        // per person forever. Nine days rather than two: a collision on one pair proves nothing,
        // and a seed that ignored the date would fail this outright.
        ulong[] seeds = [.. Enumerable.Range(1, 9).Select(d =>
            ClockSignals.From(Build.Graph, Build.Now.AddDays(d), null).DaySeed)];

        Assert.Equal(seeds.Length, seeds.Distinct().Count());
    }

    [Fact]
    public void The_mood_holds_for_the_whole_day_and_turns_over_at_midnight()
    {
        DateTimeOffset midnight = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

        ulong Seed(DateTimeOffset at) => ClockSignals.From(Build.Graph, at, null).DaySeed;

        // A mood that changed mid-conversation would not be a mood.
        Assert.Equal(Seed(midnight), Seed(midnight.AddHours(23)));
        Assert.NotEqual(Seed(midnight), Seed(midnight.AddHours(24)));
    }

    [Fact]
    public void October_activates_the_seasonal_overlay_without_anything_scheduling_it()
    {
        DateTimeOffset october = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        Assert.Contains("october", ClockSignals.From(Build.Graph, october, null).Overlays);
        Assert.DoesNotContain(
            "october",
            ClockSignals.From(Build.Graph, october.AddMonths(1), null).Overlays);
    }

    [Fact]
    public void At_4am_on_halloween_both_overlays_are_live_and_the_stronger_one_leads()
    {
        // persona/overlays/*.yaml both claim mood_fragment; latenight has the higher priority, so
        // it must come first — LinePicker walks this list in order and takes the first claim.
        IReadOnlyList<string> overlays = ClockSignals
            .From(Build.Graph, new DateTimeOffset(2026, 10, 31, 4, 0, 0, TimeSpan.Zero), null)
            .Overlays;

        Assert.Equal(["latenight", "october"], overlays);
    }

    [Fact]
    public async Task Three_am_for_the_room_is_three_am_for_her()
    {
        // 20:00 UTC is 03:00 the next day in Saigon, so the latenight overlay replaces
        // social_greeting with topic_nap. Under the old ToLocalTime() this depended on the
        // container's TZ, and a UTC host handed out the daytime pool at 3 am local.
        DateTimeOffset eveningUtc = new(2026, 7, 27, 20, 0, 0, TimeSpan.Zero);

        ChatDecision decision = await Build
            .Pipeline(now: eveningUtc, config: new FakeChatConfig(Saigon))
            .HandleAsync(Build.Request("hello"));

        Assert.Equal(HasTzData, Naps(decision));
    }

    [Fact]
    public async Task Without_a_configured_zone_she_runs_on_utc_rather_than_the_container()
    {
        // The fallback has to be a fixed zone, not the host's: two replicas in different regions
        // must answer the same, and a box that cannot resolve IANA ids only ever gets this one.
        DateTimeOffset threeAmUtc = new(2026, 7, 27, 3, 0, 0, TimeSpan.Zero);

        ChatDecision decision = await Build.Pipeline(now: threeAmUtc).HandleAsync(Build.Request("hello"));

        Assert.True(Naps(decision), $"3 am UTC should be late night: {decision.Text}");
    }

    [Fact]
    public async Task A_nonsense_zone_costs_her_nothing()
    {
        // Config is validated on write, so this needs a hand-edited row or an id the platform
        // dropped. Either way an unresolvable zone must degrade to UTC, not throw mid-turn.
        ChatDecision decision = await Build
            .Pipeline(config: new FakeChatConfig("Mars/Olympus_Mons"))
            .HandleAsync(Build.Request("hello"));

        Assert.NotNull(decision.Text);
    }

    [Fact]
    public async Task Her_calendar_costs_one_config_read_a_turn()
    {
        // Guild config is Redis-cached behind IGuildConfigService, so the read is cheap — but it
        // is on the reply path, so cheap has to mean once a turn and not once per lookup.
        FakeChatConfig config = new(Saigon);

        await Build.Pipeline(config: config).HandleAsync(Build.Request("hello"));

        Assert.Equal(1, config.ZoneReads);
    }

    [Fact]
    public async Task An_ambient_message_never_asks_what_time_it_is()
    {
        // The zone read sits after the gates (docs/10 order): most messages in a busy channel are
        // not addressed to her, and none of them should cost a lookup.
        FakeChatConfig config = new(Saigon);

        ChatDecision decision = await Build.Pipeline(config: config)
            .HandleAsync(Build.Request("just talking", addressed: false));

        Assert.Equal(ChatDecision.SkipReasons.NotAddressed, decision.Skipped);
        Assert.Equal(0, config.ZoneReads);
    }

    [Fact]
    public void The_timezone_key_she_reads_is_the_one_admins_can_set()
    {
        // If the catalog renamed this key, the pipeline would read null forever and every guild
        // would quietly move to UTC with nothing failing.
        Assert.Contains(ConfigKeys.All, k => k.Key == ConfigKeys.Timezone);
    }

    /// <summary>
    /// Contains rather than equals: <see cref="ReplyComposer"/> prepends a mood fragment on one
    /// turn in three ("it's 4am." + the nap line), and which turn that is depends on the day seed
    /// — so the Saigon case and the UTC case do not agree on whether the reply is bare. An
    /// equality check passed on a Windows box only because no tz data makes both sides false.
    /// </summary>
    private static bool Naps(ChatDecision decision) =>
        decision.Text is { } text
        && Build.Graph.Pools["topic_nap"].Lines.Any(l => text.Contains(l, StringComparison.Ordinal));
}
