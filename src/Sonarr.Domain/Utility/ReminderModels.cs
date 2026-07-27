namespace Sonarr.Domain.Utility;

/// <summary>
/// A pending reminder as the user sees it in <c>/reminders list</c> — the domain view of a
/// <c>core.job</c> row, with no jsonb or EF types leaking out.
/// </summary>
/// <param name="JobId">Also the cancel handle.</param>
/// <param name="Schedule">Human phrase: "once", "every day", …</param>
public sealed record ReminderView(
    long JobId,
    DateTimeOffset RunAt,
    string Text,
    string? Recurrence,
    string Schedule);

/// <summary>Outcome of scheduling something. <paramref name="Message"/> is user-facing.</summary>
public sealed record ScheduleResult(bool Success, string Message, long JobId = 0)
{
    public static ScheduleResult Ok(string message, long jobId) => new(true, message, jobId);

    public static ScheduleResult Rejected(string reason) => new(false, reason);
}

/// <summary>What a reminder needs to be delivered, resolved from the job payload.</summary>
public sealed record ReminderDelivery(
    ulong GuildId,
    ulong ChannelId,
    ulong UserId,
    string Text);

/// <summary>What a scheduled announcement needs to be posted.</summary>
public sealed record AnnounceDelivery(
    ulong GuildId,
    ulong ChannelId,
    string Text);
