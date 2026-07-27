namespace Sonarr.Domain.Moderation;

/// <summary>
/// The <c>tempban_lift</c> core.job contract: kind string plus payload field names, shared by
/// the service that schedules it and the handler that runs it, so a rename cannot half-land.
/// </summary>
public static class TempBanJob
{
    /// <summary>core.job.kind value.</summary>
    public const string Kind = "tempban_lift";

    /// <summary>Payload: guild to unban in.</summary>
    public const string GuildIdField = "guild_id";

    /// <summary>
    /// Payload: who to unban. Named <c>user_id</c> because <c>IJobRepository</c> queries
    /// ownership through <c>payload-&gt;&gt;'user_id'</c>.
    /// </summary>
    public const string UserIdField = "user_id";

    /// <summary>Payload: the tempban's case number, for the lift's log line.</summary>
    public const string CaseIdField = "case_id";
}
