namespace Sonarr.Domain.Entities.Stats;

/// <summary>
/// stats.command_usage — private analytics: which commands are worth keeping.
/// One row per (guild, command, day).
/// </summary>
public class CommandUsage : AuditedEntity
{
    public long GuildId { get; set; }

    public string Command { get; set; } = string.Empty;

    public DateOnly Day { get; set; }

    public long Count { get; set; }
}
