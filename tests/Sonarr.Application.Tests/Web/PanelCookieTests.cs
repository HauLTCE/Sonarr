using Microsoft.AspNetCore.Http;
using Sonarr.Bot.Api;

namespace Sonarr.Application.Tests.Web;

/// <summary>
/// Which door the browser came in by, and what that does to the cookie flags (docs/09).
/// </summary>
/// <remarks>
/// The panel is reachable two ways: HTTPS through the tunnel at sonarr.hault.io.vn, and plain
/// HTTP on the LAN. <c>Secure</c> used to be hardcoded true, which is correct for the first and
/// silently fatal for the second — the browser takes the Set-Cookie, discards it, and login just
/// never sticks with nothing logged on either side. These pin the distinction, because it is
/// invisible in every test that does not involve a real browser.
/// </remarks>
public class PanelCookieTests
{
    [Fact]
    public void The_tunnel_gets_secure_cookies()
    {
        DefaultHttpContext http = new();
        http.Request.Headers["X-Forwarded-Proto"] = "https";

        Assert.True(PanelCookies.IsSecure(http.Request));
    }

    [Fact]
    public void A_direct_https_request_gets_secure_cookies()
    {
        DefaultHttpContext http = new();
        http.Request.Scheme = "https";

        Assert.True(PanelCookies.IsSecure(http.Request));
    }

    [Fact]
    public void Plain_http_on_the_lan_does_not()
    {
        // No header, no TLS: http://192.168.1.101:3000. Secure here would drop the session.
        Assert.False(PanelCookies.IsSecure(new DefaultHttpContext().Request));
    }

    [Fact]
    public void A_proxy_chain_still_counts_as_https()
    {
        // Next's rewrite proxy forwards the header it received, so the value can be a list.
        DefaultHttpContext http = new();
        http.Request.Headers["X-Forwarded-Proto"] = "https,http";

        Assert.True(PanelCookies.IsSecure(http.Request));
    }

    [Fact]
    public void Logout_expires_both_cookies_over_plain_http()
    {
        // Cookie identity is name+domain+path and excludes Secure, so one expiry clears a session
        // issued either way. Sending a second copy with the other flag does not work anyway:
        // ResponseCookies.Delete keys by name, so it replaces rather than adds.
        DefaultHttpContext http = new();

        PanelCookies.Clear(http);

        string all = string.Join(" ", http.Response.Headers.SetCookie.ToArray()!);

        Assert.Equal(2, http.Response.Headers.SetCookie.Count);
        Assert.Contains(PanelCookies.Session, all, StringComparison.Ordinal);
        Assert.Contains(PanelCookies.Csrf, all, StringComparison.Ordinal);
        Assert.DoesNotContain("secure", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Logout_over_the_tunnel_matches_the_flags_it_issued()
    {
        DefaultHttpContext http = new();
        http.Request.Headers["X-Forwarded-Proto"] = "https";

        PanelCookies.Clear(http);

        string all = string.Join(" ", http.Response.Headers.SetCookie.ToArray()!);

        Assert.Contains("secure", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_session_cookie_stays_http_only_either_way()
    {
        // The LAN concession is Secure and nothing else: HttpOnly and SameSite are what stop a
        // script reading the session and a cross-site form carrying it.
        foreach (bool secure in (bool[])[true, false])
        {
            CookieOptions session = PanelCookies.SessionOptions(
                remember: false, DateTimeOffset.UtcNow.AddDays(1), secure);

            Assert.True(session.HttpOnly);
            Assert.Equal(SameSiteMode.Lax, session.SameSite);
            Assert.Equal(secure, session.Secure);
        }
    }

    [Fact]
    public void The_csrf_cookie_is_readable_because_the_panel_has_to_echo_it()
    {
        CookieOptions csrf = PanelCookies.CsrfOptions(
            remember: true, DateTimeOffset.UtcNow.AddDays(30), secure: true);

        Assert.False(csrf.HttpOnly);
        Assert.NotNull(csrf.Expires);
    }
}
