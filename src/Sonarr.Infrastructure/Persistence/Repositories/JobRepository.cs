using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IJobRepository"/>
public sealed class JobRepository(SonarrDbContext db) : IJobRepository
{
    /// <summary>
    /// Lock the due rows and skip whatever another poller already holds, so two bot
    /// instances (or a restart overlapping the old process) never run a job twice.
    /// Projected as "Value" because that is what EF's scalar SqlQuery expects.
    /// </summary>
    private const string ClaimSql = """
        SELECT job_id AS "Value"
        FROM core.job
        WHERE status = 'pending' AND run_at <= now()
        ORDER BY run_at
        LIMIT {0}
        FOR UPDATE SKIP LOCKED
        """;

    public async Task<long> ScheduleAsync(
        string kind,
        DateTimeOffset runAt,
        JsonObject payload,
        string? recurrence = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(payload);

        Job job = new()
        {
            Kind = kind,
            RunAt = runAt,
            Payload = payload,
            Recurrence = recurrence,
            Status = JobStatus.Pending,
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job.JobId;
    }

    public async Task<IReadOnlyList<Job>> ClaimDueAsync(int limit, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<long> ids;
        await using (IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct))
        {
            ids = await db.Database.SqlQueryRaw<long>(ClaimSql, limit).ToListAsync(ct);

            if (ids.Count == 0)
            {
                await tx.RollbackAsync(ct);
                return [];
            }

            // Same transaction as the lock: the rows leave 'pending' before it is released.
            await db.Jobs
                .Where(j => ids.Contains(j.JobId))
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(j => j.Status, JobStatus.Running)
                        .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow),
                    ct);

            await tx.CommitAsync(ct);
        }

        return await db.Jobs
            .AsNoTracking()
            .Where(j => ids.Contains(j.JobId))
            .OrderBy(j => j.RunAt)
            .ToListAsync(ct);
    }

    public Task CompleteAsync(long jobId, DateTimeOffset? nextRunAt = null, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Recurring kinds re-arm in place instead of inserting a new row, so /reminders
        // cancel keeps working against the same job id.
        return nextRunAt is { } next
            ? db.Jobs
                .Where(j => j.JobId == jobId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(j => j.Status, JobStatus.Pending)
                        .SetProperty(j => j.RunAt, next)
                        .SetProperty(j => j.Error, (string?)null)
                        .SetProperty(j => j.UpdatedAt, now),
                    ct)
            : db.Jobs
                .Where(j => j.JobId == jobId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(j => j.Status, JobStatus.Done)
                        .SetProperty(j => j.UpdatedAt, now),
                    ct);
    }

    public Task FailAsync(long jobId, string error, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        string trimmed = error.Length <= 2000 ? error : error[..2000];

        return db.Jobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.Status, JobStatus.Failed)
                    .SetProperty(j => j.Error, trimmed)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public Task RetryAsync(
        long jobId,
        DateTimeOffset runAt,
        JsonObject payload,
        string? error,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string? trimmed = error is null ? null : error.Length <= 2000 ? error : error[..2000];

        // Error text is kept while the job is pending again: why the last attempt failed is the only
        // record of a retry, and CompleteAsync clears it on the next success.
        return db.Jobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.Status, JobStatus.Pending)
                    .SetProperty(j => j.RunAt, runAt)
                    .SetProperty(j => j.Payload, payload)
                    .SetProperty(j => j.Error, trimmed)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    // payload is mapped through a string converter, so ownership has to be matched in
    // SQL (payload->>'user_id') rather than in LINQ. Every user-scoped kind writes
    // user_id into its payload.
    public async Task<IReadOnlyList<Job>> GetPendingByKindAsync(
        string kind,
        long userId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        return await db.Jobs
            .FromSql(
                $"""
                SELECT * FROM core.job
                WHERE kind = {kind}
                  AND status = 'pending'
                  AND payload->>'user_id' = {userId.ToString(CultureInfo.InvariantCulture)}
                ORDER BY run_at
                """)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<bool> CancelAsync(long jobId, long userId, CancellationToken ct = default)
    {
        int deleted = await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM core.job
            WHERE job_id = {jobId}
              AND status = 'pending'
              AND payload->>'user_id' = {userId.ToString(CultureInfo.InvariantCulture)}
            """,
            ct);

        return deleted > 0;
    }
}
