using System.Globalization;
using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Tests.Moderation;

/// <summary>
/// The service-layer contract: the guard runs before any side effect, every allowed action gets a
/// case number, and a tempban leaves a durable job row behind.
/// </summary>
public sealed class ModerationServiceTests
{
    [Fact]
    public async Task Allowed_action_runs_the_effect_and_files_a_case()
    {
        (Sonarr.Application.Moderation.ModerationService service, FakeModCaseRepository cases, _) =
            Build.Moderation();

        var ran = false;

        ModerationOutcome outcome = await service.ApplyAsync(
            Build.Case(CaseAction.Kick, "being a nuisance"),
            Build.Allowed(),
            ct =>
            {
                ran = true;
                return Task.CompletedTask;
            });

        Assert.True(outcome.Allowed);
        Assert.True(ran);
        CaseRecord record = Assert.Single(cases.Rows);
        Assert.Equal(CaseAction.Kick, record.Action);
        Assert.Equal("being a nuisance", record.Reason);
    }

    [Fact]
    public async Task Cases_are_numbered_in_order_across_actions()
    {
        (Sonarr.Application.Moderation.ModerationService service, _, _) = Build.Moderation();

        ModerationOutcome first = await service.ApplyAsync(Build.Case(), Build.Allowed());
        ModerationOutcome second = await service.ApplyAsync(Build.Case(CaseAction.Kick), Build.Allowed());
        CaseRecord third = await service.RecordSlowmodeAsync(Build.Guild, 99UL, Build.Actor, 30);

        Assert.Equal(1, first.Case!.CaseId);
        Assert.Equal(2, second.Case!.CaseId);
        Assert.Equal(3, third.CaseId);
    }

    [Fact]
    public async Task Refused_action_runs_no_effect_and_files_no_case()
    {
        (Sonarr.Application.Moderation.ModerationService service, FakeModCaseRepository cases, _) =
            Build.Moderation();

        var ran = false;

        // Target's top role sits above the actor's: the classic hierarchy bypass attempt.
        ModerationOutcome outcome = await service.ApplyAsync(
            Build.Case(CaseAction.Ban),
            Build.Allowed(actorRole: 5, targetRole: 9),
            ct =>
            {
                ran = true;
                return Task.CompletedTask;
            });

        Assert.False(outcome.Allowed);
        Assert.Equal(ModerationDenial.ActorRoleTooLow, outcome.Denial);
        Assert.False(ran);
        Assert.Empty(cases.Rows);
    }

    [Fact]
    public async Task Blank_reason_becomes_the_default_text()
    {
        (Sonarr.Application.Moderation.ModerationService service, _, _) = Build.Moderation();

        ModerationOutcome outcome = await service.ApplyAsync(
            Build.Case(CaseAction.Warn, "   "), Build.Allowed());

        Assert.Equal(ModerationLimits.NoReason, outcome.Case!.Reason);
    }

    [Fact]
    public async Task Reason_is_truncated_rather_than_rejected()
    {
        (Sonarr.Application.Moderation.ModerationService service, _, _) = Build.Moderation();

        ModerationOutcome outcome = await service.ApplyAsync(
            Build.Case(CaseAction.Warn, new string('x', ModerationLimits.MaxReasonLength + 50)),
            Build.Allowed());

        Assert.Equal(ModerationLimits.MaxReasonLength, outcome.Case!.Reason.Length);
    }

    [Fact]
    public async Task Tempban_schedules_a_lift_job_that_survives_a_restart()
    {
        (Sonarr.Application.Moderation.ModerationService service, _, FakeJobRepository jobs) =
            Build.Moderation();

        DateTimeOffset before = DateTimeOffset.UtcNow;

        ModerationOutcome outcome = await service.TempBanAsync(
            Build.Case(CaseAction.Ban, "raiding"),
            TimeSpan.FromHours(6),
            Build.Allowed());

        Assert.True(outcome.Allowed);
        Assert.Equal(CaseAction.TempBan, outcome.Case!.Action);
        Assert.NotNull(outcome.Case.ExpiresAt);

        Sonarr.Domain.Entities.Core.Job job = Assert.Single(jobs.Scheduled);
        Assert.Equal(TempBanJob.Kind, job.Kind);

        // run_at is the expiry, so the poller — not an in-process timer — owns the lift.
        Assert.InRange(
            job.RunAt,
            before.AddHours(6).AddSeconds(-5),
            DateTimeOffset.UtcNow.AddHours(6).AddSeconds(5));
        Assert.Equal(outcome.Case.ExpiresAt!.Value, job.RunAt, TimeSpan.FromSeconds(1));

        // Snowflakes go in as strings: a ulong does not survive a JSON number round-trip.
        Assert.Equal(
            Build.Target.ToString(CultureInfo.InvariantCulture),
            job.Payload[TempBanJob.UserIdField]!.ToString());
        Assert.Equal(
            Build.Guild.ToString(CultureInfo.InvariantCulture),
            job.Payload[TempBanJob.GuildIdField]!.ToString());
        Assert.Equal(
            outcome.Case.CaseId.ToString(CultureInfo.InvariantCulture),
            job.Payload[TempBanJob.CaseIdField]!.ToString());
    }

    [Fact]
    public async Task Refused_tempban_schedules_nothing()
    {
        (Sonarr.Application.Moderation.ModerationService service, FakeModCaseRepository cases, FakeJobRepository jobs) =
            Build.Moderation();

        ModerationOutcome outcome = await service.TempBanAsync(
            Build.Case(CaseAction.Ban),
            TimeSpan.FromHours(1),
            Build.Allowed(actorRole: 2, targetRole: 8));

        Assert.False(outcome.Allowed);
        Assert.Empty(jobs.Scheduled);
        Assert.Empty(cases.Rows);
    }

    [Theory]
    [InlineData(1)]      // a second: below MinTempBan
    [InlineData(400)]    // days: above MaxTempBan
    public async Task Tempban_rejects_a_duration_outside_the_limits(int amount)
    {
        (Sonarr.Application.Moderation.ModerationService service, _, FakeJobRepository jobs) =
            Build.Moderation();

        TimeSpan duration = amount == 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromDays(amount);

        ModerationOutcome outcome = await service.TempBanAsync(
            Build.Case(CaseAction.Ban), duration, Build.Allowed());

        Assert.Equal(ModerationDenial.InvalidDuration, outcome.Denial);
        Assert.Empty(jobs.Scheduled);
    }

    [Fact]
    public async Task Failed_effect_leaves_no_case_claiming_it_happened()
    {
        (Sonarr.Application.Moderation.ModerationService service, FakeModCaseRepository cases, _) =
            Build.Moderation();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            Build.Case(CaseAction.Ban),
            Build.Allowed(),
            ct => throw new InvalidOperationException("Discord said no")));

        Assert.Empty(cases.Rows);
    }

    [Fact]
    public async Task Slowmode_case_records_the_channel_and_seconds_only()
    {
        (Sonarr.Application.Moderation.ModerationService service, _, _) = Build.Moderation();

        CaseRecord record = await service.RecordSlowmodeAsync(Build.Guild, 777UL, Build.Actor, 45);

        Assert.Equal(CaseAction.Slowmode, record.Action);
        Assert.Equal("45", record.Context["seconds"]);
        Assert.Equal("777", record.Context["channel"]);
    }

    [Fact]
    public async Task Policy_falls_back_to_defaults_when_nothing_is_configured()
    {
        (Sonarr.Application.Moderation.ModerationService service, _, _) = Build.Moderation();

        ModerationPolicy policy = await service.GetPolicyAsync(Build.Guild);

        Assert.Equal(ModerationPolicy.Default, policy);
    }

    [Fact]
    public async Task Policy_reads_configured_values()
    {
        FakeGuildConfigService config = new FakeGuildConfigService()
            .With(Sonarr.Application.Moderation.ModerationConfigKeys.IdenticalThreshold, "3")
            .With(Sonarr.Application.Moderation.ModerationConfigKeys.Action, "timeout")
            .With(Sonarr.Application.Moderation.ModerationConfigKeys.AntiSpamEnabled, "false");

        (Sonarr.Application.Moderation.ModerationService service, _, _) = Build.Moderation(config);

        ModerationPolicy policy = await service.GetPolicyAsync(Build.Guild);

        Assert.Equal(3, policy.IdenticalFloodThreshold);
        Assert.Equal(SpamAction.Timeout, policy.Action);
        Assert.False(policy.AntiSpamEnabled);
    }

    [Fact]
    public async Task Modlog_pages_newest_first()
    {
        (Sonarr.Application.Moderation.ModerationService service, _, _) = Build.Moderation();

        for (var i = 0; i < CasePage.PageSize + 3; i++)
        {
            await service.ApplyAsync(Build.Case(CaseAction.Warn, $"warn {i}"), Build.Allowed());
        }

        CasePage first = await service.GetModLogAsync(Build.Guild, null, 1);
        CasePage second = await service.GetModLogAsync(Build.Guild, null, 2);

        Assert.Equal(CasePage.PageSize + 3, first.TotalCount);
        Assert.Equal(2, first.PageCount);
        Assert.Equal(CasePage.PageSize, first.Cases.Count);
        Assert.Equal(3, second.Cases.Count);
        Assert.True(first.Cases[0].CaseId > first.Cases[1].CaseId);
    }

    [Fact]
    public async Task A_case_from_another_guild_is_not_visible()
    {
        (Sonarr.Application.Moderation.ModerationService service, _, _) = Build.Moderation();

        ModerationOutcome outcome = await service.ApplyAsync(Build.Case(), Build.Allowed());

        Assert.Null(await service.GetCaseAsync(Build.Guild + 1, outcome.Case!.CaseId));
        Assert.NotNull(await service.GetCaseAsync(Build.Guild, outcome.Case.CaseId));
    }
}
