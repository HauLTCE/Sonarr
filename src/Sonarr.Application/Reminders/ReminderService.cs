using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sonarr.Application.Utility;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Jobs;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Reminders;

/// <inheritdoc cref="IReminderService"/>
internal sealed class ReminderService(
    IJobRepository jobs,
    IMemberRepository members,
    ZoneResolver zones,
    ILogger<ReminderService> log) : IReminderService
{
    /// <summary>Discord embeds are bigger, but a reminder you can't read at a glance is useless.</summary>
    public const int MaxTextLength = 1000;

    /// <summary>Keeps one user from filling the poller's claim batch on their own.</summary>
    public const int MaxPendingPerUser = 25;

    public async Task<ScheduleResult> CreateAsync(
        ulong guildId,
        ulong channelId,
        ulong userId,
        string when,
        string text,
        CancellationToken cancellationToken = default)
    {
        var body = text?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            return ScheduleResult.Rejected("Remind you about what, exactly?");
        }

        if (body.Length > MaxTextLength)
        {
            return ScheduleResult.Rejected(
                $"That's {body.Length} characters. Keep it under {MaxTextLength} — it's a reminder, not an essay.");
        }

        IReadOnlyList<Job> existing = await jobs.GetPendingByKindAsync(JobKinds.Reminder, (long)userId, cancellationToken);
        if (existing.Count >= MaxPendingPerUser)
        {
            return ScheduleResult.Rejected(
                $"You already have {existing.Count} reminders pending. Cancel one with `/reminders cancel` first.");
        }

        TimeZoneInfo zone = await zones.ForUserAsync(guildId, userId, cancellationToken);
        if (!WhenParser.TryParse(when, DateTimeOffset.UtcNow, zone, out WhenResult? parsed, out var error))
        {
            return ScheduleResult.Rejected(error);
        }

        JsonObject payload = new()
        {
            [JobKinds.UserIdField] = userId.ToString(CultureInfo.InvariantCulture),
            [JobKinds.GuildIdField] = guildId.ToString(CultureInfo.InvariantCulture),
            [JobKinds.ChannelIdField] = channelId.ToString(CultureInfo.InvariantCulture),
            [JobKinds.TextField] = body,
        };

        var jobId = await jobs.ScheduleAsync(
            JobKinds.Reminder,
            parsed!.RunAt,
            payload,
            parsed.Recurrence,
            cancellationToken);

        log.LogInformation(
            "Reminder {JobId} scheduled for user {UserId} at {RunAt} ({Recurrence})",
            jobId, userId, parsed.RunAt, parsed.Recurrence ?? "once");

        var repeat = parsed.Recurrence is null ? string.Empty : $", {Recurrence.Describe(parsed.Recurrence)}";
        return ScheduleResult.Ok($"Fine. <t:{parsed.RunAt.ToUnixTimeSeconds()}:R>{repeat} — `#{jobId}`.", jobId);
    }

    public async Task<IReadOnlyList<ReminderView>> ListAsync(
        ulong userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Job> rows = await jobs.GetPendingByKindAsync(JobKinds.Reminder, (long)userId, cancellationToken);

        return
        [
            .. rows.Select(job => new ReminderView(
                job.JobId,
                job.RunAt,
                TextOf(job),
                job.Recurrence,
                Recurrence.Describe(job.Recurrence))),
        ];
    }

    public Task<bool> CancelAsync(ulong userId, long jobId, CancellationToken cancellationToken = default)
        => jobs.CancelAsync(jobId, (long)userId, cancellationToken);

    public ReminderDelivery? Read(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        // A row whose payload can't be read is a bad row, not a transient failure — the handler
        // completes it instead of retrying forever.
        return Snowflake(job, JobKinds.GuildIdField) is { } guildId
               && Snowflake(job, JobKinds.ChannelIdField) is { } channelId
               && Snowflake(job, JobKinds.UserIdField) is { } userId
               && TextOf(job) is { Length: > 0 } text
            ? new ReminderDelivery(guildId, channelId, userId, text)
            : null;
    }

    public async Task<DateTimeOffset?> NextOccurrenceAsync(
        Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!Recurrence.IsValid(job.Recurrence))
        {
            return null;
        }

        // The owner's zone, not the guild's: "every day 9am" means 9am where they are.
        TimeZoneInfo zone = Snowflake(job, JobKinds.GuildIdField) is { } guildId
                            && Snowflake(job, JobKinds.UserIdField) is { } userId
            ? await zones.ForUserAsync(guildId, userId, cancellationToken)
            : TimeZoneInfo.Utc;

        return Recurrence.Next(job.Recurrence, job.RunAt, DateTimeOffset.UtcNow, zone);
    }

    public async Task<ScheduleResult> SetTimezoneAsync(
        ulong guildId,
        ulong userId,
        string? ianaId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            await members.SetTimezoneAsync((long)guildId, (long)userId, null, cancellationToken);
            return ScheduleResult.Ok("Cleared. I'll use the server's timezone for you.", 0);
        }

        var id = ianaId.Trim();

        // Shape-checked, not resolved: on a Windows dev box no IANA id resolves at all
        // (InvariantGlobalization strips ICU), and refusing a valid id there would make the
        // command untestable. The container resolves them for real. Same rule as ConfigKeys.
        if (!IanaId.LooksValid(id))
        {
            return ScheduleResult.Rejected(
                $"`{id}` isn't a timezone I recognise. Use an IANA id like `Asia/Ho_Chi_Minh`.");
        }

        await members.SetTimezoneAsync((long)guildId, (long)userId, id, cancellationToken);

        var now = ZoneResolver.Find(id) is { } zone
            ? $" It's {TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone):HH:mm} there."
            : string.Empty;

        return ScheduleResult.Ok($"Noted — you're on `{id}`.{now}", 0);
    }

    public Task<TimeZoneInfo> ResolveZoneAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default)
        => zones.ForUserAsync(guildId, userId, cancellationToken);

    private static string TextOf(Job job)
        => job.Payload[JobKinds.TextField]?.GetValue<string>() ?? string.Empty;

    /// <summary>
    /// Payload ids are stored as strings — a snowflake exceeds JSON's safe integer range, and
    /// <c>payload-&gt;&gt;'user_id'</c> comparisons in <see cref="IJobRepository"/> are textual.
    /// </summary>
    private static ulong? Snowflake(Job job, string field)
        => job.Payload[field]?.GetValue<string>() is { } raw
           && ulong.TryParse(raw, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
