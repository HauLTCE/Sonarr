using System.Reflection;
using Sonarr.Bot.Api;

namespace Sonarr.Application.Tests;

/// <summary>
/// The cross-cutting rules of docs/06-data-and-privacy.md, as tests rather than as a paragraph
/// somebody has to re-read. <see cref="Utility.PrivacyNoticeTests"/> checks that <c>/privacy</c>
/// still <em>says</em> the right things; this file checks the code still <em>does</em> them.
/// </summary>
/// <remarks>
/// Three of docs/06's rules are structural and cannot regress by accident, so they are not here:
/// the chat engine only learns from a mention or a reply (<c>ChatPipeline</c> returns before any
/// repository call when <c>Addressed</c> is false — pinned by
/// <see cref="Chat.ChatPipelineTests.An_ambient_message_gets_no_reply_but_still_feeds_the_ring_buffer"/>);
/// DM content is never stored (every gateway handler requires a <c>SocketTextChannel</c>, so a DM
/// never reaches a service at all, and the only DM the bot ever *sends* is the login code); and
/// nothing is shared with a third party (the sole outbound HTTP call in the solution is
/// <c>LavalinkProbe</c> hitting the Lavalink on loopback).
/// </remarks>
public sealed class PrivacyAuditTests
{
    /// <summary>
    /// Every string column in the schema, and what it is allowed to hold. A new entity or a new
    /// text column fails this test until it is added here — which is the point: docs/06 promises
    /// "counts and timestamps only" for ordinary traffic, and the way that promise breaks is
    /// somebody adding a `content` column to something that sees every message.
    /// </summary>
    /// <remarks>
    /// Read the values as the audit itself. <c>user text</c> is the only class that needs
    /// justifying, and there are exactly six: a fact she was told directly, a capsule and a quote
    /// the author explicitly saved, a mod case reason a moderator typed, an event's name and
    /// description, and a reminder's text (which lives in a job payload, not a column).
    /// </remarks>
    private static readonly Dictionary<string, string> AllowedText = new(StringComparer.Ordinal)
    {
        // chat — hers, or told to her while she was addressed.
        ["Episode.Quote"] = "her own line, not the user's (ChatPipeline stores result.Text)",
        ["Episode.SentimentTag"] = "mode id",
        ["Fact.Predicate"] = "slot name",
        ["Fact.Value"] = "user text — told to her directly, deletable per-fact",
        ["IntentEmbedding.ContentHash"] = "hash",
        ["IntentEmbedding.IntentId"] = "persona id",
        ["IntentEmbedding.Example"] = "persona YAML, not user text",
        ["Person.DialogueState"] = "engine state id",
        ["Person.RelationshipTier"] = "tier id",
        ["Person.AssignedNickname"] = "hers to pick",
        ["RelationshipEvent.Cause"] = "intent id",
        ["Stance.Topic"] = "persona topic id",
        ["Stance.StanceText"] = "persona YAML",
        ["Stance.PoolRef"] = "persona pool id",
        ["StanceAgreement.Topic"] = "persona topic id",

        // core
        ["FeatureFlag.Feature"] = "feature name",
        ["Guild.Name"] = "guild name, from the gateway",
        ["GuildConfig.Key"] = "config key",
        ["Job.Kind"] = "job kind",
        ["Job.Recurrence"] = "cron-ish phrase",
        ["Job.Status"] = "status",
        ["Job.Error"] = "handler failure message",
        ["Member.Username"] = "Discord handle",
        ["Member.DisplayName"] = "Discord display name",
        ["Member.Timezone"] = "opt-in, /timezone only",
        ["Member.Locale"] = "panel language",

        // levels / mod / stats
        ["Season.Status"] = "status",
        ["ModCase.Action"] = "action",
        ["ModCase.Reason"] = "user text — a moderator's, and not self-service deletable",
        ["CommandUsage.Command"] = "command name",

        // music — titles and uris come from the resolver, not from a member typing.
        ["PlayHistory.Title"] = "track title",
        ["PlayHistory.Uri"] = "track uri",
        ["Playlist.Name"] = "user text — a name the owner chose",
        ["PlaylistTrack.Title"] = "track title",
        ["PlaylistTrack.Uri"] = "track uri",
        ["TrackRating.Uri"] = "track uri",

        // social — all four are explicit-consent storage (docs/06).
        ["Capsule.Message"] = "user text — the author wrote it to be stored",
        ["EventRsvp.Response"] = "going/maybe/no",
        ["QuoteBoard.Content"] = "user text — saved by a command, never automatically",
        ["SocialEvent.Name"] = "user text — the creator's",
        ["SocialEvent.Description"] = "user text — the creator's",
        ["SocialEvent.Status"] = "status",
        ["Ticket.Status"] = "status",
        ["Ticket.TranscriptRef"] = "a jump url; the transcript itself lives in the log channel",

        // web
        ["LoginToken.TokenHash"] = "SHA-256; the raw token only ever exists in the DM",
        ["LoginToken.Purpose"] = "purpose",
        ["WebAudit.SessionId"] = "session hash",
        ["WebAudit.Action"] = "action",
        ["WebAudit.Target"] = "an id or a config key",
        ["WebSession.SessionId"] = "session hash",
        ["WebSession.UserAgent"] = "browser UA, shown back on the sessions list",
    };

    [Fact]
    public void No_text_column_exists_that_this_audit_has_not_accounted_for()
    {
        var undeclared = Columns().Where(c => !AllowedText.ContainsKey(c)).ToList();

        Assert.True(
            undeclared.Count == 0,
            "New string column(s) in the schema. docs/06 limits what may be stored — add each to "
                + $"PrivacyAuditTests.AllowedText with what it holds: {string.Join(", ", undeclared)}");
    }

    [Fact]
    public void The_audit_lists_no_column_that_is_gone()
    {
        // The other direction, for the same reason RedisKeysTests checks both: an entry for a
        // dropped column is a justification nobody can check any more.
        var stale = AllowedText.Keys.Except(Columns(), StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0, $"no such column any more: {string.Join(", ", stale)}");
    }

    /// <summary>
    /// docs/06: user panel queries are scoped to the session's <c>user_id</c> in every query. The
    /// enforceable half of that is that no <c>/api/me</c> handler <em>accepts</em> a user id — the
    /// subject comes from <c>AuthenticateAsync</c> or from nowhere.
    /// </summary>
    /// <remarks>
    /// A parameter is the only way a user id could get in: these are minimal-API delegates, so
    /// anything bindable is either a route value, a query string or a JSON body — all three
    /// attacker-controlled. `guildId` is deliberately fine, and is checked against the caller's own
    /// membership list by the panel before it is ever sent (web/lib/guilds.ts).
    /// </remarks>
    [Theory]
    [InlineData(typeof(MeEndpoints))]
    [InlineData(typeof(PanelEndpoints))]
    public void No_user_panel_route_takes_a_user_id(Type endpoints)
    {
        var offenders = endpoints
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .SelectMany(m => m.GetParameters().Select(p => (Method: m.Name, Param: p.Name ?? "")))
            .Where(x => x.Param.Contains("user", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Method}({x.Param})")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "An /api/me handler takes a user id, which would make it a 'look at user X' route: "
                + string.Join(", ", offenders));
    }

    /// <summary>
    /// docs/06: the web app has no database credentials and no Discord token. The panel reads one
    /// env var, and this is the test that keeps that true — a second one would mean it had grown a
    /// connection of its own.
    /// </summary>
    [Fact]
    public void The_web_app_reads_no_secret_from_its_environment()
    {
        DirectoryInfo root = RepoRoot();
        var web = Path.Combine(root.FullName, "web");
        Assert.True(Directory.Exists(web), "web/ is missing");

        var reads = Directory
            .EnumerateFiles(web, "*.ts*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}.next{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadAllLines(f)
                .Where(l => l.Contains("process.env.", StringComparison.Ordinal))
                .Select(l => l[(l.IndexOf("process.env.", StringComparison.Ordinal) + "process.env.".Length)..])
                .Select(l => new string([.. l.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_')])))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // The panel talks to Kestrel and to nothing else. No DATABASE_URL, no REDIS_URL, no token.
        Assert.Equal(["SONARR_API_URL"], reads);
    }

    private static DirectoryInfo RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sonarr.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!;
    }

    private static IEnumerable<string> Columns() =>
        Assembly.Load("Sonarr.Domain").GetTypes()
            .Where(t => t.IsClass && t.Namespace?.StartsWith("Sonarr.Domain.Entities", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string))
                .Select(p => $"{t.Name}.{p.Name}"));
}
