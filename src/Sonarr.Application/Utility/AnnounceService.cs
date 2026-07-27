using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Jobs;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Utility;

/// <inheritdoc cref="IAnnounceService"/>
internal sealed class AnnounceService(
    IJobRepository jobs,
    ZoneResolver zones,
    ILogger<AnnounceService> log) : IAnnounceService
{
    /// <summary>Fits a plain message; longer belongs in an embed someone wrote by hand.</summary>
    public const int MaxMessageLength = 1800;

    public async Task<ScheduleResult> ScheduleAsync(
        ulong guildId,
        ulong channelId,
        ulong actorId,
        string schedule,
        string message,
        CancellationToken cancellationToken = default)
    {
        var body = message?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            return ScheduleResult.Rejected("Announce what? Give me something to say.");
        }

        if (body.Length > MaxMessageLength)
        {
            return ScheduleResult.Rejected(
                $"That's {body.Length} characters — keep an announcement under {MaxMessageLength}.");
        }

        // The server's timezone decides when the server hears from me, not the admin's.
        TimeZoneInfo zone = await zones.ForGuildAsync(guildId, cancellationToken);
        if (!WhenParser.TryParse(schedule, DateTimeOffset.UtcNow, zone, out WhenResult? parsed, out var error))
        {
            return ScheduleResult.Rejected(error);
        }

        JsonObject payload = new()
        {
            [JobKinds.GuildIdField] = guildId.ToString(CultureInfo.InvariantCulture),
            [JobKinds.ChannelIdField] = channelId.ToString(CultureInfo.InvariantCulture),
            // Ownership field, so an admin's own /reminders-style cancel could find it later and
            // the audit shows who armed it. Announces are not listed by /reminders (different kind).
            [JobKinds.UserIdField] = actorId.ToString(CultureInfo.InvariantCulture),
            [JobKinds.TextField] = body,
        };

        var jobId = await jobs.ScheduleAsync(
            JobKinds.Announce,
            parsed!.RunAt,
            payload,
            parsed.Recurrence,
            cancellationToken);

        log.LogInformation(
            "Announce {JobId} scheduled in guild {GuildId} at {RunAt} ({Recurrence}) by {ActorId}",
            jobId, guildId, parsed.RunAt, parsed.Recurrence ?? "once", actorId);

        var repeat = parsed.Recurrence is null ? string.Empty : $", {Recurrence.Describe(parsed.Recurrence)}";
        return ScheduleResult.Ok($"Queued for <t:{parsed.RunAt.ToUnixTimeSeconds()}:f>{repeat} — `#{jobId}`.", jobId);
    }

    public AnnounceDelivery? Read(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return Snowflake(job, JobKinds.GuildIdField) is { } guildId
               && Snowflake(job, JobKinds.ChannelIdField) is { } channelId
               && job.Payload[JobKinds.TextField]?.GetValue<string>() is { Length: > 0 } text
            ? new AnnounceDelivery(guildId, channelId, text)
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

        TimeZoneInfo zone = Snowflake(job, JobKinds.GuildIdField) is { } guildId
            ? await zones.ForGuildAsync(guildId, cancellationToken)
            : TimeZoneInfo.Utc;

        return Recurrence.Next(job.Recurrence, job.RunAt, DateTimeOffset.UtcNow, zone);
    }

    private static ulong? Snowflake(Job job, string field)
        => job.Payload[field]?.GetValue<string>() is { } raw
           && ulong.TryParse(raw, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
