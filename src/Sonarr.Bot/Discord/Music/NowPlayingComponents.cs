using Discord;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>The now-playing embed and its buttons. Ids are constants so the module can match them.</summary>
public static class NowPlayingComponents
{
    public const string Pause = "music:np:pause";
    public const string Resume = "music:np:resume";
    public const string Skip = "music:np:skip";
    public const string RateUp = "music:np:up";
    public const string RateDown = "music:np:down";

    public static Embed Embed(NowPlayingView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        TrackInfo track = view.Track;

        var embed = new EmbedBuilder()
            .WithAuthor(view.IsPaused ? "Paused" : "Now playing")
            .WithTitle(track.Title)
            .WithUrl(track.Uri)
            .WithColor(new Color(0x5865F2))
            .WithDescription(track.IsStream
                ? "Live stream"
                : $"`{Bar(view.Position, track.Duration)}` {Clock(view.Position)} / {Clock(track.Duration)}")
            .AddField("Requested by", $"<@{track.RequesterId}>", inline: true)
            .AddField("Volume", $"{view.Volume}%", inline: true)
            .AddField("Loop", view.Loop switch
            {
                LoopMode.Track => "this track",
                LoopMode.Queue => "the queue",
                _ => "off",
            }, inline: true);

        if (!string.IsNullOrWhiteSpace(track.Author))
        {
            embed.AddField("Artist", track.Author, inline: true);
        }

        embed.AddField("Queue", view.QueueLength == 0 ? "empty" : $"{view.QueueLength} waiting", inline: true);

        embed.AddField("Ratings", $"{view.Likes} up · {view.Dislikes} down", inline: true);

        if (view.TracksUntilYours is { } until)
        {
            embed.WithFooter(until == 0 ? "Yours is next." : $"{until} tracks until yours.");
        }

        return embed.Build();
    }

    public static MessageComponent Buttons(NowPlayingView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new ComponentBuilder()
            .WithButton(
                view.IsPaused ? "Resume" : "Pause",
                view.IsPaused ? Resume : Pause,
                ButtonStyle.Secondary)
            .WithButton("Skip", Skip, ButtonStyle.Secondary)
            .WithButton($"{view.Likes}", RateUp, ButtonStyle.Success, new Emoji("\U0001F44D"))
            .WithButton($"{view.Dislikes}", RateDown, ButtonStyle.Danger, new Emoji("\U0001F44E"))
            .Build();
    }

    /// <summary>Twelve blocks of progress. Text, because an image per track is not free on a J2900.</summary>
    private static string Bar(TimeSpan position, TimeSpan duration)
    {
        const int width = 12;
        var filled = duration > TimeSpan.Zero
            ? (int)Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds * width, 0, width)
            : 0;

        return string.Concat(new string('█', filled), new string('─', width - filled));
    }

    private static string Clock(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}

/// <summary>
/// Keeps <b>one</b> now-playing message per guild: the id is remembered in
/// <c>music:np_msg:{guild}</c> and the message is edited rather than reposted, so a busy channel
/// does not fill up with stale embeds (docs/07-commands.md — /nowplaying).
/// </summary>
public sealed class NowPlayingPresenter(IMusicSessionCache cache, ILogger<NowPlayingPresenter> log)
{
    public async Task ShowAsync(
        IMessageChannel channel, ulong guildId, NowPlayingView view, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(view);

        Embed embed = NowPlayingComponents.Embed(view);
        MessageComponent buttons = NowPlayingComponents.Buttons(view);

        var existing = await cache.GetNowPlayingMessageAsync(guildId, ct).ConfigureAwait(false);
        if (existing is { } messageId)
        {
            try
            {
                if (await channel.GetMessageAsync(messageId).ConfigureAwait(false) is IUserMessage message)
                {
                    await message.ModifyAsync(m =>
                    {
                        m.Content = string.Empty;
                        m.Embed = embed;
                        m.Components = buttons;
                    }).ConfigureAwait(false);
                    return;
                }
            }
            // global:: — this file's namespace is Sonarr.Bot.Discord.Music, so plain Discord.Net
            // resolves against that first and fails.
            catch (global::Discord.Net.HttpException ex)
            {
                // Deleted, or in a channel we can no longer see. Post a fresh one.
                log.LogDebug(ex, "Now-playing message {MessageId} could not be edited", messageId);
            }
        }

        IUserMessage posted = await channel
            .SendMessageAsync(embed: embed, components: buttons)
            .ConfigureAwait(false);

        await cache.SetNowPlayingMessageAsync(guildId, posted.Id, ct).ConfigureAwait(false);
    }
}
