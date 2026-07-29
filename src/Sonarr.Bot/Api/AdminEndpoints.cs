using System.Globalization;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Web;
using Sonarr.Domain.Moderation;
using Sonarr.Domain.Web;

namespace Sonarr.Bot.Api;

/// <summary>
/// <c>/api/admin/*</c> — config, flags, cases and the audit browser (docs/09), for the
/// allow-listed accounts only.
/// </summary>
/// <remarks>
/// Three gates on every route, in this order: a fresh session read (never the Redis mirror — an
/// admin's revoked cookie must stop working immediately), the allow list, then double-submit CSRF
/// on writes. Reads and writes both go through the same Application services the slash commands
/// use, so the panel cannot validate config differently from <c>/config set</c>.
/// </remarks>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapSonarrAdmin(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup("/api/admin");

        group.MapGet("/config/{guildId}", GetConfigAsync);
        group.MapPut("/config/{guildId}", SetConfigAsync);
        group.MapGet("/flags/{guildId}", GetFlagsAsync);
        group.MapPut("/flags/{guildId}", SetFlagAsync);
        group.MapGet("/directory/{guildId}", GetDirectoryAsync);
        group.MapGet("/cases/{guildId}", GetCasesAsync);
        group.MapGet("/stats/{guildId}", GetStatsAsync);
        group.MapGet("/audit", GetAuditAsync);

        return routes;
    }

    private static async Task<IResult> GetConfigAsync(
        ulong guildId, HttpContext http, IWebAuthService auth, IGuildAuthority guilds,
        IGuildConfigService config, CancellationToken ct)
    {
        if (await Gate(http, auth, guilds, guildId, write: false, ct) is null)
        {
            return Deny(http);
        }

        IReadOnlyDictionary<string, ConfigValue> values = await config.GetAllAsync(guildId, ct);

        return Results.Ok(values.ToDictionary(
            kv => kv.Key,
            kv => new { value = kv.Value.Raw, kind = kv.Value.Definition.Kind.ToString() }));
    }

    private static async Task<IResult> SetConfigAsync(
        ulong guildId, ConfigWriteBody body, HttpContext http, IWebAuthService auth,
        IGuildAuthority guilds, IGuildConfigService config, CancellationToken ct)
    {
        if (await Gate(http, auth, guilds, guildId, write: true, ct) is not { } admin)
        {
            return Deny(http);
        }

        if (string.IsNullOrWhiteSpace(body?.Key))
        {
            return Results.BadRequest(new { error = "key is required." });
        }

        // A null value means "back to default" — the same thing /config clear does.
        ConfigWriteResult result = body.Value is null
            ? await config.ClearAsync(guildId, body.Key, admin.UserId, ct)
            : await config.SetAsync(guildId, body.Key, body.Value, admin.UserId, ct);

        if (!result.Success)
        {
            // The service's message is the validation reason and is meant to be shown.
            return Results.BadRequest(new { error = result.Message });
        }

        await auth.AuditAsync(
            admin,
            body.Value is null ? "admin.config_clear" : "admin.config_set",
            body.Key,
            guildId,
            // The value is recorded: config is guild settings, not personal data, and an audit
            // trail that omits what changed answers nothing.
            new { value = body.Value },
            ct);

        return Results.Ok(new { message = result.Message });
    }

    private static async Task<IResult> GetFlagsAsync(
        ulong guildId, HttpContext http, IWebAuthService auth, IGuildAuthority guilds,
        IFeatureGate gate, CancellationToken ct)
    {
        if (await Gate(http, auth, guilds, guildId, write: false, ct) is null)
        {
            return Deny(http);
        }

        IReadOnlyList<FeatureState> states = await gate.GetAllAsync(guildId, ct);

        return Results.Ok(states.Select(s => new
        {
            feature = s.Feature,
            enabled = s.Enabled,
            source = s.Source.ToString(),
        }));
    }

    private static async Task<IResult> SetFlagAsync(
        ulong guildId, FlagWriteBody body, HttpContext http, IWebAuthService auth,
        IGuildAuthority guilds, IFeatureGate gate, CancellationToken ct)
    {
        if (await Gate(http, auth, guilds, guildId, write: true, ct) is not { } admin)
        {
            return Deny(http);
        }

        if (string.IsNullOrWhiteSpace(body?.Feature))
        {
            return Results.BadRequest(new { error = "feature is required." });
        }

        ConfigWriteResult result = await gate.SetAsync(
            body.Feature, guildId, body.Enabled, admin.UserId, ct);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Message });
        }

        await auth.AuditAsync(
            admin, "admin.flag_set", body.Feature, guildId, new { enabled = body.Enabled }, ct);

        return Results.Ok(new { message = result.Message });
    }

    /// <param name="page">
    /// Nullable, and so are <c>days</c>/<c>skip</c>/<c>take</c> below: parameter binding runs before
    /// the handler, so a non-nullable int here answers an anonymous caller 400 where the same route
    /// answers 401 without the query string — which tells them the route exists. The gate reads the
    /// session first, and a missing page is just page 1.
    /// </param>
    private static async Task<IResult> GetCasesAsync(
        ulong guildId, HttpContext http, IWebAuthService auth, IGuildAuthority guilds,
        IModCaseRepository cases, IGuildDirectory directory, int? page, string? target,
        CancellationToken ct)
    {
        if (await Gate(http, auth, guilds, guildId, write: false, ct) is null)
        {
            return Deny(http);
        }

        long? targetId = ulong.TryParse(target, out ulong parsed) ? (long)parsed : null;
        CasePage result = await cases.GetPageAsync((long)guildId, targetId, Math.Max(page ?? 1, 1), ct);

        return Results.Ok(new
        {
            page = result.Page,
            pageCount = result.PageCount,
            totalCount = result.TotalCount,
            cases = result.Cases.Select(c => new
            {
                caseId = c.CaseId,
                targetId = Id(c.TargetId),
                // The names beside the ids, not instead of them: a moderation log is evidence, and
                // a nickname changes while a snowflake does not. Null when the member has left and
                // is in no other guild she shares — the panel shows the id then.
                targetName = directory.DisplayName(guildId, (ulong)c.TargetId),
                actorId = Id(c.ActorId),
                actorName = directory.DisplayName(guildId, (ulong)c.ActorId),
                action = c.Action.ToString(),
                c.Reason,
                c.ExpiresAt,
                c.CreatedAt,
            }),
        });
    }

    /// <summary>
    /// The guild's channels and roles, named. What turns the settings page from "paste a snowflake"
    /// into a list you pick from.
    /// </summary>
    /// <remarks>
    /// Behind the same guild gate as config, because it is the same secret: which channels a private
    /// server has is not public, so this is not a route an arbitrary logged-in user may read.
    /// <para>404 rather than an empty directory when the bot is not in the guild — an empty channel
    /// list would render as a picker with nothing in it, which reads as "this server has no
    /// channels" rather than "she is not there".</para>
    /// </remarks>
    private static async Task<IResult> GetDirectoryAsync(
        ulong guildId, HttpContext http, IWebAuthService auth, IGuildAuthority guilds,
        IGuildDirectory directory, CancellationToken ct)
    {
        if (await Gate(http, auth, guilds, guildId, write: false, ct) is null)
        {
            return Deny(http);
        }

        GuildDirectory? found = await directory.GetAsync(guildId, ct);

        return found is null ? Results.NotFound() : Results.Ok(found);
    }

    /// <summary>
    /// Command usage, the activity series and member growth (docs/09's admin Stats page).
    /// </summary>
    /// <remarks>
    /// Aggregates only. Nothing here is keyed to a person: the activity samples are counts per hour
    /// and growth is joins per day, so the page an admin sees never becomes a way to read one
    /// member's timeline (docs/06).
    /// </remarks>
    /// <param name="days">Nullable for the reason given on <see cref="GetCasesAsync"/>.</param>
    private static async Task<IResult> GetStatsAsync(
        ulong guildId, HttpContext http, IWebAuthService auth, IGuildAuthority guilds,
        IStatsRepository stats, int? days, CancellationToken ct)
    {
        if (await Gate(http, auth, guilds, guildId, write: false, ct) is null)
        {
            return Deny(http);
        }

        // days is clamped in the repository; 30 is the page's default window.
        GuildStats result = await stats.GetStatsAsync(
            (long)guildId, days is null or <= 0 ? 30 : days.Value, ct);

        return Results.Ok(new
        {
            days = result.Days,
            commands = result.Commands.Select(c => new { command = c.Command, count = c.Count }),
            activity = result.Activity.Select(a => new
            {
                at = a.HourBucket,
                messages = a.Messages,
                voiceUsers = a.VoiceUsers,
                online = a.OnlineEstimate,
            }),
            growth = result.Growth.Select(g => new { day = g.Day, joined = g.Joined }),
        });
    }

    /// <remarks>
    /// Bot tier only: the audit log spans every guild, so there is no guild id a manager could be
    /// checked against. Passing 0 is what selects <see cref="PanelScope.BotWide"/>.
    /// </remarks>
    /// <param name="skip">Nullable for the reason given on <see cref="GetCasesAsync"/>.</param>
    /// <param name="take">Nullable for the reason given on <see cref="GetCasesAsync"/>.</param>
    private static async Task<IResult> GetAuditAsync(
        HttpContext http, IWebAuthService auth, IGuildAuthority guilds, IWebAuthRepository repo,
        IGuildDirectory directory, int? skip, int? take, CancellationToken ct)
    {
        if (await Gate(http, auth, guilds, guildId: 0, write: false, ct) is null)
        {
            return Deny(http);
        }

        // take is clamped in the repository, so a hand-typed take=100000 costs nothing.
        IReadOnlyList<WebAudit> rows = await repo.GetAuditAsync(
            Math.Max(skip ?? 0, 0), take is null or <= 0 ? 50 : take.Value, ct);

        return Results.Ok(rows.Select(r => new
        {
            r.AuditId,
            userId = Id((ulong)r.UserId),
            userName = directory.DisplayName((ulong)r.GuildId, (ulong)r.UserId),
            guildId = r.GuildId == 0 ? null : Id((ulong)r.GuildId),
            // The audit log spans guilds, so the server's own name is part of reading a row.
            guildName = r.GuildId == 0 ? null : directory.GuildName((ulong)r.GuildId),
            r.Action,
            r.Target,
            r.Detail,
            r.At,
        }));
    }

    /// <summary>
    /// The gates. Returns the caller on success and <c>null</c> otherwise; the caller turns that into
    /// a status code through <see cref="Deny"/>, which re-reads the session to tell "not logged in"
    /// apart from "logged in, not allowed here".
    /// </summary>
    /// <param name="guildId">
    /// The guild this route names, which is what a guild manager is checked against. Zero means the
    /// route spans guilds and only the bot tier can reach it.
    /// </param>
    /// <remarks>
    /// The session read is always fresh — never the Redis mirror — so a revoked cookie stops working
    /// immediately. Manage Server is resolved live for this one guild rather than read from the
    /// session, because the session outlives a role change. <see cref="PanelGate"/> makes the
    /// decision; this method only gathers the facts it needs.
    /// </remarks>
    private static async Task<PanelUser?> Gate(
        HttpContext http,
        IWebAuthService auth,
        IGuildAuthority guilds,
        ulong guildId,
        bool write,
        CancellationToken ct)
    {
        PanelUser? user = await auth.AuthenticateAsync(
            http.Request.Cookies[PanelCookies.Session], requireFresh: true, ct);

        if (user is null)
        {
            return null;
        }

        PanelScope scope = guildId == 0 ? PanelScope.BotWide : PanelScope.Guild;

        // Skipped for the bot tier: it already satisfies every scope, and asking the gateway would
        // be a REST call whose answer cannot change the outcome.
        bool manages = scope == PanelScope.Guild
            && !user.IsAdmin
            && await guilds.ManagesGuildAsync(user.UserId, guildId, ct);

        return PanelGate.Allows(user, scope, manages, write, PanelCookies.CsrfMatches(http.Request))
            ? user
            : null;
    }

    /// <summary>
    /// 401 when there is no session at all, 403 once there is one — a logged-in non-admin should be
    /// told they lack access rather than sent back to the login page. The failed CSRF case also
    /// lands on 403, which is what a stale tab deserves.
    /// </summary>
    private static IResult Deny(HttpContext http)
    {
        return http.Request.Cookies.ContainsKey(PanelCookies.Session)
            ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : Results.Unauthorized();
    }

    private static string Id(ulong value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <param name="Key">A <c>ConfigKeys</c> member; anything else is rejected by the service.</param>
/// <param name="Value">Raw string value, or null to clear the key.</param>
public sealed record ConfigWriteBody(string? Key, string? Value);

public sealed record FlagWriteBody(string? Feature, bool Enabled);
