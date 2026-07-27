using Sonarr.Domain.Configuration;
using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Moderation;

/// <summary>
/// The moderation policy's config keys and the typed view over them. Writes and reads go
/// through <c>IGuildConfigService</c> — this file never touches storage, it only names the keys
/// and interprets their canonical string values.
/// </summary>
/// <remarks>
/// <b>These names must also exist in <c>ConfigKeys.All</c></b>, which is the closed catalog the
/// config service validates against. Until they are added there, <c>/config moderation set</c>
/// answers "isn't a config key I know" and <see cref="ReadPolicy"/> serves
/// <see cref="ModerationPolicy.Default"/> — anti-spam still runs, on defaults. The entries needed
/// are listed on each constant below.
/// </remarks>
public static class ModerationConfigKeys
{
    /// <summary>Boolean. Master switch for the AntiSpam gateway handler.</summary>
    public const string AntiSpamEnabled = "antispam_enabled";

    /// <summary>Integer 2-20. Repeats of one message in 5 min that trip identical-flood.</summary>
    public const string IdenticalThreshold = "antispam_identical_threshold";

    /// <summary>Integer 3-50. Mentions in one message that trip mass-mention.</summary>
    public const string MentionThreshold = "antispam_mention_threshold";

    /// <summary>Boolean. Treat invite links as spam.</summary>
    public const string BlockInvites = "antispam_block_invites";

    /// <summary>Choice of <see cref="ModerationConfig.ActionNames"/>. What a trip does.</summary>
    public const string Action = "antispam_action";

    /// <summary>Integer 1-40320. Timeout length when the action is <c>timeout</c>.</summary>
    public const string TimeoutMinutes = "antispam_timeout_minutes";

    /// <summary>Boolean. Delete the message that tripped the rule.</summary>
    public const string DeleteMessage = "antispam_delete_message";

    /// <summary>In <c>/config moderation</c> display order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        AntiSpamEnabled,
        IdenticalThreshold,
        MentionThreshold,
        BlockInvites,
        Action,
        TimeoutMinutes,
        DeleteMessage,
    ];

    /// <summary>One-line description per key, for <c>/config moderation show</c> and autocomplete.</summary>
    public static IReadOnlyDictionary<string, string> Descriptions { get; } = new Dictionary<string, string>
    {
        [AntiSpamEnabled] = "Anti-spam on or off",
        [IdenticalThreshold] = "Repeats of the same message that count as flooding (2-20)",
        [MentionThreshold] = "Mentions in one message that count as mass-mention (3-50)",
        [BlockInvites] = "Treat invite links as spam",
        [Action] = "What a trip does: note, warn, timeout, kick, ban",
        [TimeoutMinutes] = "Timeout length in minutes when the action is timeout (1-40320)",
        [DeleteMessage] = "Delete the message that tripped the rule",
    };

    public static bool IsKnown(string? key)
        => All.Contains(key?.Trim().ToLowerInvariant() ?? string.Empty);
}

/// <summary>Reads a <see cref="ModerationPolicy"/> out of stored config values.</summary>
public static class ModerationConfig
{
    /// <summary>
    /// Builds the policy, falling back to <see cref="ModerationPolicy.Default"/> per key. An
    /// unparseable or absent value uses the default rather than throwing: config is user input,
    /// and anti-spam has to keep working on a sane setting.
    /// </summary>
    public static ModerationPolicy ReadPolicy(IReadOnlyDictionary<string, ConfigValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        ModerationPolicy fallback = ModerationPolicy.Default;

        return new ModerationPolicy(
            AntiSpamEnabled: Boolean(values, ModerationConfigKeys.AntiSpamEnabled) ?? fallback.AntiSpamEnabled,
            IdenticalFloodThreshold: Integer(values, ModerationConfigKeys.IdenticalThreshold)
                                     ?? fallback.IdenticalFloodThreshold,
            MassMentionThreshold: Integer(values, ModerationConfigKeys.MentionThreshold)
                                  ?? fallback.MassMentionThreshold,
            InviteLinksBlocked: Boolean(values, ModerationConfigKeys.BlockInvites) ?? fallback.InviteLinksBlocked,
            Action: ParseAction(Raw(values, ModerationConfigKeys.Action)) ?? fallback.Action,
            TimeoutMinutes: Integer(values, ModerationConfigKeys.TimeoutMinutes) ?? fallback.TimeoutMinutes,
            DeleteOffendingMessage: Boolean(values, ModerationConfigKeys.DeleteMessage)
                                    ?? fallback.DeleteOffendingMessage);
    }

    /// <summary>The <c>antispam_action</c> vocabulary, shared by autocomplete and parsing.</summary>
    public static IReadOnlyList<string> ActionNames { get; } = ["note", "warn", "timeout", "kick", "ban"];

    public static SpamAction? ParseAction(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "note" => SpamAction.Note,
        "warn" => SpamAction.Warn,
        "timeout" => SpamAction.Timeout,
        "kick" => SpamAction.Kick,
        "ban" => SpamAction.Ban,
        _ => null,
    };

    private static string? Raw(IReadOnlyDictionary<string, ConfigValue> values, string key)
        => values.TryGetValue(key, out ConfigValue? value) ? value.Raw : null;

    private static bool? Boolean(IReadOnlyDictionary<string, ConfigValue> values, string key)
        => values.TryGetValue(key, out ConfigValue? value) ? value.AsBoolean : null;

    private static int? Integer(IReadOnlyDictionary<string, ConfigValue> values, string key)
        => values.TryGetValue(key, out ConfigValue? value) ? value.AsInteger : null;
}
