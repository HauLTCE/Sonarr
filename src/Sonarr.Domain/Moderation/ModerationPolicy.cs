namespace Sonarr.Domain.Moderation;

/// <summary>
/// The guild's anti-spam and moderation policy, read through <c>IGuildConfigService</c>
/// (<c>/config moderation …</c>). Defaults are deliberately mild: a fresh guild files notes
/// rather than banning people.
/// </summary>
/// <param name="AntiSpamEnabled">Master switch for the gateway handler.</param>
/// <param name="IdenticalFloodThreshold">Repeats of the same message inside the 5 min window that trip the rule.</param>
/// <param name="MassMentionThreshold">Mentions in one message that trip the rule.</param>
/// <param name="InviteLinksBlocked">Whether invite links are treated as spam.</param>
/// <param name="Action">What to do when a rule fires.</param>
/// <param name="TimeoutMinutes">Timeout length for <see cref="SpamAction.Timeout"/>.</param>
/// <param name="DeleteOffendingMessage">Whether the message that tripped the rule is removed.</param>
public sealed record ModerationPolicy(
    bool AntiSpamEnabled,
    int IdenticalFloodThreshold,
    int MassMentionThreshold,
    bool InviteLinksBlocked,
    SpamAction Action,
    int TimeoutMinutes,
    bool DeleteOffendingMessage)
{
    /// <summary>Applied when a guild has set nothing.</summary>
    public static readonly ModerationPolicy Default = new(
        AntiSpamEnabled: true,
        IdenticalFloodThreshold: 4,
        MassMentionThreshold: 6,
        InviteLinksBlocked: true,
        Action: SpamAction.Note,
        TimeoutMinutes: 10,
        DeleteOffendingMessage: true);

    public TimeSpan Timeout => TimeSpan.FromMinutes(TimeoutMinutes);
}

/// <summary>Duration windows Discord and common sense impose.</summary>
public static class ModerationLimits
{
    /// <summary>Longest timeout Discord accepts.</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromDays(28);

    /// <summary>Shortest timeout worth issuing.</summary>
    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Shortest tempban worth scheduling a job for.</summary>
    public static readonly TimeSpan MinTempBan = TimeSpan.FromMinutes(1);

    /// <summary>A tempban longer than this should just be a ban.</summary>
    public static readonly TimeSpan MaxTempBan = TimeSpan.FromDays(365);

    /// <summary>Discord's per-channel slowmode ceiling (6 h).</summary>
    public const int MaxSlowmodeSeconds = 21600;

    /// <summary>mod.case.reason column length.</summary>
    public const int MaxReasonLength = 1024;

    /// <summary>Used when a mod gives no reason.</summary>
    public const string NoReason = "No reason given.";
}

/// <summary>
/// The <c>/warn</c> reason templates offered by autocomplete. A fixed catalog rather than a
/// table: it is a convenience list, and a mod can always type their own reason.
/// </summary>
// ponytail: hard-coded templates. Upgrade path: a mod.reason_template table + /config moderation
// reason add|remove if a guild ever wants its own wording.
public static class WarnReasonTemplates
{
    public static IReadOnlyList<string> All { get; } =
    [
        "Spam or flooding",
        "Mass mentions",
        "Unsolicited advertising / invite links",
        "Harassment or personal attacks",
        "Hate speech or slurs",
        "NSFW content outside an NSFW channel",
        "Off-topic in a focused channel",
        "Backseat moderating",
        "Ignoring staff instructions",
        "Ban evasion (alt account)",
    ];

    /// <summary>Discord allows 25 autocomplete choices; filter then cap.</summary>
    public static IEnumerable<string> Matching(string? typed)
    {
        var needle = typed?.Trim() ?? string.Empty;
        return All
            .Where(t => needle.Length == 0 || t.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Take(25);
    }
}
