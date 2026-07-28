using Sonarr.Domain.Music;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>music.play_history</c> and <c>music.track_rating</c>: what got played and what the room
/// thought of it. Feeds <c>/musicstats</c>, <c>/mytracks</c>, <c>/toptracks</c> and smart autoplay.
/// </summary>
public interface IMusicStatsRepository
{
    /// <summary>Appends a play. Called once per track start — keep it cheap.</summary>
    Task RecordPlayAsync(ulong guildId, TrackInfo track, CancellationToken cancellationToken = default);

    /// <summary>Aggregates for <c>/musicstats</c>.</summary>
    Task<MusicStats> GetStatsAsync(ulong guildId, int top, CancellationToken cancellationToken = default);

    /// <summary>The user's most recent requests, newest first, de-duplicated by uri (<c>/mytracks</c>).</summary>
    Task<IReadOnlyList<TrackPlayCount>> GetUserHistoryAsync(
        ulong guildId, ulong userId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts one vote. <paramref name="vote"/> is +1 or -1; the same value twice is idempotent,
    /// the opposite value flips it. Returns the track's tally afterwards so the embed can update.
    /// </summary>
    Task<RatedTrack> RateAsync(
        ulong guildId,
        string uri,
        string title,
        ulong userId,
        short vote,
        CancellationToken cancellationToken = default);

    /// <summary>Current tally for one track, or <c>null</c> when nobody has voted.</summary>
    Task<RatedTrack?> GetRatingAsync(ulong guildId, string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// One user's own votes, newest first — the panel's "my ratings" (docs/09). The tally is the
    /// whole guild's, so the row reads "you liked this, the room is +3".
    /// </summary>
    Task<IReadOnlyList<MyRating>> GetUserRatingsAsync(
        ulong guildId, ulong userId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Crowd favourites, best score first (<c>/toptracks</c>).</summary>
    Task<IReadOnlyList<RatedTrack>> GetTopRatedAsync(
        ulong guildId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uris the guild has voted net-negative. Smart autoplay skips these
    /// (docs/checklist.md — "avoids net-negative tracks").
    /// </summary>
    Task<IReadOnlyList<string>> GetDislikedUrisAsync(ulong guildId, CancellationToken cancellationToken = default);
}
