using Sonarr.Domain.Entities.Core;

namespace Sonarr.Domain.Abstractions;

/// <summary>core.feature_flag access — backs IFeatureGate's kill switches.</summary>
public interface IFeatureFlagRepository
{
    /// <summary>
    /// Guild rows plus the global (guild_id = 0) rows, which the gate falls back to.
    /// </summary>
    Task<IReadOnlyList<FeatureFlag>> GetAllAsync(long guildId, CancellationToken ct = default);

    Task SetAsync(long guildId, string feature, bool state, long changedBy, CancellationToken ct = default);
}
