using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Application.Config;

/// <inheritdoc cref="IGuildConfigService"/>
public sealed class GuildConfigService(
    IGuildConfigRepository repository,
    IConfigCache cache,
    ILogger<GuildConfigService> log) : IGuildConfigService
{
    private static readonly JsonSerializerOptions ExportOptions = new() { WriteIndented = true };

    /// <summary>The jsonb shape: <c>{"value": "..."}</c> — one canonical string per key.</summary>
    private const string ValueProperty = "value";

    public async Task<IReadOnlyDictionary<string, ConfigValue>> GetAllAsync(
        ulong guildId, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string>? cached =
            await cache.GetGuildConfigAsync<Dictionary<string, string>>(guildId, cancellationToken);

        if (cached is null)
        {
            IReadOnlyList<GuildConfig> rows = await repository.GetAllAsync((long)guildId, cancellationToken);
            cached = rows
                .Where(r => r.Value[ValueProperty] is not null)
                .ToDictionary(r => r.Key, r => r.Value[ValueProperty]!.GetValue<string>());

            await cache.SetGuildConfigAsync(guildId, cached, cancellationToken);
        }

        // Keys dropped from the catalog stay in Postgres but are not served — the catalog is
        // the contract, and an orphan row should not resurrect a retired feature.
        return cached
            .Select(kv => ConfigKeys.TryGet(kv.Key, out ConfigKeyDefinition? d)
                ? new ConfigValue(d, kv.Value)
                : null)
            .Where(v => v is not null)
            .ToDictionary(v => v!.Definition.Key, v => v!);
    }

    public async Task<ConfigValue?> GetAsync(
        ulong guildId, string key, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, ConfigValue> all = await GetAllAsync(guildId, cancellationToken);
        return ConfigKeys.TryGet(key, out ConfigKeyDefinition? definition)
               && all.TryGetValue(definition.Key, out ConfigValue? value)
            ? value
            : null;
    }

    public async Task<ConfigWriteResult> SetAsync(
        ulong guildId, string key, string value, ulong actorId, CancellationToken cancellationToken = default)
    {
        if (!ConfigKeys.TryValidate(key, value, out var canonical, out var error))
        {
            return ConfigWriteResult.Rejected(error);
        }

        // TryValidate already proved the key is in the catalog.
        ConfigKeys.TryGet(key, out ConfigKeyDefinition? definition);

        await repository.SetAsync(
            (long)guildId,
            definition!.Key,
            new JsonObject { [ValueProperty] = canonical },
            (long)actorId,
            cancellationToken);

        await cache.InvalidateGuildConfigAsync(guildId, cancellationToken);
        log.LogInformation("Config {Key} set for guild {GuildId} by {ActorId}", definition.Key, guildId, actorId);

        return ConfigWriteResult.Ok($"`{definition.Key}` is now `{canonical}`.");
    }

    public async Task<ConfigWriteResult> ClearAsync(
        ulong guildId, string key, ulong actorId, CancellationToken cancellationToken = default)
    {
        if (!ConfigKeys.TryGet(key, out ConfigKeyDefinition? definition))
        {
            return ConfigWriteResult.Rejected($"`{key}` isn't a config key I know.");
        }

        var removed = await repository.RemoveAsync((long)guildId, definition.Key, cancellationToken);
        await cache.InvalidateGuildConfigAsync(guildId, cancellationToken);

        if (!removed)
        {
            return ConfigWriteResult.Ok($"`{definition.Key}` wasn't set — nothing to clear.");
        }

        log.LogInformation("Config {Key} cleared for guild {GuildId} by {ActorId}", definition.Key, guildId, actorId);
        return ConfigWriteResult.Ok($"`{definition.Key}` is back to its default.");
    }

    public async Task<string> ExportAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, ConfigValue> all = await GetAllAsync(guildId, cancellationToken);
        return JsonSerializer.Serialize(
            all.ToDictionary(kv => kv.Key, kv => kv.Value.Raw),
            ExportOptions);
    }

    public async Task<ConfigImportResult> ImportAsync(
        ulong guildId, string json, ulong actorId, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string>? incoming;
        try
        {
            // Numbers and booleans in the file are fine; JsonObject keeps them as JsonValue and
            // ToString() gives us the same text a user would have typed.
            JsonObject? parsed = JsonNode.Parse(json) as JsonObject;
            if (parsed is null)
            {
                return ConfigImportResult.Rejected(["That isn't a JSON object — export one first and edit that."]);
            }

            incoming = parsed.ToDictionary(
                p => p.Key,
                p => p.Value?.ToString() ?? string.Empty);
        }
        catch (JsonException ex)
        {
            return ConfigImportResult.Rejected([$"That isn't valid JSON: {ex.Message}"]);
        }

        // Validate everything before writing anything — a half-applied config is worse than a
        // rejected one.
        var rejections = new List<string>();
        var accepted = new List<(string Key, string Value)>();
        foreach ((var key, var raw) in incoming)
        {
            if (ConfigKeys.TryValidate(key, raw, out var canonical, out var error))
            {
                accepted.Add((key, canonical));
            }
            else
            {
                rejections.Add(error);
            }
        }

        if (rejections.Count > 0)
        {
            return ConfigImportResult.Rejected(rejections);
        }

        // ponytail: validate-then-write, not a DB transaction. Every value is already proven
        // valid, so the only way to stop mid-loop is Postgres going down. Upgrade path: a
        // repository-level SetManyAsync in one transaction if that ever matters.
        foreach ((var key, var canonical) in accepted)
        {
            ConfigKeys.TryGet(key, out ConfigKeyDefinition? definition);
            await repository.SetAsync(
                (long)guildId,
                definition!.Key,
                new JsonObject { [ValueProperty] = canonical },
                (long)actorId,
                cancellationToken);
        }

        await cache.InvalidateGuildConfigAsync(guildId, cancellationToken);
        log.LogInformation("Config import applied {Count} key(s) to guild {GuildId} by {ActorId}",
            accepted.Count, guildId, actorId);

        return ConfigImportResult.Ok(accepted.Count);
    }
}
