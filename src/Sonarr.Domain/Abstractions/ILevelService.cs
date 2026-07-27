using Sonarr.Domain.Configuration;
using Sonarr.Domain.Levels;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// The levels module's business rules (docs/07-commands.md#levels). Domain types only: the
/// Discord handler and the web panel both call this.
/// </summary>
public interface ILevelService
{
    /// <summary>
    /// Awards message XP if the 60 s cooldown allows it, touches the streak and grants the
    /// once-a-day bonus. The cooldown FAILS CLOSED: no Redis means no XP.
    /// </summary>
    /// <param name="today">The date in the guild's timezone — the caller owns the clock.</param>
    Task<XpAward> AwardMessageXpAsync(
        ulong guildId,
        ulong userId,
        ulong channelId,
        DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Credits one accrual tick of voice time. Returns a skip when the anti-AFK rule says the
    /// time does not count (fewer than two humans, or muted).
    /// </summary>
    /// <param name="humanCount">Non-bot members in the channel, including this user.</param>
    Task<XpAward> AwardVoiceXpAsync(
        ulong guildId,
        ulong userId,
        ulong channelId,
        int humanCount,
        bool muted,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default);

    Task<LevelCard> GetCardAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    Task<LevelComparison> CompareAsync(
        ulong guildId, ulong leftUserId, ulong rightUserId, CancellationToken cancellationToken = default);

    Task<MemberStats> GetStatsAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary><paramref name="seasonId"/> <c>null</c> = the live standings.</summary>
    Task<LeaderboardPage> GetLeaderboardAsync(
        ulong guildId, long? seasonId, int page, CancellationToken cancellationToken = default);

    /// <summary>Seasons available to <c>/leaderboard season</c>, newest first.</summary>
    Task<IReadOnlyList<SeasonSummary>> GetSeasonsAsync(ulong guildId, CancellationToken cancellationToken = default);

    Task<LevelsPolicy> GetPolicyAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Configured role rewards, lowest level first.</summary>
    Task<IReadOnlyList<RoleReward>> GetRewardsAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Adds or repoints the reward at one level. Rejects out-of-range levels.</summary>
    Task<ConfigWriteResult> SetRewardAsync(
        ulong guildId, int level, ulong roleId, CancellationToken cancellationToken = default);

    Task<ConfigWriteResult> RemoveRewardAsync(ulong guildId, int level, CancellationToken cancellationToken = default);
}

/// <summary>A configured level → role reward.</summary>
public sealed record RoleReward(int Level, ulong RoleId);

/// <summary>A season as the leaderboard picker sees it.</summary>
public sealed record SeasonSummary(long SeasonId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status);
