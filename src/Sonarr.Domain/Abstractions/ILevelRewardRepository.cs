using Sonarr.Domain.Entities.Levels;

namespace Sonarr.Domain.Abstractions;

/// <summary>levels.reward access — the role-per-level table.</summary>
public interface ILevelRewardRepository
{
    /// <summary>Every reward for the guild, lowest level first.</summary>
    Task<IReadOnlyList<LevelReward>> GetAllAsync(long guildId, CancellationToken ct = default);

    /// <summary>Rewards at or below <paramref name="level"/> — what a member should hold.</summary>
    Task<IReadOnlyList<LevelReward>> GetEarnedAsync(long guildId, int level, CancellationToken ct = default);

    /// <summary>Adds or repoints the reward for one level (guild+level is the key).</summary>
    Task SetAsync(long guildId, int level, long roleId, CancellationToken ct = default);

    /// <summary><c>false</c> when there was no reward at that level.</summary>
    Task<bool> RemoveAsync(long guildId, int level, CancellationToken ct = default);
}
