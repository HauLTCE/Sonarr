using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Application.Tests.Jobs;

/// <summary>
/// In-memory core.job that behaves like the real one where the scheduler cares: claiming moves a
/// row out of the due set, <c>CompleteAsync(next)</c> re-arms it, <c>FailAsync</c> parks it.
/// </summary>
internal sealed class SchedulerJobRepository : IJobRepository
{
    private long _next;

    public List<Job> Rows { get; } = [];

    public List<long> Completed { get; } = [];

    public List<(long JobId, DateTimeOffset RunAt)> ReArmed { get; } = [];

    public List<(long JobId, string Error)> Failed { get; } = [];

    /// <summary>Kept separate from <see cref="ReArmed"/>: a retry is not a successful occurrence.</summary>
    public List<(long JobId, DateTimeOffset RunAt, string? Error)> Retried { get; } = [];

    public int ClaimCalls { get; private set; }

    public Job Add(string kind, JsonObject? payload = null, string? recurrence = null, DateTimeOffset? runAt = null)
    {
        Job job = new()
        {
            JobId = ++_next,
            Kind = kind,
            RunAt = runAt ?? DateTimeOffset.UtcNow.AddMinutes(-1),
            Payload = payload ?? new JsonObject(),
            Recurrence = recurrence,
            Status = JobStatus.Pending,
        };

        Rows.Add(job);
        return job;
    }

    public Task<IReadOnlyList<Job>> ClaimDueAsync(int limit, CancellationToken ct = default)
    {
        ClaimCalls++;

        List<Job> due =
        [
            .. Rows
                .Where(j => j.Status == JobStatus.Pending && j.RunAt <= DateTimeOffset.UtcNow)
                .OrderBy(j => j.RunAt)
                .Take(limit),
        ];

        foreach (Job job in due)
        {
            job.Status = JobStatus.Running;
        }

        return Task.FromResult<IReadOnlyList<Job>>(due);
    }

    public Task CompleteAsync(long jobId, DateTimeOffset? nextRunAt = null, CancellationToken ct = default)
    {
        Job? job = Rows.FirstOrDefault(j => j.JobId == jobId);
        if (job is null)
        {
            return Task.CompletedTask;
        }

        if (nextRunAt is { } next)
        {
            job.Status = JobStatus.Pending;
            job.RunAt = next;
            job.Error = null;
            ReArmed.Add((jobId, next));
        }
        else
        {
            job.Status = JobStatus.Done;
            Completed.Add(jobId);
        }

        return Task.CompletedTask;
    }

    public Task FailAsync(long jobId, string error, CancellationToken ct = default)
    {
        Job? job = Rows.FirstOrDefault(j => j.JobId == jobId);
        if (job is not null)
        {
            job.Status = JobStatus.Failed;
            job.Error = error;
        }

        Failed.Add((jobId, error));
        return Task.CompletedTask;
    }

    public Task RetryAsync(
        long jobId,
        DateTimeOffset runAt,
        JsonObject payload,
        string? error,
        CancellationToken ct = default)
    {
        Job? job = Rows.FirstOrDefault(j => j.JobId == jobId);
        if (job is not null)
        {
            job.Status = JobStatus.Pending;
            job.RunAt = runAt;
            job.Payload = payload;
            // Kept, unlike CompleteAsync: why the last attempt failed is the only record of a retry.
            job.Error = error;
        }

        Retried.Add((jobId, runAt, error));
        return Task.CompletedTask;
    }

    public Task<long> ScheduleAsync(
        string kind, DateTimeOffset runAt, JsonObject payload, string? recurrence = null, CancellationToken ct = default)
        => Task.FromResult(Add(kind, payload, recurrence, runAt).JobId);

    public Task<IReadOnlyList<Job>> GetPendingByKindAsync(string kind, long userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Job>>(
            [.. Rows.Where(j => j.Kind == kind && j.Status == JobStatus.Pending)]);

    public Task<bool> CancelAsync(long jobId, long userId, CancellationToken ct = default)
        => Task.FromResult(Rows.RemoveAll(j => j.JobId == jobId) > 0);
}

/// <summary>A handler whose behaviour a test dictates: succeed, throw, or count the calls.</summary>
internal sealed class ProbeHandler(string kind) : IJobHandler
{
    public string Kind { get; } = kind;

    public int Calls { get; private set; }

    public Exception? Throws { get; set; }

    public Task HandleAsync(Job job, CancellationToken ct = default)
    {
        Calls++;
        return Throws is null ? Task.CompletedTask : Task.FromException(Throws);
    }
}

/// <summary>A recurring handler that always asks for the same next occurrence.</summary>
internal sealed class ProbeRecurringHandler(string kind, DateTimeOffset? next)
    : IJobHandler, Bot.Discord.Jobs.IRecurringJobHandler
{
    public string Kind { get; } = kind;

    public int Calls { get; private set; }

    public Task HandleAsync(Job job, CancellationToken ct = default)
    {
        Calls++;
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> NextRunAtAsync(Job job, CancellationToken ct = default)
        => Task.FromResult(next);
}

internal static class SchedulerHost
{
    /// <summary>
    /// The minimum DI graph <see cref="Bot.Discord.Jobs.JobScheduler"/> needs: a scope factory that
    /// hands back the fake repository and whatever handlers the test registered.
    /// </summary>
    public static IServiceScopeFactory Scopes(SchedulerJobRepository jobs, params IJobHandler[] handlers)
    {
        ServiceCollection services = new();
        services.AddSingleton<IJobRepository>(jobs);
        foreach (IJobHandler handler in handlers)
        {
            // Registered as the interface: the scheduler resolves IJobHandler, not the concrete type.
            services.AddSingleton<IJobHandler>(handler);
        }

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
