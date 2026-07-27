namespace Sonarr.Domain.Entities.Social;

/// <summary>
/// social.event — /event create. Mirrors a native Discord scheduled event and adds
/// opt-in role pings.
/// </summary>
public class SocialEvent : AuditedEntity
{
    public long EventId { get; set; }

    public long GuildId { get; set; }

    public long CreatorId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Discord's own scheduled-event id, when one was created.</summary>
    public long? DiscordEventId { get; set; }

    /// <summary>Role pinged on reminder; opt-in via RSVP.</summary>
    public long? PingRoleId { get; set; }

    /// <summary>See <see cref="SocialEventStatus"/>.</summary>
    public string Status { get; set; } = SocialEventStatus.Scheduled;

    public ICollection<EventRsvp> Rsvps { get; set; } = [];
}

/// <summary>social.event.status values.</summary>
public static class SocialEventStatus
{
    public const string Scheduled = "scheduled";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
}
