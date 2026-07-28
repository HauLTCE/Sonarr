using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Application.Tests.Chat;
using Sonarr.Application.Tests.Levels;
using Sonarr.Application.Utility;
using Sonarr.Bot.Discord.Utility;
using Sonarr.Bot.Modules;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Utility;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Social;

/// <summary>
/// <c>/birthday</c>, <c>/anniversary</c> and the day's list the announcer reads. The date
/// arithmetic is the whole feature, so most of this is <see cref="MilestoneRules"/>: leap days,
/// missing years, and the year-zero case that would otherwise wish somebody a happy 0th.
/// </summary>
public sealed class MilestoneTests
{
    private const ulong Guild = 700;

    private const ulong User = 701;

    private static readonly DateOnly Today = new(2026, 7, 28);

    // ---- MilestoneRules: building a stored birthday --------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(-1, 5)]
    public void An_impossible_month_is_refused(int month, int day)
    {
        Assert.False(MilestoneRules.TryBuildBirthday(month, day, null, Today, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(2, 30)]
    [InlineData(4, 31)]
    [InlineData(1, 0)]
    public void An_impossible_day_is_refused(int month, int day)
        => Assert.False(MilestoneRules.TryBuildBirthday(month, day, null, Today, out _, out _));

    /// <summary>
    /// Without a year there is no calendar to check against, so 29 February has to be storable —
    /// otherwise a leap-day birthday is unrepresentable.
    /// </summary>
    [Fact]
    public void A_leap_day_is_storable_without_a_year()
    {
        Assert.True(MilestoneRules.TryBuildBirthday(2, 29, null, Today, out DateOnly stored, out _));
        Assert.Equal(2, stored.Month);
        Assert.Equal(29, stored.Day);
        Assert.False(MilestoneRules.HasYear(stored));
    }

    /// <summary>With a year given, the day is checked against that year's calendar.</summary>
    [Fact]
    public void A_leap_day_in_a_common_year_is_refused()
        => Assert.False(MilestoneRules.TryBuildBirthday(2, 29, 2001, Today, out _, out _));

    [Fact]
    public void A_real_year_is_kept()
    {
        Assert.True(MilestoneRules.TryBuildBirthday(3, 14, 1999, Today, out DateOnly stored, out _));
        Assert.Equal(new DateOnly(1999, 3, 14), stored);
        Assert.True(MilestoneRules.HasYear(stored));
        Assert.Equal(27, MilestoneRules.AgeOn(stored, Today));
    }

    /// <summary>A stored month+day has no age to report, ever.</summary>
    [Fact]
    public void A_yearless_birthday_has_no_age()
    {
        Assert.True(MilestoneRules.TryBuildBirthday(3, 14, null, Today, out DateOnly stored, out _));
        Assert.Null(MilestoneRules.AgeOn(stored, Today));
    }

    [Theory]
    [InlineData(2020)] // too young
    [InlineData(1850)] // too old
    [InlineData(2030)] // not yet born
    public void An_implausible_birth_year_is_refused(int year)
        => Assert.False(MilestoneRules.TryBuildBirthday(7, 1, year, Today, out _, out _));

    // ---- MilestoneRules: years and next occurrence ----------------------------------------------

    [Fact]
    public void Years_do_not_count_a_date_that_has_not_come_round_yet()
    {
        // Joined in December; by July only the earlier Decembers count.
        Assert.Equal(1, MilestoneRules.YearsSince(new DateOnly(2024, 12, 25), Today));
        Assert.Equal(2, MilestoneRules.YearsSince(new DateOnly(2024, 7, 28), new DateOnly(2026, 7, 28)));
    }

    [Fact]
    public void Years_are_never_negative()
        => Assert.Equal(0, MilestoneRules.YearsSince(new DateOnly(2030, 1, 1), Today));

    [Fact]
    public void Today_is_its_own_next_occurrence()
        => Assert.Equal(Today, MilestoneRules.NextOccurrence(7, 28, Today));

    [Fact]
    public void A_date_already_past_rolls_to_next_year()
        => Assert.Equal(new DateOnly(2027, 1, 5), MilestoneRules.NextOccurrence(1, 5, Today));

    /// <summary>
    /// The alternative to observing 29 February on 1 March is skipping it three years in four.
    /// </summary>
    [Fact]
    public void A_leap_day_is_observed_on_the_first_of_march_in_a_common_year()
    {
        Assert.Equal(new DateOnly(2027, 3, 1), MilestoneRules.NextOccurrence(2, 29, new DateOnly(2027, 2, 1)));
        Assert.Equal(new DateOnly(2028, 2, 29), MilestoneRules.NextOccurrence(2, 29, new DateOnly(2028, 2, 1)));
    }

    [Fact]
    public void A_leap_day_birthday_falls_on_the_first_of_march_in_a_common_year()
    {
        Assert.True(MilestoneRules.FallsOn(new DateOnly(2000, 2, 29), new DateOnly(2027, 3, 1)));
        Assert.False(MilestoneRules.FallsOn(new DateOnly(2000, 2, 29), new DateOnly(2028, 3, 1)));
        Assert.True(MilestoneRules.FallsOn(new DateOnly(2000, 2, 29), new DateOnly(2028, 2, 29)));
    }

    [Theory]
    [InlineData(2027, 3, 1, true)]  // common year — 29 Feb has nowhere else to go
    [InlineData(2028, 3, 1, false)] // leap year — it already happened yesterday
    [InlineData(2027, 3, 2, false)]
    public void The_leap_day_stand_in_is_only_the_first_of_march(int year, int month, int day, bool expected)
        => Assert.Equal(expected, MilestoneRules.ObservesLeapDay(new DateOnly(year, month, day)));

    // ---- The service ----------------------------------------------------------------------------

    [Fact]
    public async Task Setting_a_birthday_stores_the_month_and_day()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        members.Seed((long)Guild, (long)User, new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero));

        BirthdayResult result = await service.SetBirthdayAsync(Guild, User, 3, 14, null);

        Assert.True(result.Success);
        Assert.Contains("14 March", result.Message, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(MilestoneRules.UnknownYear, 3, 14), (await Row(members)).Birthday);
    }

    /// <summary>The year is stored when given, and only then does she mention an age.</summary>
    [Fact]
    public async Task A_given_year_is_stored_and_acknowledged()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        members.Seed((long)Guild, (long)User, DateTimeOffset.UtcNow);

        BirthdayResult result = await service.SetBirthdayAsync(Guild, User, 3, 14, 1999);

        Assert.True(result.Success);
        Assert.Equal(1999, (await Row(members)).Birthday!.Value.Year);
    }

    [Fact]
    public async Task A_refused_birthday_writes_nothing()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        members.Seed((long)Guild, (long)User, DateTimeOffset.UtcNow);

        BirthdayResult result = await service.SetBirthdayAsync(Guild, User, 2, 30, null);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
        Assert.Null((await Row(members)).Birthday);
    }

    /// <summary>docs/06: what a user volunteers, a user can take back.</summary>
    [Fact]
    public async Task Clearing_a_birthday_forgets_it()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        members.Seed((long)Guild, (long)User, DateTimeOffset.UtcNow, new DateOnly(1999, 3, 14));

        Assert.True((await service.ClearBirthdayAsync(Guild, User)).Success);
        Assert.Null((await Row(members)).Birthday);
    }

    [Fact]
    public async Task Clearing_a_birthday_nobody_set_still_succeeds()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();

        Assert.True((await service.ClearBirthdayAsync(Guild, User)).Success);
    }

    [Fact]
    public async Task An_unknown_member_has_no_anniversary()
    {
        (IMilestoneService service, _) = Harness();

        AnniversaryCard card = await service.GetAnniversaryAsync(Guild, User);

        Assert.Null(card.FirstSeenAt);
        Assert.Equal(0, card.Years);
    }

    [Fact]
    public async Task The_anniversary_counts_from_first_seen()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        DateTimeOffset joined = DateTimeOffset.UtcNow.AddYears(-3).AddDays(-10);
        members.Seed((long)Guild, (long)User, joined);

        AnniversaryCard card = await service.GetAnniversaryAsync(Guild, User);

        Assert.Equal(joined, card.FirstSeenAt);
        Assert.Equal(3, card.Years);
        Assert.NotNull(card.NextOn);
        Assert.InRange(card.DaysUntilNext, 0, 366);
    }

    [Fact]
    public async Task Todays_birthdays_are_listed()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        members.Seed((long)Guild, (long)User, DateTimeOffset.UtcNow.AddYears(-2),
            new DateOnly(1990, today.Month, today.Day));

        IReadOnlyList<Milestone> due = await service.TodayAsync(Guild);

        Milestone birthday = Assert.Single(due, m => m.Kind == MilestoneKind.Birthday);
        Assert.Equal(User, birthday.UserId);
        Assert.Equal(MilestoneRules.YearsSince(new DateOnly(1990, today.Month, today.Day), today), birthday.Years);
    }

    /// <summary>
    /// Year zero is the join itself. "Happy 0th" on somebody's first day is a bug, and
    /// <c>WelcomeFlow</c> already said hello.
    /// </summary>
    [Fact]
    public async Task Someone_who_joined_today_gets_no_anniversary_announcement()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        members.Seed((long)Guild, (long)User, DateTimeOffset.UtcNow);

        IReadOnlyList<Milestone> due = await service.TodayAsync(Guild);

        Assert.DoesNotContain(due, m => m.Kind == MilestoneKind.Anniversary);
    }

    [Fact]
    public async Task An_older_join_on_todays_date_is_announced()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        members.Seed((long)Guild, (long)User, DateTimeOffset.UtcNow.AddYears(-2));

        IReadOnlyList<Milestone> due = await service.TodayAsync(Guild);

        Milestone anniversary = Assert.Single(due, m => m.Kind == MilestoneKind.Anniversary);
        Assert.Equal(2, anniversary.Years);
    }

    /// <summary>Another guild's rows are another guild's business.</summary>
    [Fact]
    public async Task Another_guilds_milestones_are_not_listed()
    {
        (IMilestoneService service, FakeMemberRepository members) = Harness();
        members.Seed((long)Guild + 1, (long)User, DateTimeOffset.UtcNow.AddYears(-2));

        Assert.Empty(await service.TodayAsync(Guild));
    }

    [Fact]
    public async Task A_quiet_day_lists_nothing()
    {
        (IMilestoneService service, _) = Harness();

        Assert.Empty(await service.TodayAsync(Guild));
    }

    // ---- Persona -------------------------------------------------------------------------------

    [Theory]
    [InlineData(MilestoneKind.Birthday)]
    [InlineData(MilestoneKind.Anniversary)]
    public void Every_milestone_has_an_authored_line(MilestoneKind kind)
    {
        (IMilestoneService service, _) = Harness();

        Assert.NotEmpty(service.Line(new Milestone(kind, User, 2)));
    }

    /// <summary>Stable within a day, so a retried post reads the same.</summary>
    [Fact]
    public void The_line_is_stable_for_a_person_and_a_year()
    {
        (IMilestoneService service, _) = Harness();
        Milestone milestone = new(MilestoneKind.Birthday, User, 30);

        Assert.Equal(service.Line(milestone), service.Line(milestone));
    }

    [Theory]
    [InlineData(MilestoneRules.BirthdayPool)]
    [InlineData(MilestoneRules.AnniversaryPool)]
    public void The_pool_is_in_the_shipped_persona(string pool)
    {
        Assert.True(Chat.Build.Graph.Pools.TryGetValue(pool, out PoolDef? def), pool);
        Assert.NotEmpty(def!.Lines);
    }

    // ---- The announcer's message shape ----------------------------------------------------------

    /// <summary>
    /// Birthdays before anniversaries, and each kind is its own message — one mixed message would
    /// have to pick one of her lines for both.
    /// </summary>
    [Fact]
    public void The_announcer_groups_birthdays_before_anniversaries()
    {
        List<Milestone> due =
        [
            new(MilestoneKind.Anniversary, 1, 2),
            new(MilestoneKind.Birthday, 2, 30),
        ];

        (MilestoneKind Kind, IReadOnlyList<Milestone> Group)[] groups = [.. DailyTick.Group(due)];

        Assert.Equal(2, groups.Length);
        Assert.Equal(MilestoneKind.Birthday, groups[0].Kind);
        Assert.Equal(MilestoneKind.Anniversary, groups[1].Kind);
    }

    [Fact]
    public void The_announcer_skips_a_kind_with_nobody_in_it()
    {
        (MilestoneKind Kind, IReadOnlyList<Milestone> Group)[] groups =
            [.. DailyTick.Group([new Milestone(MilestoneKind.Birthday, 1, 30)])];

        Assert.Equal(MilestoneKind.Birthday, Assert.Single(groups).Kind);
    }

    /// <summary>Everyone is mentioned once, and only her authored line follows.</summary>
    [Fact]
    public void The_greeting_mentions_everyone_in_the_batch()
    {
        var text = DailyTick.Compose(
            MilestoneKind.Birthday,
            [new Milestone(MilestoneKind.Birthday, 11, 30), new Milestone(MilestoneKind.Birthday, 22, 41)],
            "many happy returns");

        Assert.Contains("<@11>", text, StringComparison.Ordinal);
        Assert.Contains("<@22>", text, StringComparison.Ordinal);
        Assert.Contains("many happy returns", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The year count is only shown when the whole batch shares it — "(2y)" over a batch of two
    /// people on their 2nd and 5th would be a lie about one of them.
    /// </summary>
    [Fact]
    public void A_shared_anniversary_year_is_shown()
    {
        var text = DailyTick.Compose(
            MilestoneKind.Anniversary,
            [new Milestone(MilestoneKind.Anniversary, 11, 3), new Milestone(MilestoneKind.Anniversary, 22, 3)],
            "still here");

        Assert.Contains("(3y)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mixed_anniversary_year_is_not_shown()
    {
        var text = DailyTick.Compose(
            MilestoneKind.Anniversary,
            [new Milestone(MilestoneKind.Anniversary, 11, 3), new Milestone(MilestoneKind.Anniversary, 22, 5)],
            "still here");

        Assert.DoesNotContain("y)", text, StringComparison.Ordinal);
    }

    /// <summary>A birthday's year is the person's age, which is nobody else's business.</summary>
    [Fact]
    public void A_birthday_never_shows_the_age()
    {
        var text = DailyTick.Compose(
            MilestoneKind.Birthday, [new Milestone(MilestoneKind.Birthday, 11, 41)], "happy birthday");

        Assert.DoesNotContain("41", text, StringComparison.Ordinal);
    }

    // ---- Module --------------------------------------------------------------------------------

    /// <summary>
    /// <c>/anniversary</c> plus <c>/birthday set|clear</c>. Registration is what catches a group
    /// name collision or an unresolvable constructor.
    /// </summary>
    [Fact]
    public async Task The_module_registers_its_commands()
    {
        ServiceProvider provider = Services();
        using DiscordRestClient rest = new();
        using InteractionService interactions = new(rest);

        ModuleInfo module = await interactions.AddModuleAsync(typeof(MilestoneModule), provider);

        Assert.Contains(module.SlashCommands, c => c.Name == "anniversary");
        ModuleInfo birthday = Assert.Single(module.SubModules, m => m.SlashGroupName == "birthday");
        Assert.Contains(birthday.SlashCommands, c => c.Name == "set");
        Assert.Contains(birthday.SlashCommands, c => c.Name == "clear");
    }

    private static async Task<Domain.Entities.Core.Member> Row(FakeMemberRepository members)
        => (await members.GetAsync((long)Guild, (long)User))!;

    private static (IMilestoneService Service, FakeMemberRepository Members) Harness()
    {
        FakeMemberRepository members = new();
        ServiceProvider provider = Services(members);
        return (provider.GetRequiredService<IMilestoneService>(), members);
    }

    private static ServiceProvider Services(FakeMemberRepository? members = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IMemberRepository>(members ?? new FakeMemberRepository());
        services.AddSingleton<IGuildConfigService>(new Moderation.FakeGuildConfigService());
        services.AddSingleton(Chat.Build.Persona());
        services.AddSonarrUtilityServices();
        return services.BuildServiceProvider();
    }
}
