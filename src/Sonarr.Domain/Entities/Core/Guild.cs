namespace Sonarr.Domain.Entities.Core;

/// <summary>core.guild — one row per Discord guild the bot is in.</summary>
public class Guild : AuditedEntity
{
    public long GuildId { get; set; }

    /// <summary>Cached from the gateway, so a guild row is legible without a Discord lookup.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset JoinedAt { get; set; }
}
