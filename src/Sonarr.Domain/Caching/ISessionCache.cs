namespace Sonarr.Domain.Caching;

/// <summary>Live conversation session (<c>chat:session:{guild}:{user}</c>, 10 min sliding).</summary>
/// <param name="MessageCount">Turns in this session — feeds "message 6 of a back-and-forth".</param>
/// <param name="StartedAt">When the session began (absent session = "first contact in days").</param>
/// <param name="LastTurnAt">Timestamp of the most recent turn.</param>
public sealed record ChatSessionState(int MessageCount, DateTimeOffset StartedAt, DateTimeOffset LastTurnAt);

/// <summary>One entry of the channel ring buffer (<c>chat:ring:{channel}</c>). Metadata only — no content.</summary>
public sealed record RecentMessage(ulong AuthorId, DateTimeOffset SentAt, bool MentionedHer);

/// <summary>
/// Chat-engine transient state (Redis area <c>chat</c>, docs/05-caching.md).
/// </summary>
/// <remarks>
/// Availability contract: FAILS OPEN. Reads degrade to a cache miss and writes are swallowed
/// (both logged) — losing this state costs at most "the conversation feels reset".
/// </remarks>
public interface ISessionCache
{
    /// <summary>Reads the session without extending it. <c>null</c> = no live session.</summary>
    Task<ChatSessionState?> GetSessionAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a turn: creates or increments the session and slides the 10 min TTL.
    /// Returns the state after the increment.
    /// </summary>
    Task<ChatSessionState> TouchSessionAsync(
        ulong guildId,
        ulong userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>She asked something (<c>chat:pending_q</c>, 10 min). Expiry unanswered is itself the signal.</summary>
    Task SetPendingQuestionAsync(ulong guildId, ulong userId, string questionId, CancellationToken cancellationToken = default);

    Task<string?> GetPendingQuestionAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    Task ClearPendingQuestionAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message she replied to (<c>chat:replied:{channel}:{message}</c>, 1 h) with the content
    /// hash she saw, which is what makes edit detection possible inside the window.
    /// </summary>
    Task MarkRepliedAsync(ulong channelId, ulong messageId, string stateHash, CancellationToken cancellationToken = default);

    /// <summary>The hash she replied to, or <c>null</c> if outside the edit-detection window.</summary>
    Task<string?> GetRepliedStateHashAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken = default);

    /// <summary>Pushes onto the capped-10 channel ring buffer (15 min).</summary>
    Task PushRecentMessageAsync(ulong channelId, RecentMessage message, CancellationToken cancellationToken = default);

    /// <summary>Newest-first channel ring buffer, at most 10 entries.</summary>
    Task<IReadOnlyList<RecentMessage>> GetRecentMessagesAsync(ulong channelId, CancellationToken cancellationToken = default);

    /// <summary>Counts one reply against the channel engagement budget (1 h window). Returns the new count.</summary>
    Task<int> IncrementEngagementAsync(ulong channelId, CancellationToken cancellationToken = default);

    /// <summary>Current engagement count for the channel window (0 when absent).</summary>
    Task<int> GetEngagementAsync(ulong channelId, CancellationToken cancellationToken = default);

    /// <summary>Hash of the last ping content (<c>chat:lastping</c>, 5 min) — "pinged twice with nothing new".</summary>
    Task<string?> GetLastPingHashAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    Task SetLastPingHashAsync(ulong guildId, ulong userId, string contentHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hot cache over the person row (<c>chat:hot</c>, 5 min sliding) to skip a DB read mid-conversation.
    /// Generic on purpose: the cache owns the key and the TTL, the caller owns the shape.
    /// </summary>
    Task<TPerson?> GetHotPersonAsync<TPerson>(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
        where TPerson : class;

    /// <summary>Write-through on person save.</summary>
    Task SetHotPersonAsync<TPerson>(ulong guildId, ulong userId, TPerson person, CancellationToken cancellationToken = default)
        where TPerson : class;
}
