using Sonarr.Domain.Entities.Web;

namespace Sonarr.Domain.Abstractions;

/// <summary><c>web.login_token</c> and <c>web.session</c> access (docs/04, docs/09).</summary>
/// <remarks>
/// Everything here takes and returns <em>hashes</em>. The raw token lives in one DM and the raw
/// session id in one cookie; neither ever reaches this layer, so a database dump cannot be
/// replayed into a session.
/// </remarks>
public interface IWebAuthRepository
{
    /// <summary>Stores an issued token. Replaces any outstanding one for the same user.</summary>
    Task AddTokenAsync(LoginToken token, CancellationToken ct = default);

    /// <summary>The token row for a hash, used or not — the caller decides what that means.</summary>
    Task<LoginToken?> GetTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Flips <c>used</c>, but only from unused: single-use is enforced here, in the WHERE, so two
    /// simultaneous verifies of the same code cannot both mint a session. False = already spent.
    /// </summary>
    Task<bool> ConsumeTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Kills every outstanding token for a user (attempt cap reached, or a new request).</summary>
    Task<int> DeleteTokensAsync(long userId, CancellationToken ct = default);

    Task AddSessionAsync(WebSession session, CancellationToken ct = default);

    /// <summary>The session row, whatever its state. Postgres is the authority for revocation.</summary>
    Task<WebSession?> GetSessionAsync(string sessionHash, CancellationToken ct = default);

    /// <summary>Sliding renewal — pushes the expiry out. No-op on a revoked row.</summary>
    Task RenewSessionAsync(string sessionHash, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>Revokes one session (logout). False when it was already gone or revoked.</summary>
    Task<bool> RevokeSessionAsync(string sessionHash, CancellationToken ct = default);

    /// <summary>"Log out everywhere" — returns the hashes revoked so the caller can drop the mirrors.</summary>
    Task<IReadOnlyList<string>> RevokeAllSessionsAsync(long userId, CancellationToken ct = default);

    /// <summary>The user's live sessions, newest first — the "sessions" block on the my-data page.</summary>
    Task<IReadOnlyList<WebSession>> GetActiveSessionsAsync(long userId, CancellationToken ct = default);

    /// <summary>One row per panel action (docs/09: every admin write is audited).</summary>
    Task AddAuditAsync(WebAudit entry, CancellationToken ct = default);

    /// <summary>Audit browser, newest first.</summary>
    Task<IReadOnlyList<WebAudit>> GetAuditAsync(int skip, int take, CancellationToken ct = default);
}
