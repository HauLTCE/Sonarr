namespace Sonarr.Domain.Entities.Stats;

/// <summary>
/// stats.activity_sample — hourly guild activity bucket. Fuels server stats,
/// "remembers server events", and inactivity signals.
/// </summary>
public class ActivitySample : AuditedEntity
{
    public long GuildId { get; set; }

    /// <summary>Truncated to the hour; part of the key.</summary>
    public DateTimeOffset HourBucket { get; set; }

    public int Messages { get; set; }

    public int VoiceUsers { get; set; }

    public int OnlineEstimate { get; set; }
}
