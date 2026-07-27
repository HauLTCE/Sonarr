namespace Sonarr.Domain.Entities.Music;

/// <summary>music.playlist_track — one entry of a playlist, ordered by Position.</summary>
public class PlaylistTrack : AuditedEntity
{
    public long PlaylistId { get; set; }

    /// <summary>0-based ordinal inside the playlist; part of the key.</summary>
    public int Position { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;

    public int DurationMs { get; set; }

    public long AddedBy { get; set; }

    public Playlist? Playlist { get; set; }
}
