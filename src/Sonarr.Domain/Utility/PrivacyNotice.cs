namespace Sonarr.Domain.Utility;

/// <summary>
/// The <c>/privacy</c> text. Lives in Domain because docs/06-data-and-privacy.md requires the
/// command, the panel's "your data" page and the doc to never disagree — one string, two
/// consumers, and a test that checks it against the doc.
/// </summary>
public static class PrivacyNotice
{
    /// <summary>Every row of the "what we collect" table in docs/06, in that order.</summary>
    public static IReadOnlyList<string> Collected { get; } =
    [
        "Your Discord ids, username and display name — so I can address you and the panel can show you.",
        "Message **counts** and activity times — for levels, streaks and server stats. The content isn't stored.",
        "XP, level, voice time and streaks — the levels feature.",
        "Things you told me directly — name, job, pets, birthday. Only what you said **to** me.",
        "Short quotes from conversations **with me**, so I can call them back later.",
        "What I think of you — moods, trust, grudges, tier, nickname.",
        "Music you queued: history, ratings, preferences.",
        "Moderation cases about you — the audit trail.",
        "Reminders, capsules, saved quotes and tickets you created.",
        "Your timezone and birthday — **only if you set them** with a command.",
        "Panel sessions and login-token hashes, for web login.",
    ];

    /// <summary>The hard rules from docs/06, in user-facing wording.</summary>
    public static IReadOnlyList<string> Rules { get; } =
    [
        "**No message-content logging.** General chat is counted, not kept. Content is only stored when you talk to me, when you save it yourself (`/quote`, capsule, ticket transcript), or when a mod action records its own reason.",
        "I only learn from messages addressed to me — a mention or a reply. I never mine the channel.",
        "DM content is never stored, except the login token I sent you (hashed).",
        "Embeddings are one-way: a vector can't be turned back into text, and the quote behind it obeys the same deletion rules as everything else.",
        "No third parties. Nothing about you is sent to an external API.",
    ];

    /// <summary>Retention, matching the docs/06 table.</summary>
    public static IReadOnlyList<string> Retention { get; } =
    [
        "Conversation quotes: your newest 200, plus anything a fact points at.",
        "Live session state in the cache: minutes to an hour.",
        "Activity stats: aggregated per hour, kept 400 days.",
        "Moderation cases: kept indefinitely — it's an audit trail. Guild owners can purge them from the admin panel.",
        "Login tokens: 10 minutes, single use. Dead sessions are purged daily.",
        "Database backups age out on their own schedule, so deletions reach them late.",
    ];

    /// <summary>Self-service rights from docs/06, including what is deliberately not deletable.</summary>
    public static IReadOnlyList<string> Rights { get; } =
    [
        "**See** everything live on the panel's \"your data\" page. `/memories` is the in-character version.",
        "**Delete** one thing with `/memories forget <fact>`, my whole memory of you from the panel (I genuinely forget you — back to stranger), or everything with panel → \"delete everything\".",
        "**Not deletable by you:** moderation cases about you (audit integrity — a guild-owner decision) and anonymous aggregate stats.",
        "**Export** all of it as JSON from the panel.",
    ];

    /// <summary>
    /// Renders the ephemeral <c>/privacy</c> body. <paramref name="panelBaseUrl"/> becomes the
    /// panel link; a blank one just omits it rather than printing a broken URL.
    /// </summary>
    public static string Render(string? panelBaseUrl)
    {
        System.Text.StringBuilder text = new();
        text.AppendLine("**What I keep about you**");
        foreach (var line in Collected)
        {
            text.AppendLine($"• {line}");
        }

        text.AppendLine().AppendLine("**Rules I don't break**");
        foreach (var line in Rules)
        {
            text.AppendLine($"• {line}");
        }

        text.AppendLine().AppendLine("**How long it lives**");
        foreach (var line in Retention)
        {
            text.AppendLine($"• {line}");
        }

        text.AppendLine().AppendLine("**What you can do about it**");
        foreach (var line in Rights)
        {
            text.AppendLine($"• {line}");
        }

        var url = panelBaseUrl?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(url))
        {
            text.AppendLine().Append($"Everything above, live and per-row: {url}/me/data");
        }

        return text.ToString().TrimEnd();
    }
}
