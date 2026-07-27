namespace Sonarr.Domain.Entities.Social;

/// <summary>social.event_rsvp — one response per (event, user).</summary>
public class EventRsvp : AuditedEntity
{
    public long EventId { get; set; }

    public long UserId { get; set; }

    /// <summary>See <see cref="RsvpResponse"/>.</summary>
    public string Response { get; set; } = RsvpResponse.Going;

    public SocialEvent? Event { get; set; }
}

/// <summary>social.event_rsvp.response values.</summary>
public static class RsvpResponse
{
    public const string Going = "going";
    public const string Maybe = "maybe";
    public const string No = "no";
}
