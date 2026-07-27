using System.Text.Json.Nodes;

namespace Sonarr.Domain.Entities.Core;

/// <summary>
/// core.guild_config — the single config system. Writes invalidate the Redis
/// config cache (docs/05-caching.md, key <c>cfg:guild:{guild}</c>).
/// </summary>
public class GuildConfig : AuditedEntity
{
    public long GuildId { get; set; }

    /// <summary>e.g. welcome_channel, log_channel, autorole_id, music_channel, levelup_channel, dj_role, timezone.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>jsonb payload; shape validated by the Config service.</summary>
    public JsonObject Value { get; set; } = new();

    /// <summary>Audit: who last wrote this key.</summary>
    public long UpdatedBy { get; set; }
}
