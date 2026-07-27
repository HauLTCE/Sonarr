using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Bot.Discord.Jobs;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Jobs;

namespace Sonarr.Application.Tests.Jobs;

/// <summary>
/// The scheduler contract three other slices depend on: claim, dispatch by kind, survive a
/// throwing handler, re-arm recurring rows (docs/08-background-services.md).
/// </summary>
public sealed class JobSchedulerTests
{
    [Fact]
    public async Task Dispatches_a_due_job_to_the_handler_for_its_kind()
    {
        SchedulerJobRepository jobs = new();
        Job reminder = jobs.Add(JobKinds.Reminder);
        jobs.Add(JobKinds.Announce);

        ProbeHandler reminders = new(JobKinds.Reminder);
        ProbeHandler announces = new(JobKinds.Announce);

        await Scheduler(jobs, reminders, announces).PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, reminders.Calls);
        Assert.Equal(1, announces.Calls);
        Assert.Contains(reminder.JobId, jobs.Completed);
        Assert.Equal(JobStatus.Done, reminder.Status);
    }

    [Fact]
    public async Task Leaves_jobs_that_are_not_due_yet_alone()
    {
        SchedulerJobRepository jobs = new();
        Job later = jobs.Add(JobKinds.Reminder, runAt: DateTimeOffset.UtcNow.AddHours(1));
        ProbeHandler handler = new(JobKinds.Reminder);

        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        Assert.Equal(0, handler.Calls);
        Assert.Equal(JobStatus.Pending, later.Status);
    }

    [Fact]
    public async Task A_throwing_handler_retries_the_row_instead_of_killing_the_poller()
    {
        SchedulerJobRepository jobs = new();
        Job job = jobs.Add(JobKinds.Reminder);
        ProbeHandler handler = new(JobKinds.Reminder) { Throws = new InvalidOperationException("channel gone") };

        JobScheduler scheduler = Scheduler(jobs, handler);

        // The poll itself must not throw — that is the whole point of the guard.
        await scheduler.PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Empty(jobs.Failed);
        Assert.Empty(jobs.Completed);
        Assert.Equal(JobStatus.Pending, job.Status);
        (var jobId, DateTimeOffset retryAt, var error) = Assert.Single(jobs.Retried);
        Assert.Equal(job.JobId, jobId);
        Assert.True(retryAt > DateTimeOffset.UtcNow, "the retry is in the future");
        Assert.Equal("channel gone", error);
    }

    [Fact]
    public async Task The_attempt_counter_lives_in_the_row_so_a_restart_does_not_reset_it()
    {
        SchedulerJobRepository jobs = new();
        Job job = jobs.Add(JobKinds.Reminder);
        ProbeHandler handler = new(JobKinds.Reminder) { Throws = new InvalidOperationException("nope") };

        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        // A fresh scheduler instance stands in for a restarted process: it must read the attempt
        // count off the payload, not start over from zero.
        Assert.Equal(1, job.Payload[JobScheduler.AttemptsField]!.GetValue<int>());

        job.RunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        job.Status = JobStatus.Pending;
        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, job.Payload[JobScheduler.AttemptsField]!.GetValue<int>());
    }

    [Fact]
    public async Task A_garbled_attempt_counter_costs_one_attempt_not_a_crash()
    {
        SchedulerJobRepository jobs = new();

        // core.job.payload is jsonb a human can edit through the panel or psql.
        Job job = jobs.Add(JobKinds.Reminder, new JsonObject { [JobScheduler.AttemptsField] = "lots" });
        ProbeHandler handler = new(JobKinds.Reminder) { Throws = new InvalidOperationException("nope") };

        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        Assert.Empty(jobs.Failed);
        Assert.Equal(1, job.Payload[JobScheduler.AttemptsField]!.GetValue<int>());
    }

    [Fact]
    public async Task A_successful_recurring_run_clears_the_attempt_counter()
    {
        SchedulerJobRepository jobs = new();
        Job job = jobs.Add(
            JobKinds.Reminder,
            new JsonObject { [JobScheduler.AttemptsField] = 1 },
            Domain.Jobs.Recurrence.Daily);

        DateTimeOffset tomorrow = DateTimeOffset.UtcNow.AddDays(1);
        await Scheduler(jobs, new ProbeRecurringHandler(JobKinds.Reminder, tomorrow))
            .PollOnceAsync(CancellationToken.None);

        // Otherwise a reminder that failed twice a year ago would be one bad day from being killed.
        Assert.Null(job.Payload[JobScheduler.AttemptsField]);
        Assert.Equal(tomorrow, job.RunAt);
        Assert.Equal(JobStatus.Pending, job.Status);
    }

    [Fact]
    public async Task Gives_up_after_the_attempt_limit_and_records_the_error()
    {
        SchedulerJobRepository jobs = new();
        Job job = jobs.Add(JobKinds.Reminder);
        ProbeHandler handler = new(JobKinds.Reminder) { Throws = new InvalidOperationException("still broken") };

        JobScheduler scheduler = Scheduler(jobs, handler);

        for (var attempt = 0; attempt < JobScheduler.MaxAttempts; attempt++)
        {
            // Each retry is scheduled in the future, so pull it back to due before polling again.
            job.RunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            job.Status = JobStatus.Pending;
            await scheduler.PollOnceAsync(CancellationToken.None);
        }

        Assert.Equal(JobScheduler.MaxAttempts, handler.Calls);
        (var jobId, var error) = Assert.Single(jobs.Failed);
        Assert.Equal(job.JobId, jobId);
        Assert.Contains("still broken", error, StringComparison.Ordinal);
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public async Task An_unknown_kind_is_failed_rather_than_reclaimed_forever()
    {
        SchedulerJobRepository jobs = new();
        Job orphan = jobs.Add("capsule_open");

        await Scheduler(jobs, new ProbeHandler(JobKinds.Reminder)).PollOnceAsync(CancellationToken.None);

        (var jobId, var error) = Assert.Single(jobs.Failed);
        Assert.Equal(orphan.JobId, jobId);
        Assert.Contains("capsule_open", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recurring_row_is_re_armed_in_place_keeping_its_job_id()
    {
        SchedulerJobRepository jobs = new();
        Job job = jobs.Add(JobKinds.Reminder, recurrence: Domain.Jobs.Recurrence.Daily);

        DateTimeOffset tomorrow = DateTimeOffset.UtcNow.AddDays(1);
        ProbeRecurringHandler handler = new(JobKinds.Reminder, tomorrow);

        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        Assert.Equal((job.JobId, tomorrow), Assert.Single(jobs.ReArmed));
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Empty(jobs.Completed);
        Assert.Single(jobs.Rows);
    }

    [Fact]
    public async Task A_recurring_row_whose_handler_returns_null_is_finished()
    {
        SchedulerJobRepository jobs = new();
        Job job = jobs.Add(JobKinds.Reminder, recurrence: Domain.Jobs.Recurrence.Daily);
        ProbeRecurringHandler handler = new(JobKinds.Reminder, null);

        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        Assert.Contains(job.JobId, jobs.Completed);
        Assert.Empty(jobs.ReArmed);
    }

    [Fact]
    public async Task A_one_shot_row_never_asks_for_a_next_occurrence()
    {
        SchedulerJobRepository jobs = new();
        jobs.Add(JobKinds.Reminder);

        // Recurrence is null on the row, so the recurring path must not run even though the handler
        // offers one.
        ProbeRecurringHandler handler = new(JobKinds.Reminder, DateTimeOffset.UtcNow.AddDays(1));

        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        Assert.Empty(jobs.ReArmed);
        Assert.Single(jobs.Completed);
    }

    [Fact]
    public async Task One_bad_job_does_not_stop_the_rest_of_the_batch()
    {
        SchedulerJobRepository jobs = new();
        jobs.Add(JobKinds.Reminder);
        Job good = jobs.Add(JobKinds.Announce);

        ProbeHandler broken = new(JobKinds.Reminder) { Throws = new InvalidOperationException("nope") };
        ProbeHandler working = new(JobKinds.Announce);

        await Scheduler(jobs, broken, working).PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, working.Calls);
        Assert.Contains(good.JobId, jobs.Completed);
    }

    [Fact]
    public async Task Claims_at_most_one_batch_per_poll()
    {
        SchedulerJobRepository jobs = new();
        for (var i = 0; i < JobScheduler.BatchSize + 5; i++)
        {
            jobs.Add(JobKinds.Reminder);
        }

        ProbeHandler handler = new(JobKinds.Reminder);
        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        Assert.Equal(JobScheduler.BatchSize, handler.Calls);
        Assert.Equal(5, jobs.Rows.Count(j => j.Status == JobStatus.Pending));
    }

    [Fact]
    public async Task An_empty_due_set_costs_one_query_and_nothing_else()
    {
        SchedulerJobRepository jobs = new();
        ProbeHandler handler = new(JobKinds.Reminder);

        await Scheduler(jobs, handler).PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, jobs.ClaimCalls);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void The_poll_interval_matches_the_documented_fifteen_seconds()
        => Assert.Equal(TimeSpan.FromSeconds(15), JobScheduler.PollInterval);

    private static JobScheduler Scheduler(SchedulerJobRepository jobs, params IJobHandler[] handlers)
        => new(SchedulerHost.Scopes(jobs, handlers), NullLogger<JobScheduler>.Instance);
}
