using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Jobs;

namespace Sonarr.Bot.Discord.Jobs;

/// <summary>
/// Posts a scheduled announcement when its <c>core.job</c> row comes due, and re-arms recurring
/// ones. Same shape as <see cref="ReminderJobHandler"/>: unrecoverable rows complete quietly,
/// transient Discord failures throw so the scheduler backs off and retries.
/// </summary>
public sealed class AnnounceJobHandler(
    DiscordSocketClient client,
    IAnnounceService announcements,
    ILogger<AnnounceJobHandler> log) : IJobHandler, IRecurringJobHandler
{
    public string Kind => JobKinds.Announce;

    public async Task HandleAsync(Job job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (announcements.Read(job) is not { } delivery)
        {
            log.LogError("Job {JobId} has no usable announce payload; dropping it", job.JobId);
            return;
        }

        if (client.GetGuild(delivery.GuildId)?.GetTextChannel(delivery.ChannelId) is not { } channel)
        {
            log.LogWarning(
                "Announcement {JobId} skipped — channel {ChannelId} in {GuildId} is gone or invisible",
                job.JobId, delivery.ChannelId, delivery.GuildId);
            return;
        }

        // Plain text, not an embed: an announcement is the staff's voice, and @everyone /
        // role pings inside an embed description do not notify anyone.
        await channel.SendMessageAsync(
            text: delivery.Text,
            allowedMentions: AllowedMentions.All,
            options: new RequestOptions { CancelToken = ct });

        log.LogInformation(
            "Announcement {JobId} posted in {ChannelId}/{GuildId}",
            job.JobId, delivery.ChannelId, delivery.GuildId);
    }

    public Task<DateTimeOffset?> NextRunAtAsync(Job job, CancellationToken ct = default)
        => announcements.NextOccurrenceAsync(job, ct);
}
