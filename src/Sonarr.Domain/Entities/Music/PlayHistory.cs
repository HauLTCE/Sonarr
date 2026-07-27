namespace Sonarr.Domain.Entities.Music;

/// <summary>
/// music.play_history — append-only log of what got played.
/// Feeds /musicstats, /mytracks and smart autoplay.
/// </summary>
public class PlayHistory : AuditedEntity
{
    public long Id { get; set; }

    public long GuildId { get; set; }

    public long RequesterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;

    public DateTimeOffset PlayedAt { get; set; }

    public int DurationMs { get; set; }
}
