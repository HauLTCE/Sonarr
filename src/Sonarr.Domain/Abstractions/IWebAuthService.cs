namespace Sonarr.Domain.Abstractions;

/// <summary>
/// DM-token login, sessions and the panel audit trail (docs/09-web-panels.md).
/// </summary>
/// <remarks>
/// The API layer above this is deliberately thin: everything that decides whether someone gets in
/// — resolution, rate limits, hashing, single-use, the attempt cap, expiry, renewal, revocation —
/// happens here, where it can be tested without a web server.
/// </remarks>
public interface IWebAuthService
{
    /// <summary>
    /// Step 1: issue a code for a Discord handle. <b>Always</b> reports the same thing to the
    /// caller, whatever happened, so the endpoint cannot enumerate users. Returns the raw token and
    /// the user to DM only when one was actually issued — the API never sees this, the DM sender does.
    /// </summary>
    Task<LoginRequest> RequestTokenAsync(
        string? username, string? sourceIp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 2: exchange a code for a session. Returns the raw session id (the cookie value) or a
    /// reason it failed. Consumes an attempt on every wrong guess and kills the token at five.
    /// </summary>
    Task<LoginResult> VerifyAsync(
        string? username,
        string? code,
        bool remember,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a cookie value against Redis and, where it matters, Postgres. Renews the session
    /// when it is past half its life. Null means "not logged in" — expired, revoked or never real.
    /// </summary>
    /// <param name="requireFresh">
    /// Skip the cache and read the row: used by admin and destructive routes, so a revoked session
    /// cannot survive on a mirror.
    /// </param>
    Task<PanelUser?> AuthenticateAsync(
        string? rawSessionId, bool requireFresh, CancellationToken cancellationToken = default);

    /// <summary>Logout. False when the session was already gone.</summary>
    Task<bool> LogoutAsync(string? rawSessionId, CancellationToken cancellationToken = default);

    /// <summary>"Log out everywhere" — returns how many sessions were revoked.</summary>
    Task<int> LogoutAllAsync(ulong userId, CancellationToken cancellationToken = default);

    /// <summary>One <c>web.audit</c> row. Called for every panel write, admin or not.</summary>
    Task AuditAsync(
        PanelUser actor,
        string action,
        string? target,
        ulong guildId = 0,
        object? detail = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What <see cref="IWebAuthService.RequestTokenAsync"/> decided. <see cref="Token"/> is null for
/// every outcome except a fresh issue; the HTTP layer returns 202 regardless.
/// </summary>
/// <param name="Outcome">Why nothing was sent, for the log — never for the response body.</param>
public sealed record LoginRequest(LoginRequestOutcome Outcome, ulong UserId = 0, string? Token = null)
{
    public static readonly LoginRequest Unknown = new(LoginRequestOutcome.UnknownUser);

    public static readonly LoginRequest Throttled = new(LoginRequestOutcome.RateLimited);

    public static readonly LoginRequest BadInput = new(LoginRequestOutcome.InvalidUsername);
}

public enum LoginRequestOutcome
{
    Issued,

    /// <summary>Handle resolved to nobody, or to more than one person.</summary>
    UnknownUser,

    /// <summary>Three requests already in this window, per handle or per IP.</summary>
    RateLimited,

    InvalidUsername,
}

/// <summary>A verified login, or the reason it was refused.</summary>
/// <param name="ExpiresAt">
/// The stored session's expiry, so the cookie and the row agree exactly rather than the API
/// recomputing a value a few milliseconds later.
/// </param>
public sealed record LoginResult(
    LoginOutcome Outcome,
    string? RawSessionId = null,
    ulong UserId = 0,
    DateTimeOffset ExpiresAt = default)
{
    public static readonly LoginResult BadCode = new(LoginOutcome.InvalidCode);

    public static readonly LoginResult Expired = new(LoginOutcome.Expired);

    public static readonly LoginResult TooManyAttempts = new(LoginOutcome.TooManyAttempts);

    public bool Success => Outcome == LoginOutcome.Success;
}

public enum LoginOutcome
{
    Success,

    /// <summary>Wrong code, unparseable code, or a code belonging to someone else.</summary>
    InvalidCode,

    /// <summary>Right code, past its ten minutes or already spent.</summary>
    Expired,

    /// <summary>Five wrong guesses; the token is gone and the visitor must ask for a new one.</summary>
    TooManyAttempts,
}

/// <summary>The signed-in identity behind a request.</summary>
/// <param name="SessionHash">Stored form of the cookie value, for audit rows and revocation.</param>
/// <param name="IsAdmin">Checked against Postgres per request (docs/09), never cached.</param>
/// <param name="RenewedUntil">Set when this request slid the session's expiry — the API resets the cookie.</param>
public sealed record PanelUser(
    ulong UserId,
    string SessionHash,
    bool IsAdmin,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RenewedUntil = null);
