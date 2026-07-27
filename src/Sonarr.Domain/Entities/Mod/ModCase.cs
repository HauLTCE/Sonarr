using System.Text.Json.Nodes;

namespace Sonarr.Domain.Entities.Mod;

/// <summary>
/// mod.case — every moderation action, case-numbered for /case 123.
/// Also mirrored to Serilog console output (the "CLI log" requirement).
/// </summary>
public class ModCase : AuditedEntity
{
    public long CaseId { get; set; }

    public long GuildId { get; set; }

    public long TargetId { get; set; }

    public long ActorId { get; set; }

    /// <summary>See <see cref="ModAction"/>.</summary>
    public string Action { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>Set for tempban/timeout; paired with a core.job row that lifts it.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>jsonb: purge filters and match count, slowmode seconds, etc.</summary>
    public JsonObject Context { get; set; } = new();
}

/// <summary>Allowed mod.case.action values (docs/04-database.md).</summary>
public static class ModAction
{
    public const string Warn = "warn";
    public const string Kick = "kick";
    public const string Ban = "ban";
    public const string TempBan = "tempban";
    public const string Timeout = "timeout";
    public const string Untimeout = "untimeout";
    public const string Unban = "unban";
    public const string Purge = "purge";
    public const string Slowmode = "slowmode";
    public const string Note = "note";
}
