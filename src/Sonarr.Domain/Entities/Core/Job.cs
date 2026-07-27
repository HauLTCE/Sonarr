using System.Text.Json.Nodes;

namespace Sonarr.Domain.Entities.Core;

/// <summary>
/// core.job — the durable scheduler (reminders, tempban lifts, capsules, announces).
/// Authority lives here, never in Redis: survives a FLUSHALL (docs/05-caching.md).
/// </summary>
public class Job : AuditedEntity
{
    public long JobId { get; set; }

    /// <summary>reminder, recurring_reminder, tempban_lift, announce, season_close, …</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Indexed; the poller claims due rows with FOR UPDATE SKIP LOCKED.</summary>
    public DateTimeOffset RunAt { get; set; }

    /// <summary>Cron-ish expression, only for recurring kinds.</summary>
    public string? Recurrence { get; set; }

    /// <summary>Kind-specific jsonb payload.</summary>
    public JsonObject Payload { get; set; } = new();

    /// <summary>See <see cref="JobStatus"/>.</summary>
    public string Status { get; set; } = JobStatus.Pending;

    /// <summary>Failure detail; null unless <see cref="Status"/> is failed.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Allowed core.job.status values. The doc lists pending/done/failed; "running"
/// is added so a claimed row is not re-claimed by another poller instance.
/// </summary>
public static class JobStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}
