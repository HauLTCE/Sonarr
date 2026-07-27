using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Utility;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>/announce</c> (docs/07-commands.md#server-management): post now, or schedule a
/// one-shot / recurring announcement as a <c>core.job</c> row so it survives a restart.
/// </summary>
public interface IAnnounceService
{
    /// <summary>
    /// Schedules the announcement. <paramref name="schedule"/> is parsed in the guild's timezone
    /// (the actor's does not decide when the server hears from Sonarr).
    /// </summary>
    Task<ScheduleResult> ScheduleAsync(
        ulong guildId,
        ulong channelId,
        ulong actorId,
        string schedule,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a claimed announce job, or <c>null</c> when the payload is unusable.</summary>
    AnnounceDelivery? Read(Job job);

    /// <summary>Next occurrence for a recurring announcement, in the guild's timezone.</summary>
    Task<DateTimeOffset?> NextOccurrenceAsync(Job job, CancellationToken cancellationToken = default);
}
