using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Application.Config;

/// <inheritdoc cref="IFeatureGate"/>
public sealed class FeatureGate(
    IFeatureFlagRepository repository,
    IConfigCache cache,
    ILogger<FeatureGate> log) : IFeatureGate
{
    /// <summary>
    /// The shape stored under <c>cfg:flags</c> (docs/05-caching.md). One blob for every guild, so
    /// it has to say which guilds it actually covers — a guild missing from
    /// <see cref="Guilds"/> has not been read yet, which is not the same as having no flags.
    /// </summary>
    /// <param name="Guilds">Guild ids whose rows are present. 0 = the global row set.</param>
    /// <param name="Flags"><c>"{guildId}:{feature}" -&gt; state</c>.</param>
    public sealed record FlagCache(HashSet<ulong> Guilds, Dictionary<string, bool> Flags);

    public async Task<bool> IsEnabledAsync(
        string feature, ulong guildId, CancellationToken cancellationToken = default)
    {
        FlagCache flags = await LoadAsync(guildId, cancellationToken);
        return Resolve(flags, feature, guildId).Enabled;
    }

    public async Task<IReadOnlyList<FeatureState>> GetAllAsync(
        ulong guildId, CancellationToken cancellationToken = default)
    {
        FlagCache flags = await LoadAsync(guildId, cancellationToken);
        return [.. FeatureNames.All.Select(f => Resolve(flags, f, guildId))];
    }

    public async Task<ConfigWriteResult> SetAsync(
        string feature, ulong guildId, bool enabled, ulong actorId, CancellationToken cancellationToken = default)
    {
        // Resolve first so `sonarr set sleep false` writes the sleep_mode row rather than
        // creating a second, never-read one under the alias.
        var normalized = FeatureNames.Resolve(feature);
        if (!FeatureNames.IsKnown(normalized))
        {
            return ConfigWriteResult.Rejected($"`{feature}` isn't one of my modules.");
        }

        await repository.SetAsync((long)guildId, normalized, enabled, (long)actorId, cancellationToken);
        await cache.InvalidateFlagsAsync(cancellationToken);

        log.LogInformation("Feature {Feature} turned {State} for guild {GuildId} by {ActorId}",
            normalized, enabled ? "on" : "off", guildId, actorId);

        return ConfigWriteResult.Ok($"**{normalized}** is now {(enabled ? "on" : "off")}.");
    }

    private static FeatureState Resolve(FlagCache cached, string feature, ulong guildId)
    {
        var normalized = FeatureNames.Resolve(feature);

        if (cached.Flags.TryGetValue(FlagKey(guildId, normalized), out var guildState))
        {
            return new FeatureState(normalized, guildState, FeatureStateSource.Guild);
        }

        if (cached.Flags.TryGetValue(FlagKey(0, normalized), out var globalState))
        {
            return new FeatureState(normalized, globalState, FeatureStateSource.Global);
        }

        // No row anywhere. A module defaults on — a guild that has never touched /feature gets the
        // whole bot — but a restriction defaults off, because "enabled" for sleep_mode means the bot
        // goes quiet at 22:00, and nobody asked it to. See FeatureNames.Restrictions.
        return new FeatureState(normalized, FeatureNames.DefaultState(normalized), FeatureStateSource.Default);
    }

    private async Task<FlagCache> LoadAsync(ulong guildId, CancellationToken cancellationToken)
    {
        FlagCache? cached = await cache.GetFlagsAsync<FlagCache>(cancellationToken);
        if (cached is not null && cached.Guilds.Contains(guildId))
        {
            return cached;
        }

        // GetAllAsync returns this guild's rows plus the global ones, so one read covers both
        // precedence levels.
        IReadOnlyList<FeatureFlag> rows = await repository.GetAllAsync((long)guildId, cancellationToken);

        var merged = new FlagCache(
            cached is null ? [] : [.. cached.Guilds],
            cached is null ? [] : new Dictionary<string, bool>(cached.Flags));

        merged.Guilds.Add(guildId);
        merged.Guilds.Add(0);
        foreach (FeatureFlag row in rows)
        {
            merged.Flags[FlagKey((ulong)row.GuildId, row.Feature)] = row.State;
        }

        await cache.SetFlagsAsync(merged, cancellationToken);
        return merged;
    }

    private static string FlagKey(ulong guildId, string feature) => $"{guildId}:{feature}";
}
