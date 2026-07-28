using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.Api;

/// <summary>
/// <c>/api/me/*</c> — what she knows about you, and the two buttons that make her forget
/// (docs/06-data-and-privacy.md, docs/09-web-panels.md).
/// </summary>
/// <remarks>
/// Every query here is keyed by the session's own user id, never by anything in the request. There
/// is deliberately no "look at user X" route on this group: the admin panel gets counts, not other
/// people's memories (docs/06).
/// </remarks>
public static class MeEndpoints
{
    /// <summary>
    /// What the delete endpoints require in the body. Typing the word is the cooling-off step —
    /// docs/06 asks for a confirmation the user cannot click through by accident.
    /// </summary>
    public const string ConfirmWord = "DELETE";

    /// <summary>
    /// Said on every deletion. Backups are kept for 7 days (docs/07), so "gone" is honest about
    /// the live database and honest about the tape too.
    /// </summary>
    public const string BackupNotice =
        "Removed from the live database. Nightly backups are kept 7 days and then rotate out, "
        + "so a copy may exist until then. Backups are never read back except to restore the "
        + "whole database after a failure.";

    public static IEndpointRouteBuilder MapSonarrMe(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup("/api/me");

        group.MapGet("/", GetMeAsync);
        group.MapGet("/data", GetDataAsync);
        group.MapGet("/export", ExportAsync);
        group.MapDelete("/chat-memory", DeleteChatMemoryAsync);
        group.MapDelete("/everything", DeleteEverythingAsync);

        return routes;
    }

    /// <summary>Who the cookie belongs to — the panel's session probe.</summary>
    private static async Task<IResult> GetMeAsync(
        HttpContext http, IWebAuthService auth, CancellationToken ct)
    {
        PanelUser? me = await Me(http, auth, requireFresh: false, ct);

        return me is null
            ? Results.Unauthorized()
            : Results.Ok(new
            {
                userId = Id(me.UserId),
                isAdmin = me.IsAdmin,
                expiresAt = me.RenewedUntil ?? me.ExpiresAt,
            });
    }

    private static async Task<IResult> GetDataAsync(
        HttpContext http, IWebAuthService auth, IUserDataRepository data, CancellationToken ct)
    {
        PanelUser? me = await Me(http, auth, requireFresh: false, ct);
        if (me is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await data.ExportAsync((long)me.UserId, ct));
    }

    /// <summary>The same payload as <c>/data</c>, as a file — docs/06's "download everything".</summary>
    private static async Task<IResult> ExportAsync(
        HttpContext http, IWebAuthService auth, IUserDataRepository data, CancellationToken ct)
    {
        PanelUser? me = await Me(http, auth, requireFresh: false, ct);
        if (me is null)
        {
            return Results.Unauthorized();
        }

        UserDataExport export = await data.ExportAsync((long)me.UserId, ct);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            export, new JsonSerializerOptions { WriteIndented = true });

        return Results.File(
            json,
            "application/json",
            $"sonarr-data-{Id(me.UserId)}-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    // [FromBody] is load-bearing on both of these, and nullable is what makes it legal. Minimal
    // APIs will not *infer* a body for DELETE, and an inferred complex parameter here threw
    // "Body was inferred but the method does not allow inferred body parameters" while building
    // the endpoint table — which is lazy and global, so it 500'd every route in the app including
    // /health. Being explicit opts the body in; nullable marks it optional, so a missing body
    // reaches the ConfirmWord check below and gets our own 400 rather than a framework one.
    private static Task<IResult> DeleteChatMemoryAsync(
        [FromBody] DeleteConfirmation? body,
        HttpContext http,
        IWebAuthService auth,
        IUserDataRepository data,
        CancellationToken ct) =>
        DeleteAsync(body, http, auth, "me.delete_chat_memory", data.DeleteChatMemoryAsync, ct);

    private static Task<IResult> DeleteEverythingAsync(
        [FromBody] DeleteConfirmation? body,
        HttpContext http,
        IWebAuthService auth,
        IUserDataRepository data,
        CancellationToken ct) =>
        DeleteAsync(body, http, auth, "me.delete_everything", data.DeleteEverythingAsync, ct);

    private static async Task<IResult> DeleteAsync(
        DeleteConfirmation? body,
        HttpContext http,
        IWebAuthService auth,
        string action,
        Func<long, CancellationToken, Task<UserDeletion>> erase,
        CancellationToken ct)
    {
        // Fresh read and a CSRF check: irreversible, so neither a stale mirror nor a cross-site
        // POST gets to trigger it.
        PanelUser? me = await Me(http, auth, requireFresh: true, ct);
        if (me is null)
        {
            return Results.Unauthorized();
        }

        if (!PanelCookies.CsrfMatches(http.Request))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!string.Equals(body?.Confirm?.Trim(), ConfirmWord, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = $"Type {ConfirmWord} to confirm." });
        }

        UserDeletion result = await erase((long)me.UserId, ct);

        // Audited before returning: the row is the only trace left of a wipe, and writing it after
        // /everything is fine — web.audit is not in the deletion set (it records who acted, and
        // for a self-delete the actor is also the subject).
        await auth.AuditAsync(me, action, target: Id(me.UserId), detail: result.Deleted, cancellationToken: ct);

        return Results.Ok(new { deleted = result.Deleted, total = result.Total, note = BackupNotice });
    }

    private static Task<PanelUser?> Me(
        HttpContext http, IWebAuthService auth, bool requireFresh, CancellationToken ct) =>
        auth.AuthenticateAsync(http.Request.Cookies[PanelCookies.Session], requireFresh, ct);

    /// <summary>Snowflakes go out as strings; JSON numbers lose precision in JavaScript.</summary>
    private static string Id(ulong value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <param name="Confirm">Must be the literal word DELETE.</param>
public sealed record DeleteConfirmation(string? Confirm);
