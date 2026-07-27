using Sonarr.Domain.Moderation;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// Moderation business rules: the hierarchy guard, the case-numbered audit trail, the
/// durable tempban lift. Takes and returns domain models only, so Discord and the web
/// panel get the same verdicts (docs/02-architecture.md).
/// </summary>
/// <remarks>
/// <para>
/// <b>The Discord side effect is passed in.</b> Application cannot reference Discord.Net, so the
/// controller hands over an <c>effect</c> delegate that performs the ban/kick/timeout. The
/// service owns the order: guard first, effect second, case row and log line last. A refused
/// action never runs the effect and never writes a case; a failed effect writes no case either,
/// so mod.case only ever contains things that actually happened.
/// </para>
/// <para>
/// <b>The guard runs here, not only in the module.</b> The interaction precondition is the fast
/// path; this is the one that cannot be bypassed by a web-panel call or a future entry point.
/// </para>
/// </remarks>
public interface IModerationService
{
    /// <summary>
    /// Guards, runs <paramref name="effect"/>, files the case. Use for warn/kick/ban/unban/
    /// timeout/untimeout — anything aimed at one member with no schedule attached.
    /// </summary>
    /// <param name="action">The case to file if the action goes through.</param>
    /// <param name="hierarchy">Permission and role-position facts, gathered by the controller.</param>
    /// <param name="effect">
    /// The Discord call. <c>null</c> for actions with no API side effect (a warn is only a case).
    /// </param>
    Task<ModerationOutcome> ApplyAsync(
        NewCase action,
        HierarchySnapshot hierarchy,
        Func<CancellationToken, Task>? effect = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ban with an auto-lift: files the case, then schedules a <c>tempban_lift</c> core.job row
    /// so the unban survives a restart (docs/05-caching.md — the job table is the authority).
    /// </summary>
    Task<ModerationOutcome> TempBanAsync(
        NewCase action,
        TimeSpan duration,
        HierarchySnapshot hierarchy,
        Func<CancellationToken, Task>? effect = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates <paramref name="filter"/> against <paramref name="candidates"/> and — only when
    /// <see cref="PurgeFilter.Preview"/> is <c>false</c> — calls <paramref name="deleter"/> with
    /// the matched ids and files a purge case.
    /// </summary>
    /// <param name="deleter">
    /// Performs the bulk delete and returns how many messages actually went. Never invoked for a
    /// preview: that is the dry-run contract, and it is enforced here rather than in the module.
    /// </param>
    /// <remarks>
    /// The case context records counts and filter names only — never the purged messages
    /// (docs/06-data-and-privacy.md, hard rule 1).
    /// </remarks>
    Task<PurgePreview> PurgeAsync(
        ulong guildId,
        ulong channelId,
        ulong actorId,
        PurgeFilter filter,
        IReadOnlyList<PurgeCandidate> candidates,
        Func<IReadOnlyList<ulong>, CancellationToken, Task<int>> deleter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Files the slowmode case after the channel has been changed. <paramref name="seconds"/> 0
    /// means off.
    /// </summary>
    Task<CaseRecord> RecordSlowmodeAsync(
        ulong guildId,
        ulong channelId,
        ulong actorId,
        int seconds,
        CancellationToken cancellationToken = default);

    /// <summary>One case by number, scoped to the guild that asked.</summary>
    Task<CaseRecord?> GetCaseAsync(ulong guildId, long caseId, CancellationToken cancellationToken = default);

    /// <summary>A page of /modlog. <paramref name="targetId"/> <c>null</c> = the whole guild.</summary>
    Task<CasePage> GetModLogAsync(
        ulong guildId,
        ulong? targetId,
        int page,
        CancellationToken cancellationToken = default);

    /// <summary>Infraction counts for /userinfo.</summary>
    Task<InfractionTally> GetTallyAsync(
        ulong guildId, ulong targetId, CancellationToken cancellationToken = default);

    /// <summary>The guild's anti-spam policy, or <see cref="ModerationPolicy.Default"/>.</summary>
    Task<ModerationPolicy> GetPolicyAsync(ulong guildId, CancellationToken cancellationToken = default);
}
