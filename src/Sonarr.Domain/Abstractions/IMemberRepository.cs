using Sonarr.Domain.Entities.Core;

namespace Sonarr.Domain.Abstractions;

/// <summary>core.member access.</summary>
public interface IMemberRepository
{
    Task<Member?> GetAsync(long guildId, long userId, CancellationToken ct = default);

    /// <summary>Insert-or-refresh the cached username/display name on gateway events.</summary>
    Task<Member> UpsertAsync(long guildId, long userId, string username, string displayName, CancellationToken ct = default);

    /// <summary>
    /// Batched write from Redis: bump message_count and last_active_at in one round trip.
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
}

/// <summary>One member's accumulated activity since the last flush.</summary>
public readonly record struct MemberActivityDelta(
    long GuildId,
    long UserId,
    long MessageCount,
    DateTimeOffset LastActiveAt);
