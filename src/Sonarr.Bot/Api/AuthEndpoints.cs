using Sonarr.Bot.Discord.Web;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.Api;

/// <summary>
/// <c>/api/auth/*</c> — the DM-token login flow (docs/09-web-panels.md).
/// </summary>
/// <remarks>
/// Thin on purpose. Every decision lives in <see cref="IWebAuthService"/>; these handlers translate
/// its answers into status codes and cookies. The one rule enforced here rather than there is that
/// <c>request-token</c> returns the same 202 body for every outcome — including throttling — so the
/// endpoint leaks nothing about who has an account.
/// </remarks>
public static class AuthEndpoints
{
    /// <summary>The only body request-token ever returns.</summary>
    public const string NeutralMessage =
        "If that account is known here, a login code has been sent by DM. Codes last 10 minutes.";

    public static IEndpointRouteBuilder MapSonarrAuth(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup("/api/auth");

        group.MapPost("/request-token", RequestTokenAsync);
        group.MapPost("/verify", VerifyAsync);
        group.MapPost("/logout", LogoutAsync);
        group.MapPost("/logout-all", LogoutAllAsync);

        return routes;
    }

    private static async Task<IResult> RequestTokenAsync(
        LoginRequestBody body,
        HttpContext http,
        IWebAuthService auth,
        LoginTokenSender sender,
        ILogger<LoginTokenSender> log,
        CancellationToken ct)
    {
        LoginRequest request = await auth.RequestTokenAsync(body?.Username, ClientIp(http), ct);

        if (request is { Outcome: LoginRequestOutcome.Issued, Token: { } token })
        {
            // The DM is best-effort and its result is not reported: closed DMs and a delivered code
            // must look identical from outside (docs/09).
            await sender.SendAsync(request.UserId, token, ct);
        }

        return Results.Accepted(value: new { message = NeutralMessage });
    }

    private static async Task<IResult> VerifyAsync(
        LoginVerifyBody body,
        HttpContext http,
        IWebAuthService auth,
        CancellationToken ct)
    {
        LoginResult result = await auth.VerifyAsync(
            body?.Username,
            body?.Code,
            body?.Remember ?? false,
            http.Request.Headers.UserAgent.ToString(),
            ct);

        if (!result.Success || result.RawSessionId is null)
        {
            // 429 for the attempt cap so the panel can say "ask for a new code" rather than
            // "wrong code" — the token is gone either way.
            return result.Outcome == LoginOutcome.TooManyAttempts
                ? Results.StatusCode(StatusCodes.Status429TooManyRequests)
                : Results.Unauthorized();
        }

        PanelCookies.Issue(http, result.RawSessionId, body?.Remember ?? false, result.ExpiresAt);

        // The id is a string: JSON numbers lose snowflake precision in JavaScript.
        return Results.Ok(new
        {
            userId = result.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            expiresAt = result.ExpiresAt,
        });
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http, IWebAuthService auth, CancellationToken ct)
    {
        await auth.LogoutAsync(http.Request.Cookies[PanelCookies.Session], ct);
        PanelCookies.Clear(http);

        // Always 204: a logout that finds nothing already achieved what the caller wanted.
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(
        HttpContext http, IWebAuthService auth, CancellationToken ct)
    {
        // Fresh read: this is the "someone has my cookie" button, so it must not trust the mirror.
        PanelUser? user = await auth.AuthenticateAsync(
            http.Request.Cookies[PanelCookies.Session], requireFresh: true, ct);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!PanelCookies.CsrfMatches(http.Request))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var revoked = await auth.LogoutAllAsync(user.UserId, ct);
        PanelCookies.Clear(http);

        return Results.Ok(new { revoked });
    }

    /// <summary>
    /// The caller's address. Behind the tunnel the socket is always loopback, so the forwarded
    /// header is the only real signal — and it is only trusted because nothing but the tunnel can
    /// reach this port (docs/02: Kestrel is not exposed).
    /// </summary>
    private static string? ClientIp(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (first.Length > 0)
            {
                return first;
            }
        }

        return http.Connection.RemoteIpAddress?.ToString();
    }
}

/// <param name="Username">Discord handle, with or without the leading @.</param>
public sealed record LoginRequestBody(string? Username);

public sealed record LoginVerifyBody(string? Username, string? Code, bool Remember);
