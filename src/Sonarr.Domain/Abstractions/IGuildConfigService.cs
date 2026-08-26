using Sonarr.Domain.Configuration;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// The one config system (docs/04-database.md#coreguild_config). Owns validation against the
/// closed key catalog and Redis invalidation, so a write applies immediately everywhere.
/// Takes and returns domain types only — no caller's transport shape leaks in.
/// </summary>
public interface IGuildConfigService
{
    /// <summary>Every set key for the guild, keyed by config key. Reads go through the cache.</summary>
    Task<IReadOnlyDictionary<string, ConfigValue>> GetAllAsync(
        ulong guildId, CancellationToken cancellationToken = default);

    /// <summary><c>null</c> when the key is unset (or unknown).</summary>
    Task<ConfigValue?> GetAsync(ulong guildId, string key, CancellationToken cancellationToken = default);

    /// <summary>Validates then writes; a bad value is rejected with a reason and nothing is stored.</summary>
    Task<ConfigWriteResult> SetAsync(
        ulong guildId, string key, string value, ulong actorId, CancellationToken cancellationToken = default);

    /// <summary>Clears a key back to its default.</summary>
    Task<ConfigWriteResult> ClearAsync(
        ulong guildId, string key, ulong actorId, CancellationToken cancellationToken = default);

    /// <summary>The guild's config as indented JSON, ready to hand to an admin as a file.</summary>
    Task<string> ExportAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates every key in <paramref name="json"/> first: one bad entry rejects the whole
    /// import, so config is never left half-applied.
    /// </summary>
    Task<ConfigImportResult> ImportAsync(
        ulong guildId, string json, ulong actorId, CancellationToken cancellationToken = default);
}
