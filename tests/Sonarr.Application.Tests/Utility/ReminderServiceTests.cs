using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sonarr.Application.Tests.Jobs;
using Sonarr.Application.Utility;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Jobs;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Tests.Utility;

/// <summary>
/// <c>/remind</c>, <c>/reminders</c> and <c>/timezone</c> at the service boundary: one durable job
/// row per reminder, string snowflakes in the payload (the repository matches
/// <c>payload-&gt;&gt;'user_id'</c> textually), and no ICU dependency anywhere in validation.
/// </summary>
/// <remarks>
/// Resolved through <c>AddSonarrUtilityServices</c> rather than <c>new ReminderService(...)</c>: the
/// implementation is internal, and the wiring is part of what these tests are asserting.
/// </remarks>
public sealed class ReminderServiceTests
{
    private const ulong Guild = 700UL;
    private const ulong Channel = 701UL;
    private const ulong User = 702UL;

    /// <summary>Mirrors <c>ReminderService.MaxTextLength</c> — internal, so restated here.</summary>
    private const int MaxTextLength = 1000;

    /// <summary>Mirrors <c>ReminderService.MaxPendingPerUser</c>.</summary>
    private const int MaxPendingPerUser = 25;

    [Fact]
    public async Task Writes_one_job_row_with_string_snowflakes()
    {
        (IReminderService service, SchedulerJobRepository jobs, _) = Build();

        ScheduleResult result = await service.CreateAsync(Guild, Channel, User, "2h", "drink water");

        Assert.True(result.Success, result.Message);
        Job row = Assert.Single(jobs.Rows);
        Assert.Equal(JobKinds.Reminder, row.Kind);
        Assert.Null(row.Recurrence);
        Assert.Equal("drink water", row.Payload[JobKinds.TextField]!.GetValue<string>());

        // Strings, not JSON numbers: a snowflake exceeds JSON's safe integer range and the
        // ownership query compares text.
        Assert.Equal(
            User.ToString(CultureInfo.InvariantCulture),
            row.Payload[JobKinds.UserIdField]!.GetValue<string>());
        Assert.Equal(
            Channel.ToString(CultureInfo.InvariantCulture),
            row.Payload[JobKinds.ChannelIdField]!.GetValue<string>());
    }

    [Fact]
    public async Task A_repeat_phrase_produces_one_row_carrying_a_recurrence()
    {
        (IReminderService service, SchedulerJobRepository jobs, _) = Build();

        ScheduleResult result = await service.CreateAsync(Guild, Channel, User, "every day 9am", "standup");

        Assert.True(result.Success, result.Message);
        Job row = Assert.Single(jobs.Rows);
        Assert.Equal(Recurrence.Daily, row.Recurrence);
        Assert.Contains("every day", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_when_is_rejected_without_writing_a_row()
    {
        (IReminderService service, SchedulerJobRepository jobs, _) = Build();

        ScheduleResult result = await service.CreateAsync(Guild, Channel, User, "sometime soon", "whatever");

        Assert.False(result.Success);
        Assert.Empty(jobs.Rows);
    }

    [Fact]
    public async Task Empty_and_oversized_text_are_both_refused()
    {
        (IReminderService service, SchedulerJobRepository jobs, _) = Build();

        Assert.False((await service.CreateAsync(Guild, Channel, User, "1h", "   ")).Success);
        Assert.False((await service.CreateAsync(
            Guild, Channel, User, "1h", new string('x', MaxTextLength + 1))).Success);
        Assert.Empty(jobs.Rows);
    }

    [Fact]
    public async Task One_user_cannot_fill_the_claim_batch()
    {
        (IReminderService service, SchedulerJobRepository jobs, _) = Build();

        for (var i = 0; i < MaxPendingPerUser; i++)
        {
            Assert.True((await service.CreateAsync(Guild, Channel, User, "1h", $"thing {i}")).Success);
        }

        ScheduleResult tooMany = await service.CreateAsync(Guild, Channel, User, "1h", "one more");

        Assert.False(tooMany.Success);
        Assert.Contains("cancel", tooMany.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MaxPendingPerUser, jobs.Rows.Count);
    }

    [Fact]
    public async Task Lists_pending_reminders_with_their_schedule_phrase()
    {
        (IReminderService service, _, _) = Build();
        await service.CreateAsync(Guild, Channel, User, "1h", "one-shot");
        await service.CreateAsync(Guild, Channel, User, "every week 09:00", "weekly thing");

        IReadOnlyList<ReminderView> pending = await service.ListAsync(User);

        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, r => r.Recurrence is null && r.Schedule == "once" && r.Text == "one-shot");
        Assert.Contains(pending, r => r.Recurrence == Recurrence.Weekly && r.Schedule == "every week");
    }

    [Fact]
    public async Task Cancelling_removes_the_row_and_reports_a_miss_honestly()
    {
        (IReminderService service, _, _) = Build();
        ScheduleResult created = await service.CreateAsync(Guild, Channel, User, "1h", "cancel me");

        Assert.True(await service.CancelAsync(User, created.JobId));
        Assert.False(await service.CancelAsync(User, created.JobId));
        Assert.Empty(await service.ListAsync(User));
    }

    [Fact]
    public async Task Read_turns_a_row_back_into_a_delivery()
    {
        (IReminderService service, SchedulerJobRepository jobs, _) = Build();
        await service.CreateAsync(Guild, Channel, User, "1h", "hydrate");

        ReminderDelivery? delivery = service.Read(jobs.Rows[0]);

        Assert.NotNull(delivery);
        Assert.Equal(
            (Guild, Channel, User, "hydrate"),
            (delivery!.GuildId, delivery.ChannelId, delivery.UserId, delivery.Text));
    }

    [Fact]
    public void Read_returns_null_for_a_row_it_cannot_use()
    {
        (IReminderService service, _, _) = Build();

        // A hand-edited row: the handler drops these instead of retrying forever.
        Assert.Null(service.Read(new Job { JobId = 9, Kind = JobKinds.Reminder }));
    }

    [Fact]
    public async Task A_recurring_row_reports_its_next_occurrence_in_the_future()
    {
        (IReminderService service, SchedulerJobRepository jobs, _) = Build();
        await service.CreateAsync(Guild, Channel, User, "every day 9am", "standup");
        Job row = jobs.Rows[0];

        DateTimeOffset? next = await service.NextOccurrenceAsync(row);

        Assert.NotNull(next);
        Assert.True(next > DateTimeOffset.UtcNow);

        // No zone set anywhere, so the fallback is UTC and a daily step is exactly 24h.
        Assert.Equal(TimeSpan.FromDays(1), next!.Value - row.RunAt);
    }

    [Fact]
    public async Task A_one_shot_row_has_no_next_occurrence()
    {
        (IReminderService service, SchedulerJobRepository jobs, _) = Build();
        await service.CreateAsync(Guild, Channel, User, "1h", "once only");

        Assert.Null(await service.NextOccurrenceAsync(jobs.Rows[0]));
    }

    [Fact]
    public async Task Setting_a_timezone_validates_the_shape_not_an_icu_lookup()
    {
        (IReminderService service, _, FakeTimezoneMemberRepository members) = Build();

        // Passes on a Windows dev box with no tzdata, exactly as it does in the container.
        ScheduleResult ok = await service.SetTimezoneAsync(Guild, User, "Asia/Ho_Chi_Minh");

        Assert.True(ok.Success, ok.Message);
        Assert.Equal("Asia/Ho_Chi_Minh", members.Timezone);
    }

    [Fact]
    public async Task A_malformed_timezone_is_refused_and_nothing_is_stored()
    {
        (IReminderService service, _, FakeTimezoneMemberRepository members) = Build();

        // A Windows id, which is exactly what a user copies out of the OS clock settings.
        ScheduleResult bad = await service.SetTimezoneAsync(Guild, User, "Pacific Standard Time");

        Assert.False(bad.Success);
        Assert.Contains("Asia/Ho_Chi_Minh", bad.Message, StringComparison.Ordinal);
        Assert.Null(members.Timezone);
    }

    [Fact]
    public async Task An_empty_timezone_clears_it()
    {
        (IReminderService service, _, FakeTimezoneMemberRepository members) = Build();
        await service.SetTimezoneAsync(Guild, User, "Europe/London");

        ScheduleResult cleared = await service.SetTimezoneAsync(Guild, User, null);

        Assert.True(cleared.Success);
        Assert.Null(members.Timezone);
    }

    [Fact]
    public async Task Zone_resolution_falls_back_to_utc_instead_of_throwing()
    {
        (IReminderService service, _, FakeTimezoneMemberRepository members) = Build();

        // Nothing set anywhere: UTC.
        Assert.Equal(TimeZoneInfo.Utc, await service.ResolveZoneAsync(Guild, User));

        // A stored id the runtime can't resolve (every id, on a box without tzdata) lands here too
        // rather than blowing up a reminder.
        members.Timezone = "Antarctica/Nowhere_Real";
        Assert.Equal(TimeZoneInfo.Utc, await service.ResolveZoneAsync(Guild, User));
    }

    private static (IReminderService Service, SchedulerJobRepository Jobs, FakeTimezoneMemberRepository Members) Build()
    {
        SchedulerJobRepository jobs = new();
        FakeTimezoneMemberRepository members = new();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IJobRepository>(jobs);
        services.AddSingleton<IMemberRepository>(members);
        services.AddSingleton<IGuildConfigService>(new Moderation.FakeGuildConfigService());
        services.AddSonarrUtilityServices();

        ServiceProvider provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IReminderService>(), jobs, members);
    }
}

/// <summary>Just enough core.member for the zone chain: one row, one timezone.</summary>
internal sealed class FakeTimezoneMemberRepository : IMemberRepository
{
    public string? Timezone { get; set; }

    public Task<Member?> GetAsync(long guildId, long userId, CancellationToken ct = default)
        => Task.FromResult<Member?>(new Member
        {
            GuildId = guildId,
            UserId = userId,
            Timezone = Timezone,
        });

    public Task SetTimezoneAsync(long guildId, long userId, string? ianaTimezone, CancellationToken ct = default)
    {
        Timezone = ianaTimezone;
        return Task.CompletedTask;
    }

    public Task<Member> UpsertAsync(
        long guildId, long userId, string username, string displayName, CancellationToken ct = default)
        => Task.FromResult(new Member { GuildId = guildId, UserId = userId });

    public Task ApplyActivityAsync(
        IReadOnlyCollection<MemberActivityDelta> deltas, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetBirthdayAsync(long guildId, long userId, DateOnly? birthday, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<Member>> GetBirthdaysAsync(
        long guildId, int month, int day, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Member>>([]);

    public Task<IReadOnlyList<Member>> GetJoinAnniversariesAsync(
        long guildId, int month, int day, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Member>>([]);

    public Task<long?> FindUserIdByUsernameAsync(string username, CancellationToken ct = default)
        => Task.FromResult<long?>(null);
}
