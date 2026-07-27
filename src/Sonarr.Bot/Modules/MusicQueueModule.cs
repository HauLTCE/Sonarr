using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/queue</c>, <c>/remove</c>, <c>/duplicate-cleanup</c>, <c>/shuffle</c>, <c>/loop</c>,
/// <c>/fairqueue</c> (docs/07-commands.md#music).
/// </summary>
[RequireFeature(FeatureNames.Music)]
[RequireContext(ContextType.Guild)]
public sealed class MusicQueueModule(IMusicService music, IGuildConfigService config)
    : MusicModuleBase(music, config)
{
    [SlashCommand("queue", "What's coming up.")]
    public async Task QueueAsync([Summary("page", "Which page")] int page = 1)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (InputGuards.InRange(page, 1, 1000, "page") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        QueuePage? queue = await Music.GetQueueAsync(context, page);
        if (queue is null)
        {
            await RespondInvalidAsync("Nothing is playing.");
            return;
        }

        await RespondAsync(embed: Render(queue).Build());
    }

    [SlashCommand("remove", "Take a track out of the queue.")]
    public async Task RemoveAsync([Summary("position", "Its number in /queue")] int position)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (InputGuards.InRange(position, 1, 10000, "position") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        await RespondResultAsync(await Music.RemoveAsync(context, position));
    }

    [SlashCommand("duplicate-cleanup", "Collapse repeated tracks in the queue. DJ only.")]
    public async Task DuplicateCleanupAsync()
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.RemoveDuplicatesAsync(context));
        }
    }

    [SlashCommand("shuffle", "Shuffle the queue.")]
    public async Task ShuffleAsync()
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.ShuffleAsync(context));
        }
    }

    [SlashCommand("loop", "Loop this track, the whole queue, or nothing.")]
    public async Task LoopAsync([Summary("mode", "track, queue or off")] LoopMode mode)
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.SetLoopAsync(context, mode));
        }
    }

    [SlashCommand("fairqueue", "Round-robin the queue across requesters. DJ only.")]
    public async Task FairQueueAsync([Summary("enabled", "on or off")] bool enabled)
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.SetFairQueueAsync(context, enabled));
        }
    }

    private static EmbedBuilder Render(QueuePage queue)
    {
        var body = new StringBuilder();

        if (queue.Current is { } current)
        {
            body.Append("**Now** ").AppendLine(Line(current));
        }

        if (queue.Entries.Count == 0)
        {
            body.AppendLine().Append("Nothing queued behind it.");
        }
        else
        {
            body.AppendLine();
            foreach (QueueEntry entry in queue.Entries)
            {
                body.Append('`').Append(entry.Position).Append("` ")
                    .Append(Line(entry.Track))
                    .Append(" — <@").Append(entry.Track.RequesterId).AppendLine(">");
            }
        }

        return new EmbedBuilder()
            .WithTitle("Queue")
            .WithColor(new Color(0x5865F2))
            .WithDescription(body.ToString())
            .WithFooter(
                $"Page {queue.Page}/{queue.TotalPages} · {queue.TotalTracks} queued · {Duration(queue.Remaining)} left");
    }
}
