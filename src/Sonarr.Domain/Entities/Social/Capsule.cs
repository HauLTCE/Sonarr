namespace Sonarr.Domain.Entities.Social;

/// <summary>
/// social.capsule — /capsule write. Delivery is driven by a paired core.job row,
/// so it survives restarts; this table holds the content.
/// </summary>
public class Capsule : AuditedEntity
{
    public long CapsuleId { get; set; }

    public long GuildId { get; set; }

    public long AuthorId { get; set; }

    public long ChannelId { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset DeliverAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }
}
