namespace Sonarr.Domain.Entities.Core;

/// <summary>core.feature_flag — module kill switches. GuildId 0 means global.</summary>
public class FeatureFlag : AuditedEntity
{
    /// <summary>0 = global default for every guild.</summary>
    public long GuildId { get; set; }

    public string Feature { get; set; } = string.Empty;

    public bool State { get; set; }

    public long ChangedBy { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
