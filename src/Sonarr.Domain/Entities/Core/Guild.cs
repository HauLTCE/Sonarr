namespace Sonarr.Domain.Entities.Core;

/// <summary>core.guild — one row per Discord guild the bot is in.</summary>
public class Guild : AuditedEntity
{
    public long GuildId { get; set; }

    /// <summary>Cached guild name for panel display.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset JoinedAt { get; set; }
}
