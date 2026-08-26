using System.Collections.Concurrent;
using System.Globalization;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Bot.Discord.Jobs;

/// <summary>
/// The durable scheduler (docs/08-background-services.md): every 15 s it claims due
/// <c>core.job</c> rows with <c>FOR UPDATE SKIP LOCKED</c> and hands each to the
/// <see cref="IJobHandler"/> registered for its kind. This is why reminders, tempban lifts,
/// announcements, capsules and season closes survive a restart — no in-process timer holds any
/// of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A throwing handler never kills the poller.</b> Each job runs inside its own try/catch; on
/// failure the row is re-armed with a backoff for <see cref="MaxAttempts"/> tries and then marked
/// <c>failed</c> with the error text kept on the row for a human to read.
/// </para>
/// <para>
/// <b>Cheap by design</b> — the box is a Pentium J2900. One indexed query per tick against
/// <c>run_at</c>, no work at all when nothing is due, and at most <see cref="BatchSize"/> jobs per
/// tick so a backlog drains over several ticks instead of one long stall.
/// </para>
/// </remarks>
public sealed class JobScheduler(IServiceScopeFactory scopes, ILogger<JobScheduler> log) : BackgroundService
{
    /// <summary>docs/08: 15 s poll.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    /// <summary>Jobs claimed per tick. Small on purpose: a backlog drains across ticks.</summary>
    public const int BatchSize = 20;

    /// <summary>Tries, then the row is failed for a human to look at.</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Payload field the attempt counter lives in. In the row, not in memory: a poison job that
    /// crashes the process would otherwise get three fresh attempts on every restart.
    /// </summary>
    public const string AttemptsField = "attempts";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("JobScheduler polling every {Seconds}s", PollInterval.TotalSeconds);

        using PeriodicTimer timer = new(PollInterval);
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Postgres blipped, or the claim transaction lost a race. The next tick is a fresh
                // attempt; the loop is the one thing that must not die.
                log.LogError(ex, "Job poll failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// One poll cycle. Public so a test can drive it deterministically instead of waiting on the
    /// timer.
    /// </summary>
    public async Task PollOnceAsync(CancellationToken ct)
    {
        // Fresh scope per tick: the claim, the handlers and their repositories share one DbContext,
        // disposed before the next tick so nothing accumulates in the change tracker. Handlers are
        // resolved here rather than in the constructor precisely because they are scoped.
        using IServiceScope scope = scopes.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        IReadOnlyList<Job> due = await jobs.ClaimDueAsync(BatchSize, ct).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return;
        }

        Dictionary<string, IJobHandler> byKind = Index(scope.ServiceProvider.GetServices<IJobHandler>());
        log.LogDebug("JobScheduler claimed {Count} job(s)", due.Count);

        foreach (Job job in due)
        {
            if (ct.IsCancellationRequested)
            {
                // Shutdown mid-batch: put the row back at its original time so the next process
                // picks it up rather than leaving it stuck in 'running'.
                await ReleaseAsync(jobs, job).ConfigureAwait(false);
                continue;
            }

            await RunAsync(jobs, byKind, job, ct).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(
        IJobRepository jobs,
        Dictionary<string, IJobHandler> byKind,
        Job job,
        CancellationToken ct)
    {
        if (!byKind.TryGetValue(job.Kind, out IJobHandler? handler))
        {
            // An unknown kind is a deployment mismatch, not a transient fault: failing the row
            // stops it being reclaimed every 15 s forever.
            log.LogError("No handler registered for job kind {Kind} (job {JobId})", job.Kind, job.JobId);
            await jobs.FailAsync(job.JobId, $"No handler registered for kind '{job.Kind}'.", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await handler.HandleAsync(job, ct).ConfigureAwait(false);

            // Recurring rows re-arm in place, keeping the same job id so a /reminders cancel handle
            // stays valid. Recurrence maths belongs to the handler — it knows the owner's timezone.
            DateTimeOffset? next = job.Recurrence is not null && handler is IRecurringJobHandler recurring
                ? await recurring.NextRunAtAsync(job, ct).ConfigureAwait(false)
                : null;

            // CompleteAsync clears the error; a re-armed recurring row keeps its payload, so the
            // attempt counter is dropped here rather than left to accumulate across occurrences.
            if (next is not null && job.Payload.Remove(AttemptsField))
            {
                await jobs.RetryAsync(job.JobId, next.Value, job.Payload, null, ct).ConfigureAwait(false);
                return;
            }

            await jobs.CompleteAsync(job.JobId, next, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseAsync(jobs, job).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(jobs, job, ex, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleFailureAsync(IJobRepository jobs, Job job, Exception ex, CancellationToken ct)
    {
        var attempt = AttemptsOf(job) + 1;

        if (attempt >= MaxAttempts)
        {
            log.LogError(ex, "Job {JobId} ({Kind}) failed permanently after {Attempts} attempts",
                job.JobId, job.Kind, attempt);
            await jobs.FailAsync(job.JobId, $"attempt {attempt}/{MaxAttempts}: {ex.Message}", ct)
                .ConfigureAwait(false);
            return;
        }

        // Linear backoff off now: 1 min, then 2. Long enough for a channel-permission or gateway
        // hiccup to clear, short enough that a reminder is still worth delivering.
        DateTimeOffset retryAt = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(attempt);
        log.LogWarning(ex, "Job {JobId} ({Kind}) failed on attempt {Attempt}, retrying at {RetryAt}",
            job.JobId, job.Kind, attempt, retryAt);

        job.Payload[AttemptsField] = attempt;
        await jobs.RetryAsync(job.JobId, retryAt, job.Payload, ex.Message, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts already spent on this row. Read defensively — the payload is jsonb a human can
    /// edit, and a garbled counter should cost the job one attempt, not crash the poller.
    /// </summary>
    private static int AttemptsOf(Job job)
        => job.Payload[AttemptsField] is { } node
           && int.TryParse(
               node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var spent)
           && spent > 0
            ? spent
            : 0;

    /// <summary>
    /// Back to pending at the same run_at — a shutdown is not an attempt, and a row left 'running'
    /// by a killed process would never run again.
    /// </summary>
    private static Task ReleaseAsync(IJobRepository jobs, Job job)
        => jobs.RetryAsync(job.JobId, job.RunAt, job.Payload, job.Error, CancellationToken.None);

    private Dictionary<string, IJobHandler> Index(IEnumerable<IJobHandler> handlers)
    {
        Dictionary<string, IJobHandler> index = [];
        foreach (IJobHandler handler in handlers)
        {
            if (!index.TryAdd(handler.Kind, handler))
            {
                log.LogError(
                    "Two handlers claim job kind {Kind} ({Kept} kept, {Dropped} ignored) — check the DI wiring",
                    handler.Kind, index[handler.Kind].GetType().Name, handler.GetType().Name);
            }
        }

        return index;
    }
}

/// <summary>
/// A job kind that can repeat. The scheduler asks for the next occurrence after a successful run
/// and re-arms the same row, so recurring work never accumulates rows or loses its cancel handle.
/// </summary>
public interface IRecurringJobHandler
{
    /// <summary>Next occurrence, or <c>null</c> to finish the job.</summary>
    Task<DateTimeOffset?> NextRunAtAsync(Job job, CancellationToken ct = default);
}
