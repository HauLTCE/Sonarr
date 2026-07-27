using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Music;
using Sonarr.Domain.Music;

namespace Sonarr.Infrastructure.Persistence.Repositories.Music;

/// <inheritdoc cref="IPlaylistRepository"/>
public sealed class PlaylistRepository(SonarrDbContext db) : IPlaylistRepository
{
    public async Task SaveAsync(
        ulong guildId,
        string name,
        ulong ownerId,
        IReadOnlyList<TrackInfo> tracks,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tracks);

        var guild = (long)guildId;
        var trimmed = name.Trim();

        Playlist? playlist = await db.Playlists
            .Include(p => p.Tracks)
            .FirstOrDefaultAsync(p => p.GuildId == guild && p.Name == trimmed, ct);

        if (playlist is null)
        {
            playlist = new Playlist { GuildId = guild, Name = trimmed, OwnerId = (long)ownerId };
            db.Playlists.Add(playlist);
        }
        else
        {
            // Replace, don't merge: "save" means "this is the playlist now".
            db.PlaylistTracks.RemoveRange(playlist.Tracks);
            playlist.Tracks.Clear();
            playlist.UpdatedAt = DateTimeOffset.UtcNow;
        }

        for (var i = 0; i < tracks.Count; i++)
        {
            TrackInfo track = tracks[i];
            playlist.Tracks.Add(new PlaylistTrack
            {
                Position = i,
                Title = Truncate(track.Title, 512),
                Uri = Truncate(track.Uri, 1024),
                DurationMs = (int)Math.Clamp(track.DurationMs, 0, int.MaxValue),
                AddedBy = (long)track.RequesterId,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<PlaylistContents?> GetAsync(ulong guildId, string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var guild = (long)guildId;
        var trimmed = name.Trim();

        Playlist? playlist = await db.Playlists
            .AsNoTracking()
            .Include(p => p.Tracks)
            .FirstOrDefaultAsync(p => p.GuildId == guild && p.Name == trimmed, ct);

        if (playlist is null)
        {
            return null;
        }

        IReadOnlyList<TrackInfo> tracks =
        [
            .. playlist.Tracks
                .OrderBy(t => t.Position)
                .Select(t => new TrackInfo(
                    t.Title,
                    // Author is not stored — the uri is what we replay from, and a saved
                    // playlist row is not worth a second string column.
                    string.Empty,
                    t.Uri,
                    t.Uri,
                    t.DurationMs,
                    (ulong)t.AddedBy)),
        ];

        return new PlaylistContents(playlist.Name, (ulong)playlist.OwnerId, tracks);
    }

    public async Task<IReadOnlyList<PlaylistSummary>> ListAsync(ulong guildId, CancellationToken ct = default)
    {
        var guild = (long)guildId;

        return await db.Playlists
            .AsNoTracking()
            .Where(p => p.GuildId == guild)
            .OrderBy(p => p.Name)
            .Select(p => new PlaylistSummary(p.Name, (ulong)p.OwnerId, p.Tracks.Count))
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(ulong guildId, string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var guild = (long)guildId;
        var trimmed = name.Trim();

        // Tracks go with it via the cascade on playlist_track.
        return await db.Playlists
            .Where(p => p.GuildId == guild && p.Name == trimmed)
            .ExecuteDeleteAsync(ct) > 0;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
