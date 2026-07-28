namespace Sonarr.Domain.Jobs;

/// <summary>
/// "Run at this hour every day", for the two sweeps that are timers rather than <c>core.job</c>
/// rows (docs/08 — RetentionPruner 04:00, BackupRunner 03:30).
/// </summary>
public static class DailySchedule
{
    /// <summary>
    /// How long until the next <paramref name="runAt"/>. Always positive: at exactly the run time
    /// the answer is tomorrow, so a run that finishes inside the same minute cannot immediately
    /// start again.
    /// </summary>
    public static TimeSpan UntilNextRun(DateTimeOffset now, TimeOnly runAt)
    {
        DateTimeOffset today = new(DateOnly.FromDateTime(now.Date), runAt, now.Offset);
        return today > now ? today - now : today.AddDays(1) - now;
    }
}
