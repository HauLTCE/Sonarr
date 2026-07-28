using System.Globalization;
using Sonarr.Application.Chat;
using Sonarr.Bot.Observability;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Domain.Levels;
using Sonarr.Domain.Music;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Api;

/// <summary>
/// The rest of the user panel (docs/09): Overview, "Sonarr &amp; me", My errors and Music. Sits
/// beside <see cref="MeEndpoints"/> — that file owns the transparency payload and the two deletes,
/// this one owns the pages that are only a read, plus the per-fact forget those pages need.
/// </summary>
/// <remarks>
/// Every route takes the guild from the query and the user from the session, never the other way
/// round. There is deliberately no "look at user X" route here: the admin panel gets counts, not
/// other people's memories (docs/06).
/// </remarks>
public static class PanelEndpoints
{
    /// <summary>Rows per music list. The page is a screen, not an archive.</summary>
    public const int MusicRows = 20;

    /// <summary>Longest predicate the forget route accepts — the column's width.</summary>
    public const int MaxPredicate = 64;

    public static IEndpointRouteBuilder MapSonarrPanel(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup("/api/me");

        group.MapGet("/guilds", GetGuildsAsync);
        group.MapGet("/overview", GetOverviewAsync);
        group.MapGet("/sonarr", GetSonarrAndMeAsync);
        group.MapGet("/errors", GetErrorsAsync);
        group.MapGet("/music", GetMusicAsync);
        group.MapDelete("/facts/{predicate}", ForgetFactAsync);

        return routes;
    }

    /// <summary>
    /// The servers this user shares with Sonarr — the panel's server picker. Every other page here
    /// is guild-scoped, and a visitor should not have to know a snowflake to use their own panel.
    /// </summary>
    private static async Task<IResult> GetGuildsAsync(
        HttpContext http, IWebAuthService auth, IMemberRepository members, CancellationToken ct)
    {
        PanelUser? me = await Me(http, auth, ct);

        return me is null
            ? Results.Unauthorized()
            : Results.Ok((await members.GetGuildsAsync((long)me.UserId, ct))
                .Select(g => new { guildId = Id((ulong)g.GuildId), name = g.Name }));
    }

    /// <summary>Level, rank, streak, activity and the user's own pending reminders.</summary>
    private static async Task<IResult> GetOverviewAsync(
        ulong guildId,
        HttpContext http,
        IWebAuthService auth,
        ILevelService levels,
        IReminderService reminders,
        CancellationToken ct)
    {
        PanelUser? me = await Me(http, auth, ct);
        if (me is null)
        {
            return Results.Unauthorized();
        }

        if (guildId == 0)
        {
            return Results.BadRequest(new { error = "guildId is required." });
        }

        MemberStats stats = await levels.GetStatsAsync(guildId, me.UserId, ct);
        IReadOnlyList<ReminderView> pending = await reminders.ListAsync(me.UserId, ct);

        return Results.Ok(new
        {
            guildId = Id(guildId),
            level = stats.Level.Level,
            xp = stats.Level.Xp,
            xpIntoLevel = stats.Level.XpIntoLevel,
            xpForLevel = stats.Level.XpForLevel,
            xpToNextLevel = stats.Level.XpToNextLevel,
            fraction = stats.Level.Fraction,
            rank = stats.Level.Rank,
            streakDays = stats.Level.StreakDays,
            voiceMinutes = stats.Level.VoiceSeconds / 60,
            messageCount = stats.MessageCount,
            firstSeenAt = stats.FirstSeenAt,
            lastActiveAt = stats.LastActiveAt,

            // Reminders are not guild-scoped in core.job, so this is every pending one the user
            // owns — which is what "my active reminders" means to the person reading it.
            reminders = pending.Select(r => new
            {
                jobId = r.JobId,
                runAt = r.RunAt,
                text = r.Text,
                schedule = r.Schedule,
                recurrence = r.Recurrence,
            }),
        });
    }

    /// <summary>
    /// Her standing, the nickname she picked, and the facts she holds — each with the predicate the
    /// forget button posts back.
    /// </summary>
    private static async Task<IResult> GetSonarrAndMeAsync(
        ulong guildId,
        HttpContext http,
        IWebAuthService auth,
        ChatIntrospection chat,
        IPersonRepository people,
        CancellationToken ct)
    {
        PanelUser? me = await Me(http, auth, ct);
        if (me is null)
        {
            return Results.Unauthorized();
        }

        if (guildId == 0)
        {
            return Results.BadRequest(new { error = "guildId is required." });
        }

        var guild = (long)guildId;
        var user = (long)me.UserId;

        // The same call /relationship makes, with the asker and the subject being the same person,
        // so the panel gets her real line rather than the third-party deflection.
        string relationship = await chat.DescribeRelationshipAsync(guild, user, user, ct);
        MemoryReport memories = await chat.ListMemoriesAsync(guild, user, ct);
        Person? person = await people.GetAsync(guild, user, ct);

        return Results.Ok(new
        {
            relationship,
            // The tier id is hers to phrase, not the panel's — but the id itself is harmless and
            // lets the page pick an accent without inventing wording.
            tier = person?.RelationshipTier,
            nickname = person?.AssignedNickname,
            opener = memories.Opener,
            facts = memories.Facts.Select(f => new
            {
                predicate = f.Predicate,
                value = f.Value,
                confidence = f.Confidence,
                learnedAt = f.LearnedAt,
            }),
        });
    }

    /// <summary>
    /// Per-fact "ask her to forget" (docs/09). Same path <c>/memories forget</c> takes, so one
    /// implementation decides what a forget means.
    /// </summary>
    private static async Task<IResult> ForgetFactAsync(
        string predicate,
        ulong guildId,
        HttpContext http,
        IWebAuthService auth,
        ChatIntrospection chat,
        CancellationToken ct)
    {
        // Destructive, so: fresh session read plus CSRF, exactly like the two big deletes.
        PanelUser? me = await auth.AuthenticateAsync(
            http.Request.Cookies[PanelCookies.Session], requireFresh: true, ct);

        if (me is null)
        {
            return Results.Unauthorized();
        }

        if (!PanelCookies.CsrfMatches(http.Request))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (guildId == 0)
        {
            return Results.BadRequest(new { error = "guildId is required." });
        }

        // Route values are attacker-controlled and the column is 64 wide.
        predicate = predicate?.Trim() ?? string.Empty;
        if (predicate.Length is 0 or > MaxPredicate)
        {
            return Results.BadRequest(new { error = $"predicate must be 1-{MaxPredicate} characters." });
        }

        string line = await chat.ForgetAsync((long)guildId, (long)me.UserId, predicate, ct);

        await auth.AuditAsync(me, "me.forget_fact", predicate, guildId, cancellationToken: ct);

        return Results.Ok(new { message = line });
    }

    /// <summary>
    /// The user's own recent command failures. In-process and per-user, so this is "since the last
    /// restart" — which is also all an errors channel would have shown them.
    /// </summary>
    private static async Task<IResult> GetErrorsAsync(
        HttpContext http, IWebAuthService auth, UserErrorLog errors, CancellationToken ct)
    {
        PanelUser? me = await Me(http, auth, ct);

        return me is null
            ? Results.Unauthorized()
            : Results.Ok(errors.Recent(me.UserId).Select(e => new
            {
                caseId = e.CaseId,
                command = e.Command,
                message = e.Message,
                at = e.At,
            }));
    }

    /// <summary>My history, my ratings, and what the server rates highest.</summary>
    private static async Task<IResult> GetMusicAsync(
        ulong guildId,
        HttpContext http,
        IWebAuthService auth,
        IMusicStatsRepository music,
        CancellationToken ct)
    {
        PanelUser? me = await Me(http, auth, ct);
        if (me is null)
        {
            return Results.Unauthorized();
        }

        if (guildId == 0)
        {
            return Results.BadRequest(new { error = "guildId is required." });
        }

        IReadOnlyList<TrackPlayCount> history =
            await music.GetUserHistoryAsync(guildId, me.UserId, MusicRows, ct);
        IReadOnlyList<MyRating> ratings =
            await music.GetUserRatingsAsync(guildId, me.UserId, MusicRows, ct);
        IReadOnlyList<RatedTrack> serverTop = await music.GetTopRatedAsync(guildId, MusicRows, ct);

        return Results.Ok(new
        {
            history = history.Select(t => new { title = t.Title, uri = t.Uri, plays = t.Plays }),
            ratings = ratings.Select(r => new
            {
                title = r.Title,
                uri = r.Uri,
                vote = r.Vote,
                likes = r.Likes,
                dislikes = r.Dislikes,
                score = r.Score,
                at = r.At,
            }),
            serverTop = serverTop.Select(t => new
            {
                title = t.Title,
                uri = t.Uri,
                likes = t.Likes,
                dislikes = t.Dislikes,
                score = t.Score,
            }),
        });
    }

    private static Task<PanelUser?> Me(HttpContext http, IWebAuthService auth, CancellationToken ct) =>
        auth.AuthenticateAsync(http.Request.Cookies[PanelCookies.Session], requireFresh: false, ct);

    /// <summary>Snowflakes go out as strings; JSON numbers lose precision in JavaScript.</summary>
    private static string Id(ulong value) => value.ToString(CultureInfo.InvariantCulture);
}
