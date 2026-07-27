namespace Sonarr.Domain.Entities.Music;

/// <summary>
/// music.track_rating — one vote per (guild, uri, user).
/// Feeds /toptracks; autoplay skips net-negative tracks.
/// </summary>
public class TrackRating : AuditedEntity
{
    public long GuildId { get; set; }

    public string Uri { get; set; } = string.Empty;

    public long UserId { get; set; }

    /// <summary>+1 or -1 (smallint); enforced by a check constraint.</summary>
    public short Vote { get; set; }
}
