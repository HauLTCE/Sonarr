using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Jobs;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Utility;

/// <inheritdoc cref="ICapsuleService"/>
/// <remarks>
/// Never recurring: a capsule opens once. The parser accepts "every monday", so a recurring phrase
/// is rejected here rather than silently turned into a subscription.
/// </remarks>
internal sealed class CapsuleService(
    ICapsuleRepository capsules,
    IJobRepository jobs,
    ZoneResolver zones,
    ILogger<CapsuleService> log) : ICapsuleService
{
    /// <summary>Matches the <c>message</c> column.</summary>
    public const int MaxMessageLength = 2048;

    /// <summary>
    /// Undelivered capsules one person may hold per guild. Low on purpose: these are the only
    /// user-authored rows that can outlive the person's membership by a year.
    /// </summary>
    public const int MaxPendingPerUser = 10;

    /// <summary>
    /// A capsule has to actually be in the future to be a capsule. The parser's floor is seconds,
    /// which would make <c>/capsule write</c> a slow <c>/say</c>.
    /// </summary>
    public static readonly TimeSpan MinDelay = TimeSpan.FromHours(1);

    public async Task<ScheduleResult> WriteAsync(
        ulong guildId,
        ulong channelId,
        ulong authorId,
        string when,
        string message,
        CancellationToken cancellationToken = default)
    {
        string body = message?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            return ScheduleResult.Rejected("A capsule with nothing in it is just a delay.");
        }

        if (body.Length > MaxMessageLength)
        {
            return ScheduleResult.Rejected(
                $"That's {body.Length} characters — keep a capsule under {MaxMessageLength}.");
        }

        int pending = await capsules
            .CountPendingAsync((long)guildId, (long)authorId, cancellationToken)
            .ConfigureAwait(false);
        if (pending >= MaxPendingPerUser)
        {
            return ScheduleResult.Rejected(
                $"You already have {pending} capsules waiting. Let one open first.");
        }

        // The author's own timezone: "next january" means their january. Falls back to the
        // guild's, then UTC (ZoneResolver).
        TimeZoneInfo zone = await zones.ForUserAsync(guildId, authorId, cancellationToken).ConfigureAwait(false);
        if (!WhenParser.TryParse(when, DateTimeOffset.UtcNow, zone, out WhenResult? parsed, out string? error))
        {
            return ScheduleResult.Rejected(error);
        }

        if (parsed!.Recurrence is not null)
        {
            return ScheduleResult.Rejected("A capsule opens once. Use `/remind` for something repeating.");
        }

        if (parsed.RunAt - DateTimeOffset.UtcNow < MinDelay)
        {
            return ScheduleResult.Rejected("That's not a capsule, that's a message. Pick a date at least an hour out.");
        }

        Capsule capsule = new()
        {
            GuildId = (long)guildId,
            AuthorId = (long)authorId,
            ChannelId = (long)channelId,
            Message = body,
            DeliverAt = parsed.RunAt,
        };

        long capsuleId = await capsules.AddAsync(capsule, cancellationToken).ConfigureAwait(false);

        // Capsule first, then the job: a capsule with no job is an unopened row somebody can
        // still find, while a job pointing at nothing would fail three times and alarm a human.
        JsonObject payload = new()
        {
            [JobKinds.CapsuleIdField] = capsuleId.ToString(CultureInfo.InvariantCulture),
            [JobKinds.UserIdField] = authorId.ToString(CultureInfo.InvariantCulture),
        };

        long jobId = await jobs
            .ScheduleAsync(JobKinds.CapsuleOpen, parsed.RunAt, payload, recurrence: null, cancellationToken)
            .ConfigureAwait(false);

        // The message is never logged (docs/06 — no user content in logs).
        log.LogInformation(
            "Capsule {CapsuleId} armed as job {JobId} for {RunAt} in guild {GuildId}",
            capsuleId, jobId, parsed.RunAt, guildId);

        return ScheduleResult.Ok(
            $"Sealed. It opens <t:{parsed.RunAt.ToUnixTimeSeconds()}:F> — `#{capsuleId}`.", capsuleId);
    }

    /// <summary>
    /// Reads a claimed <c>capsule_open</c> job into the capsule to post, or null when there is
    /// nothing deliverable — a hand-edited payload, a deleted capsule, or one already opened.
    /// </summary>
    public async Task<Capsule?> ReadAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        return job.Payload[JobKinds.CapsuleIdField]?.GetValue<string>() is { } raw
               && long.TryParse(raw, CultureInfo.InvariantCulture, out long capsuleId)
            ? await capsules.GetAsync(capsuleId, cancellationToken).ConfigureAwait(false) is { DeliveredAt: null } capsule
                ? capsule
                : null
            : null;
    }

    /// <summary>
    /// Stamps the capsule opened, <b>after</b> a successful post.
    /// </summary>
    /// <remarks>
    /// Deliberately not a claim taken before posting. Stamping first would make a failed send
    /// permanent — the retry would read a delivered row and drop a message somebody wrote a year
    /// ago. Stamping after leaves one narrow window (post succeeded, stamp failed) where a retry
    /// posts twice, which is the failure worth having: a duplicate is visible and recoverable,
    /// a silently eaten capsule is neither.
    /// </remarks>
    public Task<bool> MarkOpenedAsync(long capsuleId, CancellationToken cancellationToken = default)
        => capsules.MarkDeliveredAsync(capsuleId, DateTimeOffset.UtcNow, cancellationToken);
}
