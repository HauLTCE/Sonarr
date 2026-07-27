using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Music;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/nowplaying</c> with its buttons, the 👍/👎 ratings, and <c>/grab</c> (plus the message
/// context menu version). One now-playing message per guild, edited in place — the id lives in
/// <c>music:np_msg:{guild}</c> (docs/05-caching.md).
/// </summary>
[RequireFeature(FeatureNames.Music)]
[RequireContext(ContextType.Guild)]
public sealed class MusicNowPlayingModule(
    IMusicService music,
    IGuildConfigService config,
    NowPlayingPresenter presenter) : MusicModuleBase(music, config)
{
    [SlashCommand("nowplaying", "What's playing, with the controls.")]
    public async Task NowPlayingAsync()
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        NowPlayingView? view = await Music.GetNowPlayingAsync(context);
        if (view is null)
        {
            await RespondInvalidAsync("Nothing is playing.");
            return;
        }

        await DeferAsync();
        await presenter.ShowAsync(Context.Channel, context.GuildId, view);
        await FollowupAsync("Up there.", ephemeral: true);
    }

    [ComponentInteraction(NowPlayingComponents.RateUp, ignoreGroupNames: true)]
    public Task RateUpAsync() => RateAsync(1);

    [ComponentInteraction(NowPlayingComponents.RateDown, ignoreGroupNames: true)]
    public Task RateDownAsync() => RateAsync(-1);

    [ComponentInteraction(NowPlayingComponents.Pause, ignoreGroupNames: true)]
    public Task PauseButtonAsync() => ActAsync(static (m, c) => m.PauseAsync(c));

    [ComponentInteraction(NowPlayingComponents.Resume, ignoreGroupNames: true)]
    public Task ResumeButtonAsync() => ActAsync(static (m, c) => m.ResumeAsync(c));

    [ComponentInteraction(NowPlayingComponents.Skip, ignoreGroupNames: true)]
    public async Task SkipButtonAsync()
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        SkipOutcome outcome = await Music.SkipAsync(context);
        await RespondPersonalAsync(outcome.Message);
        await RefreshAsync(context);
    }

    [SlashCommand("grab", "DM yourself the track that's playing.")]
    public Task GrabAsync() => SendGrabAsync();

    [MessageCommand("Grab this track")]
    public Task GrabContextAsync(IMessage message) => SendGrabAsync();

    private async Task SendGrabAsync()
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        TrackInfo? track = await Music.GrabAsync(context);
        if (track is null)
        {
            await RespondInvalidAsync("Nothing is playing.");
            return;
        }

        try
        {
            await Context.User.SendMessageAsync($"Here it is: {Line(track)}");
            await RespondPersonalAsync("Sent it to your DMs.");
        }
        // global:: — this file's own namespace is Sonarr.Bot.Discord.*, so plain Discord.Net
        // resolves against that first and fails.
        catch (global::Discord.Net.HttpException)
        {
            // Closed DMs are the normal case, not a fault — answer in the channel instead.
            await RespondPersonalAsync($"Your DMs are closed, so here it is: {Line(track)}");
        }
    }

    private async Task RateAsync(short vote)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        MusicResult result = await Music.RateCurrentAsync(context, vote);
        await RespondPersonalAsync(result.Message);
        await RefreshAsync(context);
    }

    private async Task ActAsync(Func<IMusicService, MusicContext, Task<MusicResult>> action)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        MusicResult result = await action(Music, context);
        await RespondPersonalAsync(result.Message);
        await RefreshAsync(context);
    }

    /// <summary>Re-renders the tracked message so the buttons and counts match reality.</summary>
    private async Task RefreshAsync(MusicContext context)
    {
        NowPlayingView? view = await Music.GetNowPlayingAsync(context);
        if (view is not null)
        {
            await presenter.ShowAsync(Context.Channel, context.GuildId, view);
        }
    }
}
