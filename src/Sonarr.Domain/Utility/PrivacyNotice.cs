namespace Sonarr.Domain.Utility;

/// <summary>
/// The <c>/privacy</c> text. Lives in Domain because docs/06-data-and-privacy.md requires the
/// command and the doc to never disagree — one string, and a test that checks it against the doc.
/// </summary>
public static class PrivacyNotice
{
    /// <summary>Every row of the "what we collect" table in docs/06, in that order.</summary>
    public static IReadOnlyList<string> Collected { get; } =
    [
        "Your Discord ids, username and display name — so I can address you properly.",
        "Message **counts** and activity times — for levels, streaks and server stats. The content isn't stored.",
        "XP, level, voice time and streaks — the levels feature.",
        "Things you told me directly — name, job, pets, birthday. Only what you said **to** me.",
        "Short quotes from conversations **with me**, so I can call them back later.",
        "What I think of you — moods, trust, grudges, tier, nickname.",
        "Music you queued: history, ratings, preferences.",
        "Moderation cases about you — the audit trail.",
        "Reminders, capsules, saved quotes and tickets you created.",
        "Your timezone and birthday — **only if you set them** with a command.",
        "Short-lived session state in the cache — cooldowns, the queue you're listening to.",
    ];

    /// <summary>The hard rules from docs/06, in user-facing wording.</summary>
    public static IReadOnlyList<string> Rules { get; } =
    [
        "**No message-content logging.** General chat is counted, not kept. Content is only stored when you talk to me, when you save it yourself (`/quote`, capsule, ticket transcript), or when a mod action records its own reason.",
        "I only learn from messages addressed to me — a mention or a reply. I never mine the channel.",
        "DM content is never stored.",
        "Embeddings are one-way: a vector can't be turned back into text, and the quote behind it obeys the same deletion rules as everything else.",
        "No third parties. Nothing about you is sent to an external API.",
        "**No web surface.** There is no site and no API — Discord is the only way in, so there is no account to break into and no session to steal.",
    ];

    /// <summary>Retention, matching the docs/06 table.</summary>
    public static IReadOnlyList<string> Retention { get; } =
    [
        "Conversation quotes: your newest 200, plus anything a fact points at.",
        "Live session state in the cache: minutes to an hour, then it expires on its own.",
        "Activity stats: aggregated per hour, kept 400 days.",
        "Moderation cases: kept indefinitely — it's an audit trail. Only a guild owner can purge them.",
        "Database backups age out on their own schedule, so deletions reach them late.",
    ];

    /// <summary>Self-service rights from docs/06, including what is deliberately not deletable.</summary>
    public static IReadOnlyList<string> Rights { get; } =
    [
        "**See** everything I hold on you with `/memories list`.",
        "**Delete** one thing with `/memories forget <fact>`, or all of it with `/memories forget everything` — I genuinely forget you, back to stranger.",
        "**Not deletable by you:** moderation cases about you (audit integrity — a guild-owner decision) and anonymous aggregate stats.",
    ];

    /// <summary>Renders the ephemeral <c>/privacy</c> body.</summary>
    public static string Render()
    {
        System.Text.StringBuilder text = new();
        Section(text, "**What I keep about you**", Collected);
        Section(text, "**Rules I don't break**", Rules);
        Section(text, "**How long it lives**", Retention);
        Section(text, "**What you can do about it**", Rights);

        return text.ToString().TrimEnd();
    }

    private static void Section(
        System.Text.StringBuilder text, string heading, IReadOnlyList<string> lines)
    {
        if (text.Length > 0)
        {
            text.AppendLine();
        }

        text.AppendLine(heading);
        foreach (var line in lines)
        {
            text.AppendLine($"• {line}");
        }
    }
}
