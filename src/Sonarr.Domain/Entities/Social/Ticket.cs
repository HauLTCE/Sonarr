namespace Sonarr.Domain.Entities.Social;

/// <summary>social.ticket — /ticket private mod thread.</summary>
public class Ticket : AuditedEntity
{
    public long TicketId { get; set; }

    public long GuildId { get; set; }

    public long OpenerId { get; set; }

    public long ThreadId { get; set; }

    /// <summary>See <see cref="TicketStatus"/>.</summary>
    public string Status { get; set; } = TicketStatus.Open;

    /// <summary>Pointer to the saved transcript (file path or message link).</summary>
    public string? TranscriptRef { get; set; }

    public long? ClosedBy { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>social.ticket.status values.</summary>
public static class TicketStatus
{
    public const string Open = "open";
    public const string Closed = "closed";
}
