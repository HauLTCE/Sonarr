using System.Text.Json.Nodes;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// core.job access — the durable scheduler's storage (docs/08-background-services.md:
/// 15 s poll, FOR UPDATE SKIP LOCKED). This table is the authority, not Redis.
/// </summary>
public interface IJobRepository
{
    /// <summary>Enqueue a job. Returns the assigned job_id.</summary>
    Task<long> ScheduleAsync(
        string kind,
        DateTimeOffset runAt,
        JsonObject payload,
        string? recurrence = null,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically claim up to <paramref name="limit"/> pending jobs whose run_at has passed,
    /// marking them running. Concurrent pollers never see the same row
    /// (SELECT … FOR UPDATE SKIP LOCKED).
    /// </summary>
    Task<IReadOnlyList<Job>> ClaimDueAsync(int limit, CancellationToken ct = default);

    /// <summary>Mark done. For recurring kinds, pass the next occurrence to re-arm in one step.</summary>
    Task CompleteAsync(long jobId, DateTimeOffset? nextRunAt = null, CancellationToken ct = default);

    /// <summary>Mark failed with the error text kept for the panel.</summary>
    Task FailAsync(long jobId, string error, CancellationToken ct = default);

    /// <summary>
    /// Put a claimed job back to pending at <paramref name="runAt"/>, replacing its payload
    /// (the attempt counter lives there). Used for retry-with-backoff and for releasing a job
    /// untouched on shutdown — a job left 'running' by a killed process would never run again.
    /// </summary>
    Task RetryAsync(
        long jobId,
        DateTimeOffset runAt,
        JsonObject payload,
        string? error,
        CancellationToken ct = default);

    /// <summary>Pending jobs of a kind for one user — /reminders list.</summary>
    Task<IReadOnlyList<Job>> GetPendingByKindAsync(string kind, long userId, CancellationToken ct = default);

    /// <summary>Cancel a pending job. False if it was missing, not the user's, or already run.</summary>
    Task<bool> CancelAsync(long jobId, long userId, CancellationToken ct = default);
}
