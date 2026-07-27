namespace Sonarr.Domain.Entities.Music;

/// <summary>music.playlist — a saved queue. Name is unique per guild.</summary>
public class Playlist : AuditedEntity
{
    public long PlaylistId { get; set; }

    public long GuildId { get; set; }

    public string Name { get; set; } = string.Empty;

    public long OwnerId { get; set; }

    public ICollection<PlaylistTrack> Tracks { get; set; } = [];
}
