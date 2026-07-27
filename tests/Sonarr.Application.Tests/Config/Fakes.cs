using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Application.Tests.Config;

/// <summary>In-memory core.guild_config. Records writes so tests can assert on them.</summary>
internal sealed class FakeGuildConfigRepository : IGuildConfigRepository
{
    private readonly Dictionary<(long GuildId, string Key), GuildConfig> _rows = [];

    public List<string> WrittenKeys { get; } = [];

    /// <summary>How many times the service actually hit "Postgres" — proves the cache is used.</summary>
    public int GetAllCalls { get; private set; }

    public Task<GuildConfig?> GetAsync(long guildId, string key, CancellationToken ct = default)
        => Task.FromResult(_rows.GetValueOrDefault((guildId, key)));

    public Task<IReadOnlyList<GuildConfig>> GetAllAsync(long guildId, CancellationToken ct = default)
    {
        GetAllCalls++;
        return Task.FromResult<IReadOnlyList<GuildConfig>>(
            [.. _rows.Values.Where(r => r.GuildId == guildId).OrderBy(r => r.Key)]);
    }

    public Task SetAsync(long guildId, string key, JsonObject value, long updatedBy, CancellationToken ct = default)
    {
        WrittenKeys.Add(key);
        _rows[(guildId, key)] = new GuildConfig
        {
            GuildId = guildId,
            Key = key,
            Value = value,
            UpdatedBy = updatedBy,
        };
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(long guildId, string key, CancellationToken ct = default)
        => Task.FromResult(_rows.Remove((guildId, key)));
}

/// <summary>In-memory core.feature_flag.</summary>
internal sealed class FakeFeatureFlagRepository : IFeatureFlagRepository
{
    private readonly List<FeatureFlag> _rows = [];

    /// <summary>Seeds a row the way an admin toggle would have. GuildId 0 = global.</summary>
    public FakeFeatureFlagRepository With(long guildId, string feature, bool state)
    {
        _rows.Add(new FeatureFlag { GuildId = guildId, Feature = feature, State = state });
        return this;
    }

    public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(long guildId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeatureFlag>>(
            [.. _rows.Where(f => f.GuildId == guildId || f.GuildId == 0)]);

    public Task SetAsync(long guildId, string feature, bool state, long changedBy, CancellationToken ct = default)
    {
        _rows.RemoveAll(f => f.GuildId == guildId && f.Feature == feature);
        _rows.Add(new FeatureFlag
        {
            GuildId = guildId,
            Feature = feature,
            State = state,
            ChangedBy = changedBy,
        });
        return Task.CompletedTask;
    }
}

/// <summary>
/// Round-trips through JSON like the real Redis cache does, so a test catches a payload the
/// production cache could not serialize. Counts invalidations.
/// </summary>
internal sealed class FakeConfigCache : IConfigCache
{
    private readonly Dictionary<ulong, string> _guildConfig = [];
    private string? _flags;

    public int GuildInvalidations { get; private set; }

    public int FlagInvalidations { get; private set; }

    public Task<TConfig?> GetGuildConfigAsync<TConfig>(ulong guildId, CancellationToken cancellationToken = default)
        where TConfig : class
        => Task.FromResult(_guildConfig.TryGetValue(guildId, out var json)
            ? JsonSerializer.Deserialize<TConfig>(json)
            : null);

    public Task SetGuildConfigAsync<TConfig>(ulong guildId, TConfig config, CancellationToken cancellationToken = default)
        where TConfig : class
    {
        _guildConfig[guildId] = JsonSerializer.Serialize(config);
        return Task.CompletedTask;
    }

    public Task InvalidateGuildConfigAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        GuildInvalidations++;
        _guildConfig.Remove(guildId);
        return Task.CompletedTask;
    }

    public Task<TFlags?> GetFlagsAsync<TFlags>(CancellationToken cancellationToken = default)
        where TFlags : class
        => Task.FromResult(_flags is null ? null : JsonSerializer.Deserialize<TFlags>(_flags));

    public Task SetFlagsAsync<TFlags>(TFlags flags, CancellationToken cancellationToken = default)
        where TFlags : class
    {
        _flags = JsonSerializer.Serialize(flags);
        return Task.CompletedTask;
    }

    public Task InvalidateFlagsAsync(CancellationToken cancellationToken = default)
    {
        FlagInvalidations++;
        _flags = null;
        return Task.CompletedTask;
    }
}

internal static class Build
{
    public const ulong Guild = 111UL;
    public const ulong Actor = 222UL;

    public static (Sonarr.Application.Config.GuildConfigService Service, FakeGuildConfigRepository Repo, FakeConfigCache Cache)
        ConfigService()
    {
        var repo = new FakeGuildConfigRepository();
        var cache = new FakeConfigCache();
        return (new Sonarr.Application.Config.GuildConfigService(
            repo, cache, NullLogger<Sonarr.Application.Config.GuildConfigService>.Instance), repo, cache);
    }

    public static (Sonarr.Application.Config.FeatureGate Gate, FakeConfigCache Cache)
        Gate(FakeFeatureFlagRepository repo)
    {
        var cache = new FakeConfigCache();
        return (new Sonarr.Application.Config.FeatureGate(
            repo, cache, NullLogger<Sonarr.Application.Config.FeatureGate>.Instance), cache);
    }
}
