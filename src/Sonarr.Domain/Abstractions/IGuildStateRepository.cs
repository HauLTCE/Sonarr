using Sonarr.Domain.Chat;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>chat.guild_state.event_log</c>: the handful of things that happened to the room rather than
/// to one person (docs/04). Counts and timestamps only — never who, never what was said.
/// </summary>
public interface IGuildStateRepository
{
    /// <summary>Newest first. Empty for a guild she has never seen do anything.</summary>
    Task<IReadOnlyList<GuildEvent>> GetEventsAsync(long guildId, CancellationToken ct = default);

    /// <summary>
    /// Appends an online-count record if <paramref name="online"/> beats the standing one, and
    /// reports whether it did.
    /// </summary>
    /// <remarks>
    /// The comparison lives with the row rather than in the caller: the sampler would otherwise
    /// read, decide, and write, and two sampler passes racing would lose one record.
    /// </remarks>
    Task<bool> RecordOnlineRecordAsync(
        long guildId, int online, DateTimeOffset at, CancellationToken ct = default);
}
