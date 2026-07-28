using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Jobs;

namespace Sonarr.Bot.Discord.Jobs;

/// <summary>
/// Opens a time capsule when its <c>core.job</c> row comes due (docs/08). Same shape as
/// <see cref="ReminderJobHandler"/>: nothing that a retry cannot improve is retried.
/// </summary>
/// <remarks>
/// Not <see cref="IRecurringJobHandler"/> — a capsule opens once, and the service refuses a
/// recurring phrase at write time.
/// </remarks>
public sealed class CapsuleJobHandler(
    DiscordSocketClient client,
    ICapsuleService capsules,
    ILogger<CapsuleJobHandler> log) : IJobHandler
{
    public string Kind => JobKinds.CapsuleOpen;

    public async Task HandleAsync(Job job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (await capsules.ReadAsync(job, ct).ConfigureAwait(false) is not { } capsule)
        {
            // Unreadable payload, deleted capsule, or already opened. None of those improve on a
            // retry, so the row completes.
            log.LogInformation("Capsule job {JobId} has nothing to deliver; closing it", job.JobId);
            return;
        }

        if (client.GetGuild((ulong)capsule.GuildId)?.GetTextChannel((ulong)capsule.ChannelId)
            is not { } channel)
        {
            log.LogWarning(
                "Capsule {CapsuleId} skipped — channel {ChannelId} in {GuildId} is gone or invisible",
                capsule.CapsuleId, capsule.ChannelId, capsule.GuildId);
            return;
        }

        Embed embed = new EmbedBuilder()
            .WithTitle("Time capsule")
            // Sanitized: a year-old line goes into a public channel, and the author is not around
            // to answer for what it does to the formatting.
            .WithDescription(Format.Sanitize(capsule.Message))
            .WithColor(new Color(0x8E6BC8))
            .WithFooter($"sealed <t:{capsule.CreatedAt.ToUnixTimeSeconds()}:D> · #{capsule.CapsuleId}")
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(
            text: MentionUtils.MentionUser((ulong)capsule.AuthorId),
            embed: embed,
            // The author asked to be found; nobody they wrote about did.
            allowedMentions: new AllowedMentions { UserIds = [(ulong)capsule.AuthorId] },
            options: new RequestOptions { CancelToken = ct });

        // After the post, not before: see CapsuleService.MarkOpenedAsync. CancellationToken.None
        // because a shutdown between the send and the stamp is exactly the case that would
        // re-deliver.
        if (!await capsules.MarkOpenedAsync(capsule.CapsuleId, CancellationToken.None).ConfigureAwait(false))
        {
            log.LogWarning(
                "Capsule {CapsuleId} was posted but already marked delivered — possible duplicate",
                capsule.CapsuleId);
        }

        log.LogInformation(
            "Capsule {CapsuleId} opened in {ChannelId}/{GuildId}",
            capsule.CapsuleId, capsule.ChannelId, capsule.GuildId);
    }
}
