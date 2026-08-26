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

    /// <summary>
    /// Command usage, the activity series and member growth for one guild over a window. One call
    /// rather than three — the three are always read together. No caller on the current surface.
    /// </summary>
    /// <param name="days">Window length, clamped by the implementation.</param>
    Task<GuildStats> GetStatsAsync(long guildId, int days, CancellationToken ct = default);
}

/// <param name="Commands">Most-used first, summed over the window.</param>
/// <param name="Activity">One point per hour bucket, oldest first.</param>
/// <param name="Growth">Members first seen per day, oldest first — the growth chart.</param>
public sealed record GuildStats(
    int Days,
    IReadOnlyList<CommandUsageCount> Commands,
    IReadOnlyList<ActivityPoint> Activity,
    IReadOnlyList<MemberGrowthPoint> Growth);

public sealed record CommandUsageCount(string Command, long Count);

public sealed record ActivityPoint(DateTimeOffset HourBucket, int Messages, int VoiceUsers, int OnlineEstimate);

public sealed record MemberGrowthPoint(DateOnly Day, int Joined);
