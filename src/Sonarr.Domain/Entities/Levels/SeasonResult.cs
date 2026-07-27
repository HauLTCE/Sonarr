namespace Sonarr.Domain.Entities.Levels;

/// <summary>
/// levels.season_result — frozen standings, written at season close.
/// Feeds "top chatter of the month".
/// </summary>
public class SeasonResult : AuditedEntity
{
    public long SeasonId { get; set; }

    public long UserId { get; set; }

    public long XpEarned { get; set; }

    public int Rank { get; set; }

    public Season? Season { get; set; }
}
