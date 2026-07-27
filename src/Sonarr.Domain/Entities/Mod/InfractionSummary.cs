namespace Sonarr.Domain.Entities.Mod;

/// <summary>
/// mod.infraction_summary — read-only view over mod.case, per (guild, target):
/// warn/kick/ban counts for /userinfo. Keyless; never written.
/// </summary>
public class InfractionSummary
{
    public long GuildId { get; set; }

    public long TargetId { get; set; }

    public long WarnCount { get; set; }

    public long KickCount { get; set; }

    public long BanCount { get; set; }

    public DateTimeOffset? LastCaseAt { get; set; }
}
