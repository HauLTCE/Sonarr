using Sonarr.Domain.Entities.Core;

namespace Sonarr.Domain.Abstractions;

/// <summary>core.member access.</summary>
public interface IMemberRepository
{
    Task<Member?> GetAsync(long guildId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Batched write: bump message_count and last_active_at, and <b>create the row if it is not
    /// there yet</b>. This is the only path that inserts into <c>core.member</c> — every other
    /// write here is an UPDATE, so a member Sonarr has never seen speak has no row for
    /// <c>/birthday</c> or <c>/timezone</c> to land on.
    /// </summary>
    Task ApplyActivityAsync(IReadOnlyCollection<MemberActivityDelta> deltas, CancellationToken ct = default);

    Task SetTimezoneAsync(long guildId, long userId, string? ianaTimezone, CancellationToken ct = default);

    Task SetBirthdayAsync(long guildId, long userId, DateOnly? birthday, CancellationToken ct = default);

    /// <summary>Birthday announcer: everyone in the guild whose month+day matches.</summary>
    Task<IReadOnlyList<Member>> GetBirthdaysAsync(long guildId, int month, int day, CancellationToken ct = default);

    /// <summary>
    /// Anniversary announcer: everyone in the guild who was first seen on this month+day in an
    /// earlier year. Same shape as <see cref="GetBirthdaysAsync"/>, over <c>first_seen_at</c>.
    /// </summary>
    Task<IReadOnlyList<Member>> GetJoinAnniversariesAsync(
        long guildId, int month, int day, CancellationToken ct = default);

    /// <summary>
    /// The guilds this user is a member of, named rather than just ids. Unused by the current
    /// surface.
    /// </summary>
    Task<IReadOnlyList<MemberGuild>> GetGuildsAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// The user id behind a Discord handle, or null when nobody here has it. Case-insensitive, and
    /// null when two rows disagree — the cached username is not unique across guilds, and DMing the
    /// wrong person is worse than answering nobody. Unused by the current surface.
    /// </summary>
    Task<long?> FindUserIdByUsernameAsync(string username, CancellationToken ct = default);
}

/// <summary>One guild a user belongs to, with its name.</summary>
public sealed record MemberGuild(long GuildId, string Name);

/// <summary>
/// One member's accumulated activity since the last flush, plus the identity fields needed to
/// create the row on first sight. <paramref name="JoinedAt"/> is Discord's own join timestamp —
/// it becomes <c>first_seen_at</c>, so <c>/anniversary</c> reports the date they actually joined
/// rather than the day Sonarr happened to notice them. Null when the gateway has no member object
/// cached, in which case now() is the only honest answer.
/// </summary>
public readonly record struct MemberActivityDelta(
    long GuildId,
    long UserId,
    long MessageCount,
    DateTimeOffset LastActiveAt,
    string Username = "",
    string DisplayName = "",
    DateTimeOffset? JoinedAt = null);
