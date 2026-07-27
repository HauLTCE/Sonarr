namespace Sonarr.Domain.Entities.Levels;

/// <summary>levels.season — a scoring period; closing it fills season_result.</summary>
public class Season : AuditedEntity
{
    public long SeasonId { get; set; }

    public long GuildId { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    /// <summary>See <see cref="SeasonStatus"/>.</summary>
    public string Status { get; set; } = SeasonStatus.Scheduled;

    public ICollection<SeasonResult> Results { get; set; } = [];
}

/// <summary>
/// levels.season.status values. The doc says only "status"; these three are the
/// boring minimum the season_close job needs.
/// </summary>
public static class SeasonStatus
{
    public const string Scheduled = "scheduled";
    public const string Active = "active";
    public const string Closed = "closed";
}
