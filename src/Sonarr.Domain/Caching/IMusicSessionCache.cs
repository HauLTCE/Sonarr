namespace Sonarr.Domain.Caching;

/// <summary>
/// Music crash-resume and per-track transient state (Redis area <c>music</c>, docs/05-caching.md).
/// </summary>
/// <remarks>
/// Availability contract: FAILS OPEN. Losing this costs at most "an in-flight session can't resume".
/// The queue snapshot is opaque to this interface — the music service owns its shape.
/// </remarks>
public interface IMusicSessionCache
{
    /// <summary>
    /// Saves the queue + position snapshot (<c>music:session:{guild}</c>, 24 h).
    /// Called on change and on the 30 s refresh tick; every save re-arms the TTL.
    /// </summary>
    Task SaveSessionAsync<TSnapshot>(ulong guildId, TSnapshot snapshot, CancellationToken cancellationToken = default)
        where TSnapshot : class;

    /// <summary>Snapshot for <c>/resume</c>, or <c>null</c> when there is nothing to resume.</summary>
    Task<TSnapshot?> GetSessionAsync<TSnapshot>(ulong guildId, CancellationToken cancellationToken = default)
        where TSnapshot : class;

    Task ClearSessionAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a vote-skip vote (<c>music:voteskip:{guild}</c>). Returns the distinct vote count.
    /// The set is cleared when the track ends; it also carries a safety TTL so a lost
    /// track-end event cannot leak the key.
    /// </summary>
    Task<int> AddVoteSkipAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    Task<int> GetVoteSkipCountAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Clears the tally — call on track end.</summary>
    Task ClearVoteSkipAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Opens the 10 s undo window with the track that was just skipped (<c>music:undo_skip:{guild}</c>).</summary>
    Task SetUndoSkipAsync<TTrack>(ulong guildId, TTrack track, CancellationToken cancellationToken = default)
        where TTrack : class;

    /// <summary>The skipped track if the 10 s window is still open, otherwise <c>null</c>.</summary>
    Task<TTrack?> GetUndoSkipAsync<TTrack>(ulong guildId, CancellationToken cancellationToken = default)
        where TTrack : class;

    /// <summary>Now-playing embed to keep edited (<c>music:np_msg:{guild}</c>, session-length TTL).</summary>
    Task SetNowPlayingMessageAsync(ulong guildId, ulong messageId, CancellationToken cancellationToken = default);

    Task<ulong?> GetNowPlayingMessageAsync(ulong guildId, CancellationToken cancellationToken = default);

    Task ClearNowPlayingMessageAsync(ulong guildId, CancellationToken cancellationToken = default);
}
