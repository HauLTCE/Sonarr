using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/play</c>, <c>/playnext</c>, <c>/pause</c>, <c>/resume</c>, <c>/stop</c>,
/// <c>/seek</c>, <c>/replay</c>, <c>/skip</c>, <c>/undo-skip</c>, <c>/previous</c>,
/// <c>/volume</c>, <c>/filter</c>, <c>/autoplay</c>, <c>/restore-queue</c>
/// (docs/07-commands.md#music). Guard, call, format.
/// </summary>
[RequireFeature(FeatureNames.Music)]
[RequireContext(ContextType.Guild)]
public sealed class MusicPlaybackModule(IMusicService music, IGuildConfigService config)
    : MusicModuleBase(music, config)
{
    [SlashCommand("play", "Play a track or add it to the queue. Search terms or a link.")]
    public async Task PlayAsync([Summary("query", "Search terms, or a link")] string query)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (InputGuards.Length(query, 500, "search") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        // Resolution goes out to Lavalink, which can take longer than Discord's 3 s window.
        await DeferAsync();

        PlayOutcome outcome = await Music.PlayAsync(context, query);

        if (outcome.Prompt is { } prompt)
        {
            // A playlist link is a big commitment; ask before dumping 47 tracks in the queue.
            var buttons = new ComponentBuilder()
                .WithButton("Queue all", $"music:pl:yes:{context.UserId}", ButtonStyle.Success)
                .WithButton("Just the first", $"music:pl:one:{context.UserId}", ButtonStyle.Secondary)
                .Build();

            await FollowupAsync($"{outcome.Message}", components: buttons);
            PlaylistPrompts.Remember(context.GuildId, context.UserId, prompt.Query);
            return;
        }

        await FollowupAsync(outcome.Message);
    }

    [ComponentInteraction("music:pl:*:*", ignoreGroupNames: true)]
    public async Task ConfirmPlaylistAsync(string choice, string requesterId)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        // The buttons belong to whoever ran /play — not to whoever clicks first.
        if (!ulong.TryParse(requesterId, out var owner) || owner != context.UserId)
        {
            await RespondInvalidAsync("Those buttons aren't yours — run `/play` yourself.");
            return;
        }

        if (PlaylistPrompts.Take(context.GuildId, context.UserId) is not { } query)
        {
            await RespondInvalidAsync("That prompt expired — run `/play` again.");
            return;
        }

        await DeferAsync();

        PlayOutcome outcome = choice == "yes"
            ? await Music.PlayAsync(context, query, confirmPlaylist: true)
            : await Music.PlayNextAsync(context, query);

        await FollowupAsync(outcome.Message);
    }

    [SlashCommand("playnext", "Put a track at the front of the queue.")]
    public async Task PlayNextAsync([Summary("query", "Search terms, or a link")] string query)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (InputGuards.Length(query, 500, "search") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        await DeferAsync();
        PlayOutcome outcome = await Music.PlayNextAsync(context, query);
        await FollowupAsync(outcome.Message);
    }

    [SlashCommand("pause", "Pause playback.")]
    public async Task PauseAsync()
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.PauseAsync(context));
        }
    }

    // /resume is the unpause, because that is what everyone reaches for. The crash-session
    // restore held this name until now and is /restore-queue instead — it is the rare one, and
    // it is what pushed the unpause onto the unguessable /resume-playback.
    [SlashCommand("resume", "Carry on from where it paused.")]
    public async Task ResumePlaybackAsync()
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.ResumeAsync(context));
        }
    }

    [SlashCommand("stop", "Stop, clear the queue and leave. DJ only.")]
    public async Task StopAsync()
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.StopAsync(context));
        }
    }

    [SlashCommand("seek", "Jump to a position, like 1:30.")]
    public async Task SeekAsync([Summary("position", "mm:ss, or seconds")] string position)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (ParsePosition(position) is not { } target)
        {
            await RespondInvalidAsync("Give me a position like `1:30`, `0:45` or `90`.");
            return;
        }

        await RespondResultAsync(await Music.SeekAsync(context, target));
    }

    [SlashCommand("replay", "Start the current track over.")]
    public async Task ReplayAsync()
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.ReplayAsync(context));
        }
    }

    [SlashCommand("skip", "Skip the current track. Busy channels vote.")]
    public async Task SkipAsync()
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        SkipOutcome outcome = await Music.SkipAsync(context);

        // A pending vote is public on purpose: the rest of the channel has to know to vote.
        if (outcome.Skipped || outcome.Needed > 0)
        {
            await RespondPublicAsync(outcome.Message);
        }
        else
        {
            await RespondInvalidAsync(outcome.Message);
        }
    }

    [SlashCommand("undo-skip", "Put the track you just skipped back on. 10 second window.")]
    public async Task UndoSkipAsync()
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.UndoSkipAsync(context));
        }
    }

    [SlashCommand("previous", "Play the previous track again.")]
    public async Task PreviousAsync()
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.PlayPreviousAsync(context));
        }
    }

    [SlashCommand("volume", "Set playback volume, 0-150. DJ only.")]
    public async Task VolumeAsync([Summary("percent", "0 to 150")] int percent)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (InputGuards.InRange(percent, MusicRules.MinVolume, MusicRules.MaxVolume, "volume") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        await RespondResultAsync(await Music.SetVolumeAsync(context, percent));
    }

    [SlashCommand("filter", "Apply an audio filter. DJ only.")]
    public async Task FilterAsync([Summary("filter", "Which filter")] MusicFilter filter)
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.ApplyFilterAsync(context, filter));
        }
    }

    [SlashCommand("autoplay", "Keep playing when the queue runs dry. DJ only.")]
    public async Task AutoplayAsync([Summary("mode", "off, on, or smart")] AutoplayMode mode)
    {
        if (await ContextAsync() is { } context)
        {
            await RespondResultAsync(await Music.SetAutoplayAsync(context, mode));
        }
    }

    [SlashCommand("restore-queue", "Restore the queue from before I restarted.")]
    public async Task ResumeSessionAsync()
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        await DeferAsync();
        MusicResult result = await Music.ResumeSessionAsync(context);
        await FollowupAsync(result.Message);
    }

    /// <summary><c>mm:ss</c>, <c>h:mm:ss</c> or plain seconds. <c>null</c> when it is none of those.</summary>
    private static TimeSpan? ParsePosition(string value)
    {
        var trimmed = value.Trim();

        if (int.TryParse(trimmed, out var seconds))
        {
            return seconds < 0 ? null : TimeSpan.FromSeconds(seconds);
        }

        string[] formats = [@"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss"];
        return TimeSpan.TryParseExact(trimmed, formats, null, out var parsed) ? parsed : null;
    }
}

/// <summary>
/// The pending <c>/play</c> playlist confirmation, per guild+user. In memory because it lives for
/// one click and a lost prompt costs a re-run of <c>/play</c>.
/// </summary>
/// <remarks>
/// ponytail: single-process only, so a restart between prompt and click loses it (the user gets
/// "that prompt expired"). Upgrade path: a RedisKeys entry with a short CacheTtl if Sonarr ever
/// runs more than one instance.
/// </remarks>
internal static class PlaylistPrompts
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ulong, ulong), string> Pending = new();

    public static void Remember(ulong guildId, ulong userId, string query)
        => Pending[(guildId, userId)] = query;

    public static string? Take(ulong guildId, ulong userId)
        => Pending.TryRemove((guildId, userId), out var query) ? query : null;
}
