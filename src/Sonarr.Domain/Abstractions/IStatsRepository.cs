namespace Sonarr.Domain.Abstractions;

/// <summary>
/// stats.* writes: private analytics only (docs/06-data-and-privacy.md — counts, never message
/// content). Both methods are upserts, because both writers are timer- or event-driven and will
/// hit the same (guild, day) or (guild, hour) key repeatedly.
/// </summary>
public interface IStatsRepository
{
    /// <summary>
    /// Adds <paramref name="delta"/> to <c>stats.command_usage</c> for (guild, command, day).
    /// </summary>
    /// <param name="command">Slash command name — no arguments, no user text.</param>
    Task IncrementCommandAsync(
        long guildId,
        string command,
        DateOnly day,
        long delta = 1,
        CancellationToken ct = default);

    /// <summary>
    /// Upserts one <c>stats.activity_sample</c> hour bucket. Online/voice counts overwrite (the
    /// latest sample in the hour is the sample), message counts add.
    /// </summary>
    Task RecordActivityAsync(
        long guildId,
        DateTimeOffset hourBucket,
        int onlineEstimate,
        int voiceUsers,
        int messageDelta = 0,
        CancellationToken ct = default);
}
