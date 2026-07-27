using Lavalink4NET.Filters;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>Transport: pause, resume, stop, seek, replay, skip, undo-skip, previous, volume, filters.</summary>
public sealed partial class MusicService
{
    public async Task<MusicResult> PauseAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        if (player.IsPaused)
        {
            return MusicResult.Fail("Already paused.");
        }

        await player.PauseAsync(cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok("Paused.");
    }

    public async Task<MusicResult> ResumeAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        if (!player.IsPaused)
        {
            return MusicResult.Fail("Nothing to resume — I'm already playing.");
        }

        await player.ResumeAsync(cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok("Playing again.");
    }

    public async Task<MusicResult> StopAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken, djOnly: true).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        await player.Queue.ClearAsync(cancellationToken).ConfigureAwait(false);
        await player.StopAsync(cancellationToken).ConfigureAwait(false);
        await player.DisconnectAsync(cancellationToken).ConfigureAwait(false);

        // The session is over on purpose, so /resume must not offer to restore it.
        await cache.ClearSessionAsync(context.GuildId, cancellationToken).ConfigureAwait(false);
        await cache.ClearNowPlayingMessageAsync(context.GuildId, cancellationToken).ConfigureAwait(false);

        return MusicResult.Ok("Stopped and cleared the queue.");
    }

    public async Task<MusicResult> SeekAsync(
        MusicContext context, TimeSpan position, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        if (player.CurrentTrack is not { } track)
        {
            return MusicResult.Fail("Nothing is playing.");
        }

        if (position < TimeSpan.Zero)
        {
            return MusicResult.Fail("That's before the start of the track.");
        }

        if (!track.IsSeekable)
        {
            return MusicResult.Fail("This one can't be seeked — live streams don't rewind.");
        }

        if (track.Duration > TimeSpan.Zero && position > track.Duration)
        {
            return MusicResult.Fail($"That's past the end — the track is {track.Duration:mm\\:ss}.");
        }

        await player.SeekAsync(position, cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok($"Jumped to {position:mm\\:ss}.");
    }

    public async Task<MusicResult> ReplayAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        if (player.CurrentTrack is not { IsSeekable: true })
        {
            return MusicResult.Fail("Nothing seekable is playing.");
        }

        await player.SeekAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok("From the top.");
    }

    public async Task<SkipOutcome> SkipAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return new SkipOutcome(false, 0, 0, access.Problem!);
        }

        if (player.CurrentItem is not { } current)
        {
            return new SkipOutcome(false, 0, 0, "Nothing is playing.");
        }

        var listeners = await gateway
            .CountListenersAsync(context.GuildId, player.VoiceChannelId, cancellationToken)
            .ConfigureAwait(false);

        var requester = TrackMapping.Required(current).RequesterId;
        var solo = listeners <= 1 || requester == context.UserId;

        if (!context.IsDj && !solo && listeners >= MusicRules.VoteSkipThreshold)
        {
            var needed = MusicRules.VotesNeeded(listeners);

            var votes = await cache.AddVoteSkipAsync(context.GuildId, context.UserId, cancellationToken)
                .ConfigureAwait(false);

            // Vote-skip FAILS CLOSED (docs/05-caching.md). The cache swallows Redis faults and
            // returns 0, and 0 is impossible after a successful add — so a zero here means the
            // tally is unavailable, and one person must not be able to skip on a busy channel.
            if (votes == 0)
            {
                return new SkipOutcome(false, 0, needed, "Vote-skip is unavailable right now. Try again in a moment.");
            }

            if (votes < needed)
            {
                return new SkipOutcome(false, votes, needed, $"Vote noted — **{votes}/{needed}** to skip.");
            }
        }

        await RememberSkipAsync(context.GuildId, current, cancellationToken).ConfigureAwait(false);
        await player.SkipAsync(1, cancellationToken).ConfigureAwait(false);
        await cache.ClearVoteSkipAsync(context.GuildId, cancellationToken).ConfigureAwait(false);

        return new SkipOutcome(true, 0, 0, "Skipped.");
    }

    /// <summary>Parks the skipped track so <c>/undo-skip</c> has 10 seconds to take it back.</summary>
    private async Task RememberSkipAsync(ulong guildId, ITrackQueueItem item, CancellationToken ct)
    {
        TrackInfo skipped = TrackMapping.Required(item);
        await cache.SetUndoSkipAsync(guildId, skipped, ct).ConfigureAwait(false);
    }

    public async Task<MusicResult> UndoSkipAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        TrackInfo? skipped = await cache.GetUndoSkipAsync<TrackInfo>(context.GuildId, cancellationToken)
            .ConfigureAwait(false);

        if (skipped is null)
        {
            return MusicResult.Fail("Nothing to bring back — the undo window is 10 seconds.");
        }

        await player.Queue.InsertAsync(0, TrackMapping.FromDomain(skipped), cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok($"**{skipped.Title}** is back, up next.");
    }

    public async Task<MusicResult> PlayPreviousAsync(MusicContext context, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        ITrackQueueItem? previous = player.Queue.History?.LastOrDefault();
        if (previous is null)
        {
            return MusicResult.Fail("No history yet.");
        }

        await player.PlayAsync(previous, enqueue: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        return MusicResult.Ok($"Back to **{TrackMapping.Required(previous).Title}**.");
    }

    public async Task<MusicResult> SetVolumeAsync(
        MusicContext context, int volume, CancellationToken cancellationToken = default)
    {
        if (volume is < MusicRules.MinVolume or > MusicRules.MaxVolume)
        {
            return MusicResult.Fail($"Volume goes from {MusicRules.MinVolume} to {MusicRules.MaxVolume}.");
        }

        var access = await RequirePlayerAsync(context, cancellationToken, djOnly: true).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        await player.SetVolumeAsync(volume / 100f, cancellationToken).ConfigureAwait(false);

        // Remembered per user so the next session starts where they left it (docs/07 — /volume).
        await prefs.SetVolumeAsync(context.GuildId, context.UserId, volume, cancellationToken).ConfigureAwait(false);

        return MusicResult.Ok($"Volume at **{volume}%**.");
    }

    public async Task<MusicResult> ApplyFilterAsync(
        MusicContext context, MusicFilter filter, CancellationToken cancellationToken = default)
    {
        var access = await RequirePlayerAsync(context, cancellationToken, djOnly: true).ConfigureAwait(false);
        if (access.Player is not { } player)
        {
            return MusicResult.Fail(access.Problem!);
        }

        IPlayerFilters filters = player.Filters;
        filters.Clear();

        switch (filter)
        {
            case MusicFilter.Clear:
                break;
            case MusicFilter.Bassboost:
                filters.Equalizer = new EqualizerFilterOptions(BassBoost);
                break;
            case MusicFilter.Nightcore:
                filters.Timescale = new TimescaleFilterOptions(Speed: 1.2f, Pitch: 1.2f, Rate: null);
                break;
            case MusicFilter.Karaoke:
                filters.Karaoke = new KaraokeFilterOptions(Level: 1f, MonoLevel: 1f, FilterBand: 220f, FilterWidth: 100f);
                break;
            case MusicFilter.Speed:
                filters.Timescale = new TimescaleFilterOptions(Speed: 1.5f, Pitch: null, Rate: null);
                break;
            default:
                return MusicResult.Fail("I don't know that filter.");
        }

        await filters.CommitAsync(cancellationToken).ConfigureAwait(false);

        return MusicResult.Ok(filter is MusicFilter.Clear
            ? "Filters cleared."
            : $"Filter set to **{filter.ToString().ToLowerInvariant()}**.");
    }

    /// <summary>Low bands lifted, the rest flat. Built once — an Equalizer is 15 floats.</summary>
    private static readonly Equalizer BassBoost = BuildBassBoost();

    private static Equalizer BuildBassBoost()
    {
        var builder = Equalizer.CreateBuilder();
        builder.Band0 = 0.2f;
        builder.Band1 = 0.2f;
        builder.Band2 = 0.15f;
        builder.Band3 = 0.1f;
        builder.Band4 = 0.05f;
        return builder.Build();
    }
}
