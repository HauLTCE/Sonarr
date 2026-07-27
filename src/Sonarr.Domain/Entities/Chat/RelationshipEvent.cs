using System.Text.Json.Nodes;

namespace Sonarr.Domain.Entities.Chat;

/// <summary>
/// chat.relationship_event — trajectory, not just level. Enables #trend#
/// ("you've been almost tolerable this week") and multi-day grudge decay.
/// </summary>
public class RelationshipEvent : AuditedEntity
{
    public long Id { get; set; }

    public long GuildId { get; set; }

    public long UserId { get; set; }

    /// <summary>jsonb: register deltas applied by this event.</summary>
    public JsonObject Delta { get; set; } = new();

    /// <summary>Intent id that caused the change.</summary>
    public string Cause { get; set; } = string.Empty;

    public long Turn { get; set; }

    /// <summary>Column name is <c>at</c> in the doc.</summary>
    public DateTimeOffset At { get; set; }
}
