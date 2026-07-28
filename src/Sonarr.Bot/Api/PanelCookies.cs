using System.Security.Cryptography;

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
    /// Whether the browser reached us over HTTPS, and so whether <c>Secure</c> can be set.
    /// </summary>
    /// <remarks>
    /// Kestrel always serves plain HTTP, so the socket says nothing: through the tunnel at
    /// sonarr.hault.io.vn the browser is on HTTPS and the forwarded header says so, while on the
    /// LAN (http://192.168.1.101:3000) it is genuinely HTTP. Hardcoding <c>Secure = true</c> is
    /// right for the tunnel and silently breaks the LAN — the browser accepts the Set-Cookie,
    /// drops it, and login just never sticks with nothing logged anywhere.
    /// <para>The header is only trusted because the sole things in front of this port are the
    /// panel's own rewrite proxy and the tunnel; anything on the LAN can spoof it, but spoofing
    /// it only ever adds <c>Secure</c> to your own cookie, which costs the sender their session
    /// and nobody else anything.</para>
    /// </remarks>
    public static bool IsSecure(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.IsHttps
            || request.Headers["X-Forwarded-Proto"].ToString()
                .Contains("https", StringComparison.OrdinalIgnoreCase);
    }

    public static CookieOptions SessionOptions(bool remember, DateTimeOffset expiresAt, bool secure) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        // A remembered session is the only one that survives closing the browser; the 24 h one is
        // deliberately a session cookie, so a shared machine forgets it on quit.
        Expires = remember ? expiresAt : null,
    };

    public static CookieOptions CsrfOptions(bool remember, DateTimeOffset expiresAt, bool secure) => new()
    {
        HttpOnly = false,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = remember ? expiresAt : null,
    };

    /// <summary>
    /// Deletion options — an expiry in the past, with the same shape the cookie was issued with.
    /// </summary>
    public static CookieOptions DeleteOptions(bool secure) => new()
    {
        HttpOnly = true,
        Secure = secure,
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

    /// <summary>
    /// Issues both cookies after a successful verify. Takes the context rather than the response
    /// because whether <c>Secure</c> is set depends on how the browser got here.
    /// </summary>
    public static void Issue(HttpContext http, string rawSessionId, bool remember, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(http);

        bool secure = IsSecure(http.Request);
        http.Response.Cookies.Append(Session, rawSessionId, SessionOptions(remember, expiresAt, secure));
        http.Response.Cookies.Append(Csrf, NewCsrfToken(), CsrfOptions(remember, expiresAt, secure));
    }

    public static void Clear(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        // The flags follow the request, same as Issue. A cookie's identity is name+domain+path and
        // does not include Secure, so an expiry sent without it still clears one set with it —
        // which is what makes logging out of a tunnel session over the LAN work.
        CookieOptions options = DeleteOptions(IsSecure(http.Request));
        http.Response.Cookies.Delete(Session, options);
        http.Response.Cookies.Delete(Csrf, options);
    }
}
