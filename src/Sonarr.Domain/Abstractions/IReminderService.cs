using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Utility;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>/remind</c> and <c>/reminders</c> (docs/07-commands.md#utility). Every reminder is a
/// <c>core.job</c> row, so nothing is lost on restart — there is no in-process timer anywhere in
/// this path (docs/05-caching.md: the job table is the authority).
/// </summary>
/// <remarks>Domain models only: the caller resolves ids, this owns parsing, storage and text.</remarks>
public interface IReminderService
{
    /// <summary>
    /// Parses <paramref name="when"/> in the user's timezone (falling back to the guild's, then
    /// UTC) and writes the job row. A recurring phrase produces one row with a recurrence.
    /// </summary>
    Task<ScheduleResult> CreateAsync(
        ulong guildId,
        ulong channelId,
        ulong userId,
        string when,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>The user's pending reminders, soonest first.</summary>
    Task<IReadOnlyList<ReminderView>> ListAsync(
        ulong userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels one of the user's reminders. False when it is not theirs, already ran, or gone —
    /// ownership is enforced in the query, not by trusting the caller.
    /// </summary>
    Task<bool> CancelAsync(ulong userId, long jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a claimed reminder job into its delivery, or <c>null</c> when the payload is
    /// unusable (a hand-edited row) — the handler drops those rather than retrying forever.
    /// </summary>
    ReminderDelivery? Read(Job job);

    /// <summary>
    /// The next occurrence for a recurring reminder, in the owner's timezone, or <c>null</c> for a
    /// one-shot. The scheduler passes it straight to <c>IJobRepository.CompleteAsync</c>.
    /// </summary>
    Task<DateTimeOffset?> NextOccurrenceAsync(Job job, CancellationToken cancellationToken = default);

    /// <summary>Stores (or clears, with <c>null</c>) the user's IANA timezone — <c>/timezone</c>.</summary>
    Task<ScheduleResult> SetTimezoneAsync(
        ulong guildId,
        ulong userId,
        string? ianaId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The zone reminders are read in: the member's own, else the guild's, else UTC. Used by
    /// <c>/timestamp</c> too, so both commands agree on "your time".
    /// </summary>
    Task<TimeZoneInfo> ResolveZoneAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default);
}
