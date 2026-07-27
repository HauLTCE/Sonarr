using Sonarr.Domain.Music;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>music.playlist</c> + <c>music.playlist_track</c> (docs/04-database.md). Names are unique
/// per guild and a playlist belongs to whoever saved it — the owner check lives in the service,
/// this only reports <see cref="PlaylistSummary.OwnerId"/>.
/// </summary>
public interface IPlaylistRepository
{
    /// <summary>
    /// Saves (or replaces) a named playlist. Replacing rewrites the whole track list, which is
    /// why the tracks arrive in one call: a half-written playlist is worse than the old one.
    /// </summary>
    Task SaveAsync(
        ulong guildId,
        string name,
        ulong ownerId,
        IReadOnlyList<TrackInfo> tracks,
        CancellationToken cancellationToken = default);

    /// <summary>The playlist's tracks in order, or <c>null</c> when there is no such name here.</summary>
    Task<PlaylistContents?> GetAsync(ulong guildId, string name, CancellationToken cancellationToken = default);

    /// <summary>Every playlist in the guild, name-ordered, for <c>/playlist list</c> and autocomplete.</summary>
    Task<IReadOnlyList<PlaylistSummary>> ListAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary><c>false</c> when nothing matched. Ownership is checked before this is called.</summary>
    Task<bool> DeleteAsync(ulong guildId, string name, CancellationToken cancellationToken = default);
}

/// <param name="TrackCount">Shown in the list so people can spot the empty ones.</param>
public sealed record PlaylistSummary(string Name, ulong OwnerId, int TrackCount);

public sealed record PlaylistContents(string Name, ulong OwnerId, IReadOnlyList<TrackInfo> Tracks);
