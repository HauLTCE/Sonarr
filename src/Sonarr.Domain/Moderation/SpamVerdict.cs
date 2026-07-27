namespace Sonarr.Domain.Moderation;

/// <summary>Which anti-spam rule fired (docs/08-background-services.md: AntiSpam).</summary>
public enum SpamTrigger
{
    /// <summary>Nothing fired.</summary>
    None,

    /// <summary>The same message repeated inside the <c>rl:spam</c> window.</summary>
    IdenticalFlood,

    /// <summary>More user/role mentions in one message than the policy allows.</summary>
    MassMention,

    /// <summary>A discord.gg / discord.com invite link.</summary>
    InviteLink,
}

/// <summary>What Sonarr does when a rule fires. Configured per guild.</summary>
public enum SpamAction
{
    /// <summary>Case row only — the audit trail without a punishment.</summary>
    Note,

    Warn,

    Timeout,

    Kick,

    Ban,
}

/// <summary>
/// One message reduced to what AntiSpam needs. No Discord types, and the content is used to
/// hash and to count mentions — never stored or logged (docs/06, hard rule 1).
/// </summary>
/// <param name="GuildId">Guild the message was sent in.</param>
/// <param name="ChannelId">Channel, for the log line.</param>
/// <param name="AuthorId">Author snowflake.</param>
/// <param name="Content">Raw text. In-memory only.</param>
/// <param name="MentionedUserCount">Distinct user mentions resolved by the gateway.</param>
/// <param name="MentionedRoleCount">Distinct role mentions.</param>
/// <param name="MentionsEveryone">@everyone / @here present.</param>
/// <param name="AuthorIsBot">Bots are exempt: webhooks and other bots are the admin's business.</param>
/// <param name="AuthorIsModerator">Moderators are exempt from their own anti-spam.</param>
public sealed record SpamCandidate(
    ulong GuildId,
    ulong ChannelId,
    ulong AuthorId,
    string Content,
    int MentionedUserCount = 0,
    int MentionedRoleCount = 0,
    bool MentionsEveryone = false,
    bool AuthorIsBot = false,
    bool AuthorIsModerator = false);

/// <summary>
/// The AntiSpam decision. <see cref="Trigger"/> is <see cref="SpamTrigger.None"/> for the
/// overwhelming majority of messages, which is the only path that must stay cheap.
/// </summary>
/// <param name="Trigger">Which rule fired.</param>
/// <param name="Action">What to do about it, from guild policy.</param>
/// <param name="Reason">The case reason, already user-facing.</param>
/// <param name="DeleteMessage">Whether the offending message should be removed.</param>
/// <param name="TimeoutFor">Timeout length when <see cref="Action"/> is <see cref="SpamAction.Timeout"/>.</param>
/// <param name="Detail">Structured log context: counts, never content.</param>
public sealed record SpamVerdict(
    SpamTrigger Trigger,
    SpamAction Action,
    string Reason,
    bool DeleteMessage,
    TimeSpan? TimeoutFor = null,
    IReadOnlyDictionary<string, string>? Detail = null)
{
    /// <summary>The hot path: nothing fired, nothing to do.</summary>
    public static readonly SpamVerdict Clean =
        new(SpamTrigger.None, SpamAction.Note, string.Empty, false);

    public bool IsSpam => Trigger is not SpamTrigger.None;
}
