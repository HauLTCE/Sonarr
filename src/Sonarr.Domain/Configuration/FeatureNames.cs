namespace Sonarr.Domain.Configuration;

/// <summary>
/// The kill-switch catalog (docs/checklist.md — "Kill switches &amp; health"). A disabled
/// module answers "this feature is currently off" rather than vanishing, so these names are
/// user-visible in <c>/feature</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of name live here. A <b>module</b> is a slice of the bot and defaults on, so a guild
/// that has never touched <c>/feature</c> gets everything. A <b>restriction</b>
/// (<see cref="Restrictions"/>) is the opposite shape: it defaults <em>off</em>, and turning it on
/// takes something away. Both are rows in <c>core.feature_flag</c> because that is the only config
/// table without a foreign key to <c>core.guild</c>, and so the only one that can hold the
/// <c>guild_id = 0</c> global row these two are meant to be set on.
/// </para>
/// <para>
/// <see cref="Sleep"/> and <see cref="Midday"/> are the Python bot's <c>SLEEP_MODE_ENABLED</c> and
/// <c>MIDDAY_BREAK_ENABLED</c>, which were environment variables and so could not be changed
/// without a restart. They gated <c>economy_allowed()</c>; there is no economy module here, so what
/// they gate now is the chat reply path — see <see cref="QuietHours"/>. The third legacy toggle,
/// <c>HA_ENABLED</c>, chose Postgres over SQLite and is deliberately not restored: Postgres is the
/// only store this rewrite has.
/// </para>
/// </remarks>
public static class FeatureNames
{
    public const string Chat = "chat";
    public const string Music = "music";
    public const string Levels = "levels";
    public const string Moderation = "moderation";
    public const string Social = "social";
    public const string Welcome = "welcome";
    public const string Tickets = "tickets";
    public const string Events = "events";
    public const string Reminders = "reminders";

    /// <summary>She stops answering between 22:00 and 06:00 in the guild's zone.</summary>
    public const string Sleep = "sleep_mode";

    /// <summary>She stops answering for the 12:00 hour in the guild's zone.</summary>
    public const string Midday = "midday_break";

    public static IReadOnlyList<string> All { get; } =
        [Chat, Music, Levels, Moderation, Social, Welcome, Tickets, Events, Reminders, Sleep, Midday];

    /// <summary>
    /// The names that take something away rather than provide it, and so default off. Everything
    /// else in <see cref="All"/> is a module and defaults on.
    /// </summary>
    public static IReadOnlySet<string> Restrictions { get; } =
        new HashSet<string>([Sleep, Midday], StringComparer.Ordinal);

    /// <summary>
    /// What a name means with no row anywhere. Restrictions are off by default, matching the
    /// <c>=False</c> the legacy template shipped — a fresh install must not go quiet at 22:00
    /// because nobody asked it to.
    /// </summary>
    public static bool DefaultState(string? feature)
        => !Restrictions.Contains(Normalize(feature));

    public static bool IsKnown(string? feature) => All.Contains(Normalize(feature));

    /// <summary>
    /// Short names the CLI accepts in place of the stored name, so <c>sonarr set sleep false</c>
    /// works without spelling out <c>sleep_mode</c>. A name that is already a catalog name resolves
    /// to itself, so callers can normalize unconditionally.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sleep"] = Sleep,
            ["midday"] = Midday,
            ["lunch"] = Midday,
        };

    /// <inheritdoc cref="Aliases"/>
    public static string Resolve(string? feature)
    {
        var normalized = Normalize(feature);
        return Aliases.TryGetValue(normalized, out var name) ? name : normalized;
    }

    private static string Normalize(string? feature)
        => feature?.Trim().ToLowerInvariant() ?? string.Empty;
}
