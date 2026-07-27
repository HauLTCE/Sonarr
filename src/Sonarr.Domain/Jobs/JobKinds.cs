namespace Sonarr.Domain.Jobs;

/// <summary>
/// The core.job <c>kind</c> catalog owned by the scheduler slice, plus the payload field names
/// each kind agrees on. One place, so a rename cannot half-land between the service that writes
/// the row and the handler that runs it (same pattern as
/// <see cref="Moderation.TempBanJob"/>, which owns <c>tempban_lift</c>).
/// </summary>
/// <remarks>
/// Recurring work is <b>not</b> a separate kind: the row carries <c>recurrence</c> and the
/// handler is the same either way (docs/08-background-services.md lists reminders and recurring
/// reminders as one dispatch path). That keeps <c>/reminders list</c> to a single query and stops
/// a recurring reminder from needing a second handler registration.
/// </remarks>
public static class JobKinds
{
    /// <summary><c>/remind</c> — one-shot or recurring when <c>recurrence</c> is set.</summary>
    public const string Reminder = "reminder";

    /// <summary><c>/announce</c> with a schedule — one-shot or recurring.</summary>
    public const string Announce = "announce";

    /// <summary>
    /// Payload field every user-scoped kind must write: <c>IJobRepository</c> matches ownership
    /// through <c>payload-&gt;&gt;'user_id'</c>, so <c>/reminders cancel</c> depends on it.
    /// </summary>
    public const string UserIdField = "user_id";

    public const string GuildIdField = "guild_id";

    public const string ChannelIdField = "channel_id";

    /// <summary>Reminder text / announcement body.</summary>
    public const string TextField = "text";
}
