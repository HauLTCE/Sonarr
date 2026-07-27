using Sonarr.Domain.Configuration;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// Kill switches, checked at the Controller layer (docs/02-architecture.md#cross-cutting).
/// Precedence: the guild row wins over the global (guild_id = 0) row, and a feature with no
/// row anywhere is on — a fresh guild works without any setup.
/// </summary>
public interface IFeatureGate
{
    Task<bool> IsEnabledAsync(string feature, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Every catalog feature with its effective state and where that state came from.</summary>
    Task<IReadOnlyList<FeatureState>> GetAllAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Sets this guild's row and drops the cached flags.</summary>
    Task<ConfigWriteResult> SetAsync(
        string feature, ulong guildId, bool enabled, ulong actorId, CancellationToken cancellationToken = default);
}
