using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Moderation;

/// <inheritdoc cref="IModerationService"/>
public sealed class ModerationService(
    IModCaseRepository cases,
    IJobRepository jobs,
    IGuildConfigService config,
    ILogger<ModerationService> log) : IModerationService
{
    public async Task<ModerationOutcome> ApplyAsync(
        NewCase action,
        HierarchySnapshot hierarchy,
        Func<CancellationToken, Task>? effect = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(hierarchy);

        ModerationDenial denial = ModerationGuard.Check(
            action.Action, action.ActorId, action.TargetId, hierarchy);

        if (denial is not ModerationDenial.None)
        {
            log.LogInformation(
                "Mod action {Action} on {TargetId} in {GuildId} refused for {ActorId}: {Denial}",
                action.Action, action.TargetId, action.GuildId, action.ActorId, denial);
            return ModerationOutcome.Denied(denial);
        }

        // Effect before the case row: mod.case is the record of what happened, so a failed
        // Discord call must not leave a case claiming otherwise. The exception propagates to
        // the controller's error pipeline.
        if (effect is not null)
        {
            await effect(cancellationToken);
        }

        CaseRecord record = await FileAsync(action, cancellationToken);
        return ModerationOutcome.Done(record);
    }

    public async Task<ModerationOutcome> TempBanAsync(
        NewCase action,
        TimeSpan duration,
        HierarchySnapshot hierarchy,
        Func<CancellationToken, Task>? effect = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (duration < ModerationLimits.MinTempBan || duration > ModerationLimits.MaxTempBan)
        {
            return ModerationOutcome.Denied(ModerationDenial.InvalidDuration);
        }

        DateTimeOffset expiresAt = DateTimeOffset.UtcNow + duration;

        ModerationOutcome outcome = await ApplyAsync(
            action with { Action = CaseAction.TempBan, ExpiresAt = expiresAt },
            hierarchy,
            effect,
            cancellationToken);

        if (outcome.Case is not { } record)
        {
            return outcome;
        }

        // The durable half: a core.job row, not a Timer. A restart between the ban and the lift
        // changes nothing — the poller finds this row when it comes due (docs/05-caching.md).
        long jobId = await jobs.ScheduleAsync(
            TempBanJob.Kind,
            expiresAt,
            new JsonObject
            {
                [TempBanJob.GuildIdField] = record.GuildId.ToString(CultureInfo.InvariantCulture),
                [TempBanJob.UserIdField] = record.TargetId.ToString(CultureInfo.InvariantCulture),
                [TempBanJob.CaseIdField] = record.CaseId.ToString(CultureInfo.InvariantCulture),
            },
            ct: cancellationToken);

        log.LogInformation(
            "Tempban case {CaseId} lifts at {ExpiresAt:o} via job {JobId}",
            record.CaseId, expiresAt, jobId);

        return outcome;
    }

    public async Task<PurgePreview> PurgeAsync(
        ulong guildId,
        ulong channelId,
        ulong actorId,
        PurgeFilter filter,
        IReadOnlyList<PurgeCandidate> candidates,
        Func<IReadOnlyList<ulong>, CancellationToken, Task<int>> deleter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(deleter);

        PurgePreview plan = PurgePlanner.Plan(filter, candidates);

        if (filter.Preview)
        {
            // THE dry run. No deleter call, no case row, no state change of any kind —
            // /purge preview:true is the one command whose whole job is to touch nothing.
            log.LogInformation(
                "Purge preview in {GuildId}/{ChannelId} by {ActorId}: would delete {Matched} " +
                "(skipped {Pinned} pinned, {TooOld} too old)",
                guildId, channelId, actorId, plan.Matched, plan.Pinned, plan.TooOld);

            return plan with { WasDryRun = true };
        }

        if (plan.MessageIds.Count == 0)
        {
            log.LogInformation(
                "Purge in {GuildId}/{ChannelId} by {ActorId} matched nothing", guildId, channelId, actorId);
            return plan;
        }

        int deleted = await deleter(plan.MessageIds, cancellationToken);

        // Case context is counts and filter names. Not one line of message content
        // (docs/06-data-and-privacy.md, hard rule 1).
        CaseRecord record = await FileAsync(
            new NewCase(
                guildId,
                TargetId: filter.FromUserId ?? 0,
                actorId,
                CaseAction.Purge,
                Reason: DescribePurge(filter),
                Context: WithChannel(filter.ToContext(plan.Matched, deleted), channelId)),
            cancellationToken);

        log.LogInformation(
            "Purge case {CaseId}: deleted {Deleted} of {Matched} in {GuildId}/{ChannelId} by {ActorId}",
            record.CaseId, deleted, plan.Matched, guildId, channelId, actorId);

        return plan with { Deleted = deleted };
    }

    public Task<CaseRecord> RecordSlowmodeAsync(
        ulong guildId,
        ulong channelId,
        ulong actorId,
        int seconds,
        CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(seconds, 0, ModerationLimits.MaxSlowmodeSeconds);

        return FileAsync(
            new NewCase(
                guildId,
                TargetId: 0,
                actorId,
                CaseAction.Slowmode,
                Reason: clamped == 0
                    ? "Slowmode off"
                    : $"Slowmode {clamped}s",
                Context: WithChannel(
                    new Dictionary<string, string>
                    {
                        ["seconds"] = clamped.ToString(CultureInfo.InvariantCulture),
                    },
                    channelId)),
            cancellationToken);
    }

    public Task<CaseRecord?> GetCaseAsync(
        ulong guildId, long caseId, CancellationToken cancellationToken = default)
        => cases.GetAsync((long)guildId, caseId, cancellationToken);

    public Task<CasePage> GetModLogAsync(
        ulong guildId, ulong? targetId, int page, CancellationToken cancellationToken = default)
        => cases.GetPageAsync(
            (long)guildId,
            targetId is { } t ? (long)t : null,
            Math.Max(page, 1),
            cancellationToken);

    public Task<InfractionTally> GetTallyAsync(
        ulong guildId, ulong targetId, CancellationToken cancellationToken = default)
        => cases.GetTallyAsync((long)guildId, (long)targetId, cancellationToken);

    public async Task<ModerationPolicy> GetPolicyAsync(
        ulong guildId, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, Domain.Configuration.ConfigValue> all =
            await config.GetAllAsync(guildId, cancellationToken);

        return ModerationConfig.ReadPolicy(all);
    }

    /// <summary>
    /// The one place a case row is created — so every action gets a number AND a structured
    /// console line (docs/checklist.md: "DB row + Serilog structured console line for every action").
    /// </summary>
    private async Task<CaseRecord> FileAsync(NewCase action, CancellationToken ct)
    {
        NewCase normalized = action with { Reason = NormalizeReason(action.Reason) };
        CaseRecord record = await cases.AddAsync(normalized, ct);

        log.LogInformation(
            "Case {CaseId} {Action}: target {TargetId} by {ActorId} in guild {GuildId} " +
            "expires {ExpiresAt} reason {Reason}",
            record.CaseId,
            record.Action,
            record.TargetId,
            record.ActorId,
            record.GuildId,
            record.ExpiresAt,
            record.Reason);

        return record;
    }

    private static string NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return ModerationLimits.NoReason;
        }

        return trimmed.Length <= ModerationLimits.MaxReasonLength
            ? trimmed
            : trimmed[..ModerationLimits.MaxReasonLength];
    }

    private static string DescribePurge(PurgeFilter filter)
    {
        List<string> parts = [$"up to {filter.Count}"];
        if (filter.FromUserId is { } author)
        {
            parts.Add($"from {author}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Contains))
        {
            parts.Add("matching text");
        }

        if (filter.BotsOnly)
        {
            parts.Add("bots only");
        }

        return $"Purge: {string.Join(", ", parts)}";
    }

    private static Dictionary<string, string> WithChannel(Dictionary<string, string> context, ulong channelId)
    {
        context["channel"] = channelId.ToString(CultureInfo.InvariantCulture);
        return context;
    }
}
