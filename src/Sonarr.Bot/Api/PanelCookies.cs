using System.Security.Cryptography;
using Sonarr.Domain.Web;

namespace Sonarr.Bot.Api;

/// <summary>
/// The two cookies the panel uses and the CSRF rule between them (docs/09).
/// </summary>
/// <remarks>
/// Double-submit rather than a server-side CSRF store: the session cookie is <c>SameSite=Lax</c>,
/// so a cross-site <em>form</em> POST already cannot carry it, and the header echo covers the
/// remaining case of a same-site script the panel did not serve. Nothing to store, nothing to expire.
/// </remarks>
public static class PanelCookies
{
    public const string Session = "sonarr_session";

    /// <summary>Readable by the panel's JS on purpose — it has to echo the value into the header.</summary>
    public const string Csrf = "sonarr_csrf";

    public const string CsrfHeader = "X-CSRF-Token";

    public static string NewCsrfToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Session cookie options. <c>Secure</c> even though Kestrel serves plain HTTP: the browser only
    /// ever talks to the tunnel, which is HTTPS, and the flag is about the browser's behaviour.
    /// </summary>
    public static CookieOptions SessionOptions(bool remember, DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        // A remembered session is the only one that survives closing the browser; the 24 h one is
        // deliberately a session cookie, so a shared machine forgets it on quit.
        Expires = remember ? expiresAt : null,
    };

    public static CookieOptions CsrfOptions(bool remember, DateTimeOffset expiresAt) => new()
    {
        HttpOnly = false,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = remember ? expiresAt : null,
    };

    public static CookieOptions DeleteOptions => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
    };

    /// <summary>
    /// Double-submit check. Constant-time compare so the endpoint cannot be used as an oracle, and
    /// a missing value on either side is a failure rather than a match.
    /// </summary>
    public static bool CsrfMatches(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cookie = request.Cookies[Csrf];
        var header = request.Headers[CsrfHeader].ToString();

        return !string.IsNullOrEmpty(cookie)
            && !string.IsNullOrEmpty(header)
            && CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(cookie),
                System.Text.Encoding.UTF8.GetBytes(header));
    }

    /// <summary>Issues both cookies after a successful verify.</summary>
    public static void Issue(HttpResponse response, string rawSessionId, bool remember, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(Session, rawSessionId, SessionOptions(remember, expiresAt));
        response.Cookies.Append(Csrf, NewCsrfToken(), CsrfOptions(remember, expiresAt));
    }

    /// <summary>Re-stamps the session cookie after a sliding renewal, keeping the CSRF value.</summary>
    public static void Renew(HttpResponse response, string rawSessionId, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        var remember = expiresAt - DateTimeOffset.UtcNow > WebAuthRules.SessionLifetime;
        response.Cookies.Append(Session, rawSessionId, SessionOptions(remember, expiresAt));
    }

    public static void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(Session, DeleteOptions);
        response.Cookies.Delete(Csrf, DeleteOptions);
    }
}
