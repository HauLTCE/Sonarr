using Sonarr.Domain.Entities.Mod;

namespace Sonarr.Domain.Moderation;

/// <summary>What a moderation case is about. Maps 1:1 onto <see cref="ModAction"/> strings.</summary>
public enum CaseAction
{
    Warn,
    Kick,
    Ban,
    TempBan,
    Unban,
    Timeout,
    Untimeout,
    Purge,
    Slowmode,
    Note,
}

/// <summary>The enum ↔ storage-string mapping, in one place so the two cannot drift.</summary>
public static class CaseActions
{
    public static string ToStorage(this CaseAction action) => action switch
    {
        CaseAction.Warn => ModAction.Warn,
        CaseAction.Kick => ModAction.Kick,
        CaseAction.Ban => ModAction.Ban,
        CaseAction.TempBan => ModAction.TempBan,
        CaseAction.Unban => ModAction.Unban,
        CaseAction.Timeout => ModAction.Timeout,
        CaseAction.Untimeout => ModAction.Untimeout,
        CaseAction.Purge => ModAction.Purge,
        CaseAction.Slowmode => ModAction.Slowmode,
        _ => ModAction.Note,
    };

    public static CaseAction FromStorage(string? stored) => stored switch
    {
        ModAction.Warn => CaseAction.Warn,
        ModAction.Kick => CaseAction.Kick,
        ModAction.Ban => CaseAction.Ban,
        ModAction.TempBan => CaseAction.TempBan,
        ModAction.Unban => CaseAction.Unban,
        ModAction.Timeout => CaseAction.Timeout,
        ModAction.Untimeout => CaseAction.Untimeout,
        ModAction.Purge => CaseAction.Purge,
        ModAction.Slowmode => CaseAction.Slowmode,
        _ => CaseAction.Note,
    };

    /// <summary>Past tense for the user-facing confirmation line ("banned", "timed out").</summary>
    public static string PastTense(this CaseAction action) => action switch
    {
        CaseAction.Warn => "warned",
        CaseAction.Kick => "kicked",
        CaseAction.Ban => "banned",
        CaseAction.TempBan => "temporarily banned",
        CaseAction.Unban => "unbanned",
        CaseAction.Timeout => "timed out",
        CaseAction.Untimeout => "un-timed out",
        CaseAction.Purge => "purged",
        CaseAction.Slowmode => "slowed",
        _ => "noted",
    };

    /// <summary>True when the action needs the target to still be in the guild.</summary>
    public static bool NeedsPresentMember(this CaseAction action) => action
        is CaseAction.Warn or CaseAction.Kick or CaseAction.Timeout or CaseAction.Untimeout;
}

/// <summary>
/// One mod.case row as the rest of the system sees it — no EF entity, no jsonb node.
/// <paramref name="Context"/> holds counts and filters only: never message content
/// (docs/06-data-and-privacy.md, hard rule 1).
/// </summary>
public sealed record CaseRecord(
    long CaseId,
    ulong GuildId,
    ulong TargetId,
    ulong ActorId,
    CaseAction Action,
    string Reason,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Context);

/// <summary>A new case before Postgres assigns its number.</summary>
public sealed record NewCase(
    ulong GuildId,
    ulong TargetId,
    ulong ActorId,
    CaseAction Action,
    string Reason,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyDictionary<string, string>? Context = null);

/// <summary>mod.infraction_summary for one member — the /userinfo history block.</summary>
public sealed record InfractionTally(int Warns, int Kicks, int Bans, DateTimeOffset? LastCaseAt)
{
    public static readonly InfractionTally Empty = new(0, 0, 0, null);

    public int Total => Warns + Kicks + Bans;
}

/// <summary>One page of /modlog.</summary>
public sealed record CasePage(IReadOnlyList<CaseRecord> Cases, int Page, int PageCount, int TotalCount)
{
    public const int PageSize = 10;
}
