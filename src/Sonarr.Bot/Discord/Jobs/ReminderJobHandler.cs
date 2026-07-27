using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Jobs;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Discord.Jobs;

/// <summary>
/// Delivers a due reminder into the channel it was set in, and tells the scheduler when a
/// recurring one should fire next.
/// </summary>
/// <remarks>
/// Idempotent where it can be: a missing guild, a missing channel or an unreadable payload all
/// complete quietly — none of those improve on a retry. A Discord 5xx is allowed to throw so the
/// scheduler's backoff gets a turn.
/// </remarks>
public sealed class ReminderJobHandler(
    DiscordSocketClient client,
    IReminderService reminders,
    ILogger<ReminderJobHandler> log) : IJobHandler, IRecurringJobHandler
{
    public string Kind => JobKinds.Reminder;

    public async Task HandleAsync(Job job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (reminders.Read(job) is not { } delivery)
        {
            log.LogError("Job {JobId} has no usable reminder payload; dropping it", job.JobId);
            return;
        }

        if (client.GetGuild(delivery.GuildId)?.GetTextChannel(delivery.ChannelId) is not { } channel)
        {
            log.LogWarning(
                "Reminder {JobId} skipped — channel {ChannelId} in {GuildId} is gone or invisible",
                job.JobId, delivery.ChannelId, delivery.GuildId);
            return;
        }

        Embed embed = new EmbedBuilder()
            .WithTitle("Reminder")
            .WithDescription(delivery.Text)
            .WithColor(new Color(0x5865F2))
            .WithFooter(job.Recurrence is null
                ? $"#{job.JobId}"
                : $"#{job.JobId} · {Recurrence.Describe(job.Recurrence)} · /reminders cancel to stop")
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(
            text: MentionUtils.MentionUser(delivery.UserId),
            embed: embed,
            allowedMentions: new AllowedMentions { UserIds = [delivery.UserId] },
            options: new RequestOptions { CancelToken = ct });

        // The text itself is never logged (docs/06-data-and-privacy.md: no message content).
        log.LogInformation("Reminder {JobId} delivered to {UserId}", job.JobId, delivery.UserId);
    }

    public Task<DateTimeOffset?> NextRunAtAsync(Job job, CancellationToken ct = default)
        => reminders.NextOccurrenceAsync(job, ct);
}
