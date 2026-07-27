using System.Text.Json.Nodes;

namespace Sonarr.Domain.Entities.Web;

/// <summary>
/// web.audit — admin-panel actions: who toggled or edited what, when, from which session.
/// </summary>
public class WebAudit : AuditedEntity
{
    public long AuditId { get; set; }

    public long UserId { get; set; }

    /// <summary>Hashed session id the action came from; null for system actions.</summary>
    public string? SessionId { get; set; }

    /// <summary>0 when the action is not guild-scoped.</summary>
    public long GuildId { get; set; }

    /// <summary>e.g. config.set, flag.toggle, case.delete.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>What was acted on (config key, feature name, case id…).</summary>
    public string? Target { get; set; }

    /// <summary>jsonb: before/after detail.</summary>
    public JsonObject Detail { get; set; } = new();

    public DateTimeOffset At { get; set; }
}
