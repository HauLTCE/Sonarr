namespace Sonarr.Domain.Caching;

/// <summary>
/// Mirror of a <c>web.session</c> row (<c>web:session:{hash}</c>). Postgres stays the authority:
/// revocation is checked against the DB on sensitive routes, this only skips a DB hit per request.
/// </summary>
public sealed record WebSessionSnapshot(long UserId, DateTimeOffset ExpiresAt, bool Remember);

/// <summary>
/// Web panel transient state (Redis area <c>web</c>, docs/05-caching.md).
/// </summary>
/// <remarks>
/// Availability contract: FAILS OPEN — a miss sends the caller to Postgres, which is the
/// authority for session validity. This is safe precisely because the cache can only
/// affirm a session the DB already issued, never extend one past its own expiry: the
/// mirror's TTL is the session expiry.
/// </remarks>
public interface IWebSessionCache
{
    /// <summary>Cached session, or <c>null</c> on a miss (caller falls back to the DB).</summary>
    Task<WebSessionSnapshot?> GetSessionAsync(string sessionHash, CancellationToken cancellationToken = default);

    /// <summary>Mirrors a session; the TTL is derived from <see cref="WebSessionSnapshot.ExpiresAt"/>.</summary>
    Task SetSessionAsync(string sessionHash, WebSessionSnapshot session, CancellationToken cancellationToken = default);

    /// <summary>Drops the mirror on logout or revocation.</summary>
    Task RemoveSessionAsync(string sessionHash, CancellationToken cancellationToken = default);

    /// <summary>Live-status blob for the status page (<c>web:live_status</c>, 5 s), or <c>null</c> when stale.</summary>
    Task<string?> GetLiveStatusJsonAsync(CancellationToken cancellationToken = default);

    Task SetLiveStatusJsonAsync(string json, CancellationToken cancellationToken = default);
}
