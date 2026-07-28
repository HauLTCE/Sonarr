using Microsoft.Extensions.DependencyInjection;
using Sonarr.Application.Utility;
using Sonarr.Bot.Discord.Levels;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Levels;

/// <summary>
/// The month boundary: which window a season covers, who placed where, and the roll itself.
/// The arithmetic is the interesting part — "XP earned this season" is derived from a lifetime
/// total, so the subtraction is what these tests are really about.
/// </summary>
public sealed class SeasonTests
{
    private const ulong Guild = 900;

    // ---- SeasonRules: the window ---------------------------------------------------------------

    [Fact]
    public void The_window_is_the_calendar_month()
    {
        (DateTimeOffset starts, DateTimeOffset ends) =
            SeasonRules.MonthWindow(new DateTimeOffset(2026, 7, 28, 13, 5, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), starts);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), ends);
    }

    /// <summary>December rolls into next January, not into month 13.</summary>
    [Fact]
    public void The_window_crosses_the_year()
    {
        (_, DateTimeOffset ends) =
            SeasonRules.MonthWindow(new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), ends);
    }

    /// <summary>The window carries the caller's offset, so a guild's month is its own.</summary>
    [Fact]
    public void The_window_keeps_the_guilds_offset()
    {
        (DateTimeOffset starts, _) =
            SeasonRules.MonthWindow(new DateTimeOffset(2026, 7, 28, 13, 0, 0, TimeSpan.FromHours(7)));

        Assert.Equal(TimeSpan.FromHours(7), starts.Offset);
    }

    [Fact]
    public void A_season_is_not_over_before_its_end()
    {
        DateTimeOffset ends = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.False(SeasonRules.IsOver(ends, ends.AddSeconds(-1)));
    }

    /// <summary>The boundary belongs to the next season: at 00:00 on the 1st, July is done.</summary>
    [Fact]
    public void A_season_is_over_on_the_boundary()
    {
        DateTimeOffset ends = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(SeasonRules.IsOver(ends, ends));
    }

    [Fact]
    public void The_label_names_the_month()
        => Assert.Equal(
            "July 2026",
            SeasonRules.Label(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

    // ---- SeasonRules: ranking ------------------------------------------------------------------

    /// <summary>
    /// The point of the whole design: XP is a lifetime total, so this season's is what earlier
    /// seasons have not already counted.
    /// </summary>
    [Fact]
    public void Earned_xp_is_the_total_minus_what_past_seasons_counted()
    {
        IReadOnlyList<SeasonResult> results = SeasonRules.Rank([new SeasonStanding(1, 5_000, 4_200)]);

        Assert.Equal(800, Assert.Single(results).XpEarned);
    }

    [Fact]
    public void Ranking_is_by_earned_not_by_total()
    {
        IReadOnlyList<SeasonResult> results = SeasonRules.Rank(
        [
            // Bigger lifetime total, but almost all of it belongs to past seasons.
            new SeasonStanding(1, 100_000, 99_900),
            new SeasonStanding(2, 5_000, 0),
        ]);

        Assert.Equal([2L, 1L], results.Select(r => r.UserId));
        Assert.Equal([1, 2], results.Select(r => r.Rank));
    }

    /// <summary>Nobody is stored at rank N with nothing earned — a result means taking part.</summary>
    [Fact]
    public void A_member_who_earned_nothing_is_left_out()
    {
        IReadOnlyList<SeasonResult> results = SeasonRules.Rank(
        [
            new SeasonStanding(1, 5_000, 5_000),
            new SeasonStanding(2, 5_000, 4_000),
        ]);

        Assert.Equal(2L, Assert.Single(results).UserId);
    }

    /// <summary>
    /// An admin XP reset leaves a total below what past seasons counted. That is a wipe, not a
    /// negative season.
    /// </summary>
    [Fact]
    public void A_total_below_what_past_seasons_counted_is_not_negative()
        => Assert.Empty(SeasonRules.Rank([new SeasonStanding(1, 100, 9_000)]));

    [Fact]
    public void A_tie_breaks_on_the_user_id()
    {
        IReadOnlyList<SeasonResult> results = SeasonRules.Rank(
            [new SeasonStanding(22, 500, 0), new SeasonStanding(11, 500, 0)]);

        Assert.Equal([11L, 22L], results.Select(r => r.UserId));
    }

    [Fact]
    public void A_quiet_month_ranks_nobody()
        => Assert.Empty(SeasonRules.Rank([]));

    // ---- SeasonService: the roll ---------------------------------------------------------------

    /// <summary>
    /// A guild that has never had a season gets the current month opened, and there is nothing to
    /// announce — no month has ended yet.
    /// </summary>
    [Fact]
    public async Task A_guild_with_no_season_gets_one_opened()
    {
        (ISeasonService service, FakeSeasonRepository seasons) = Harness();

        Assert.Null(await service.RollAsync(Guild));

        (long guildId, DateTimeOffset starts, DateTimeOffset ends) = Assert.Single(seasons.Opened);
        Assert.Equal((long)Guild, guildId);
        Assert.Equal(1, starts.Day);
        Assert.Equal(starts.AddMonths(1), ends);
    }

    /// <summary>The common case: a tick inside the month does nothing at all.</summary>
    [Fact]
    public async Task An_open_season_that_has_not_ended_is_left_alone()
    {
        FakeSeasonRepository seasons = new();
        seasons.Open((long)Guild, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        (ISeasonService service, _) = Harness(seasons);

        Assert.Null(await service.RollAsync(Guild));
        Assert.Empty(seasons.Opened);
    }

    [Fact]
    public async Task An_ended_season_is_closed_and_the_next_is_opened()
    {
        FakeSeasonRepository seasons = new();
        Season ended = seasons.Open(
            (long)Guild, DateTimeOffset.UtcNow.AddMonths(-2), DateTimeOffset.UtcNow.AddDays(-1));
        seasons.Standings.Add(new SeasonStanding(11, 900, 0));
        (ISeasonService service, _) = Harness(seasons);

        SeasonRoll? roll = await service.RollAsync(Guild);

        Assert.NotNull(roll);
        Assert.Equal(ended.SeasonId, roll.SeasonId);
        Assert.Equal(SeasonStatus.Closed, (await seasons.GetAsync(ended.SeasonId))!.Status);
        Assert.Single(seasons.Opened);
    }

    /// <summary>The podium is three, however many took part.</summary>
    [Fact]
    public async Task The_roll_reports_a_podium_and_the_full_count()
    {
        FakeSeasonRepository seasons = new();
        seasons.Open((long)Guild, DateTimeOffset.UtcNow.AddMonths(-2), DateTimeOffset.UtcNow.AddDays(-1));
        for (var i = 1; i <= 5; i++)
        {
            seasons.Standings.Add(new SeasonStanding(i, i * 100, 0));
        }

        (ISeasonService service, _) = Harness(seasons);

        SeasonRoll roll = (await service.RollAsync(Guild))!;

        Assert.Equal(5, roll.Participants);
        Assert.Equal(SeasonRules.TopCount, roll.Top.Count);
        Assert.Equal(5UL, roll.Top[0].UserId);
        Assert.Equal(500, roll.Top[0].Xp);
    }

    /// <summary>A month nobody spoke in still ends. The announcement just has nobody in it.</summary>
    [Fact]
    public async Task A_quiet_month_still_closes()
    {
        FakeSeasonRepository seasons = new();
        seasons.Open((long)Guild, DateTimeOffset.UtcNow.AddMonths(-2), DateTimeOffset.UtcNow.AddDays(-1));
        (ISeasonService service, _) = Harness(seasons);

        SeasonRoll roll = (await service.RollAsync(Guild))!;

        Assert.Empty(roll.Top);
        Assert.Equal(0, roll.Participants);
    }

    /// <summary>
    /// Two rollers racing: the loser's close writes nothing, so it announces nothing rather than
    /// posting a second set of standings.
    /// </summary>
    [Fact]
    public async Task A_season_closed_underneath_the_roller_is_not_announced()
    {
        FakeSeasonRepository seasons = new();
        Season ended = seasons.Open(
            (long)Guild, DateTimeOffset.UtcNow.AddMonths(-2), DateTimeOffset.UtcNow.AddDays(-1));
        (ISeasonService service, _) = Harness(seasons);

        // The other roller got there first.
        Assert.True(await seasons.CloseAsync(ended.SeasonId, []));
        seasons.Opened.Clear();

        // Our roller is still holding the season it read as active.
        Assert.Null(await service.RollAsync(Guild));
    }

    /// <summary>Rolling twice does not close the season it just opened.</summary>
    [Fact]
    public async Task Rolling_twice_in_the_same_month_is_a_no_op()
    {
        (ISeasonService service, FakeSeasonRepository seasons) = Harness();

        await service.RollAsync(Guild);
        Assert.Null(await service.RollAsync(Guild));
        Assert.Single(seasons.Opened);
    }

    // ---- Persona and the announcement ----------------------------------------------------------

    [Fact]
    public void The_close_has_a_line()
    {
        (ISeasonService service, _) = Harness();

        Assert.NotEmpty(service.Line(new SeasonRoll(1, "July 2026", 3, [])));
    }

    [Fact]
    public void The_line_is_stable_for_a_season()
    {
        (ISeasonService service, _) = Harness();
        SeasonRoll roll = new(7, "July 2026", 3, []);

        Assert.Equal(service.Line(roll), service.Line(roll));
    }

    [Fact]
    public void The_close_pool_is_in_the_shipped_persona()
    {
        Assert.True(Chat.Build.Graph.Pools.TryGetValue(SeasonRules.ClosePool, out PoolDef? def));
        Assert.NotEmpty(def!.Lines);
    }

    [Fact]
    public void The_podium_names_the_month_and_mentions_the_winners()
    {
        var embed = SeasonRoller.Embed(
            new SeasonRoll(1, "July 2026", 2, [new LeaderboardEntry(1, 11, 1_500, 0)]));

        Assert.Contains("July 2026", embed.Title, StringComparison.Ordinal);
        Assert.Contains("<@11>", embed.Description, StringComparison.Ordinal);
        Assert.Contains("1,500", embed.Description, StringComparison.Ordinal);
        Assert.Contains("2 members", embed.Footer!.Value.Text, StringComparison.Ordinal);
    }

    /// <summary>An empty podium must still render — the embed is posted either way.</summary>
    [Fact]
    public void An_empty_podium_says_so()
    {
        var embed = SeasonRoller.Embed(new SeasonRoll(1, "July 2026", 0, []));

        Assert.False(string.IsNullOrWhiteSpace(embed.Description));
    }

    private static (ISeasonService Service, FakeSeasonRepository Seasons) Harness(
        FakeSeasonRepository? seasons = null)
    {
        FakeSeasonRepository store = seasons ?? new FakeSeasonRepository();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ISeasonRepository>(store);
        services.AddSingleton<IMemberRepository>(new FakeMemberRepository());
        services.AddSingleton<IGuildConfigService>(new Moderation.FakeGuildConfigService());
        services.AddSingleton(Chat.Build.Persona());
        services.AddSonarrUtilityServices();

        return (services.BuildServiceProvider().GetRequiredService<ISeasonService>(), store);
    }
}
