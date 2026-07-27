using Sonarr.Domain.Music;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// Every music behaviour, in domain terms (docs/02-architecture.md). Zero Discord and zero
/// Lavalink types cross this line: the module gathers who/where into a
/// <see cref="MusicContext"/>, the implementation owns the player, the queue and the wording.
/// </summary>
/// <remarks>
/// <para>
/// <b>Permission split.</b> DJ-only actions check <see cref="MusicContext.IsDj"/> here as well as
/// in the module, for the same reason moderation re-checks hierarchy: the module is the fast
/// path, not the authority.
/// </para>
/// <para>
/// <b>Voice requirement.</b> Anything that changes playback needs the caller in the bot's channel;
/// read-only calls (<c>/queue</c>, <c>/nowplaying</c>, stats) do not.
/// </para>
/// </remarks>
public interface IMusicService
{
    /// <summary>
    /// <c>/play</c>. A playlist link comes back as a <see cref="PlayOutcome.Prompt"/> instead of
    /// 47 surprise tracks; call again with <paramref name="confirmPlaylist"/> to accept.
    /// </summary>
    Task<PlayOutcome> PlayAsync(
        MusicContext context,
        string query,
        bool confirmPlaylist = false,
        CancellationToken cancellationToken = default);

    /// <summary><c>/playnext</c> — same resolution as <c>/play</c>, lands at the front of the queue.</summary>
    Task<PlayOutcome> PlayNextAsync(
        MusicContext context, string query, CancellationToken cancellationToken = default);

    Task<MusicResult> PauseAsync(MusicContext context, CancellationToken cancellationToken = default);

    Task<MusicResult> ResumeAsync(MusicContext context, CancellationToken cancellationToken = default);

    /// <summary><c>/stop</c> — stop and clear the queue. DJ only.</summary>
    Task<MusicResult> StopAsync(MusicContext context, CancellationToken cancellationToken = default);

    /// <summary><c>/seek</c>. Rejects a position past the end, and streams (nothing to seek in).</summary>
    Task<MusicResult> SeekAsync(
        MusicContext context, TimeSpan position, CancellationToken cancellationToken = default);

    /// <summary><c>/replay</c> — back to 0:00 of the current track.</summary>
    Task<MusicResult> ReplayAsync(MusicContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>/skip</c>. With fewer than <see cref="MusicRules.VoteSkipThreshold"/> listeners it just
    /// skips; above that it counts votes in <c>music:voteskip:{guild}</c>. A DJ always bypasses.
    /// </summary>
    Task<SkipOutcome> SkipAsync(MusicContext context, CancellationToken cancellationToken = default);

    /// <summary><c>/undo-skip</c> — only inside the 10 s window held by <c>music:undo_skip:{guild}</c>.</summary>
    Task<MusicResult> UndoSkipAsync(MusicContext context, CancellationToken cancellationToken = default);

    /// <summary><c>/queue</c>. <c>null</c> when nothing is playing.</summary>
    Task<QueuePage?> GetQueueAsync(
        MusicContext context, int page = 1, CancellationToken cancellationToken = default);

    /// <summary><c>/remove</c> — your own track, or anyone's if you are a DJ.</summary>
    Task<MusicResult> RemoveAsync(
        MusicContext context, int position, CancellationToken cancellationToken = default);

    /// <summary><c>/duplicate-cleanup</c> (DJ) — collapses repeated uris in the queue.</summary>
    Task<MusicResult> RemoveDuplicatesAsync(MusicContext context, CancellationToken cancellationToken = default);

    Task<MusicResult> ShuffleAsync(MusicContext context, CancellationToken cancellationToken = default);

    Task<MusicResult> SetLoopAsync(MusicContext context, LoopMode mode, CancellationToken cancellationToken = default);

    /// <summary><c>/fairqueue</c> (DJ) — round-robin the queue across requesters.</summary>
    Task<MusicResult> SetFairQueueAsync(
        MusicContext context, bool enabled, CancellationToken cancellationToken = default);

    /// <summary><c>/previous</c> — replay the last track from history.</summary>
    Task<MusicResult> PlayPreviousAsync(MusicContext context, CancellationToken cancellationToken = default);

    /// <summary><c>/nowplaying</c>. <c>null</c> when nothing is playing.</summary>
    Task<NowPlayingView?> GetNowPlayingAsync(MusicContext context, CancellationToken cancellationToken = default);

    /// <summary>👍/👎 on the now-playing embed. <paramref name="vote"/> is +1 or -1.</summary>
    Task<MusicResult> RateCurrentAsync(
        MusicContext context, short vote, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>/volume</c> (DJ), 0–150. Also remembered per user in <c>music.user_prefs</c> so their
    /// next session starts where they left it.
    /// </summary>
    Task<MusicResult> SetVolumeAsync(
        MusicContext context, int volume, CancellationToken cancellationToken = default);

    /// <summary><c>/filter</c> (DJ). <see cref="MusicFilter.Clear"/> removes all of them.</summary>
    Task<MusicResult> ApplyFilterAsync(
        MusicContext context, MusicFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>/grab</c>. Returns the current track for the caller to DM; <c>null</c> when idle.
    /// The DM itself is a transport concern, so it stays in the module.
    /// </summary>
    Task<TrackInfo?> GrabAsync(MusicContext context, CancellationToken cancellationToken = default);

    /// <summary><c>/playlist save</c> — the live queue (current track first) under a name.</summary>
    Task<MusicResult> SavePlaylistAsync(
        MusicContext context, string name, CancellationToken cancellationToken = default);

    /// <summary><c>/playlist load</c> — appends the saved tracks to the queue.</summary>
    Task<MusicResult> LoadPlaylistAsync(
        MusicContext context, string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaylistSummary>> ListPlaylistsAsync(
        ulong guildId, CancellationToken cancellationToken = default);

    /// <summary><c>/playlist delete</c> — owner only, unless the caller is a DJ.</summary>
    Task<MusicResult> DeletePlaylistAsync(
        MusicContext context, string name, CancellationToken cancellationToken = default);

    /// <summary><c>/resume</c> — rebuild the queue and position from <c>music:session:{guild}</c>.</summary>
    Task<MusicResult> ResumeSessionAsync(MusicContext context, CancellationToken cancellationToken = default);

    Task<MusicStats> GetStatsAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary><c>/mytracks</c> — the caller's recent requests.</summary>
    Task<IReadOnlyList<TrackPlayCount>> GetMyTracksAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary><c>/toptracks</c> — crowd favourites from the 👍/👎 buttons.</summary>
    Task<IReadOnlyList<RatedTrack>> GetTopTracksAsync(
        ulong guildId, CancellationToken cancellationToken = default);

    /// <summary><c>/autoplay</c> (DJ). <see cref="AutoplayMode.Smart"/> seeds from requester history.</summary>
    Task<MusicResult> SetAutoplayAsync(
        MusicContext context, AutoplayMode mode, CancellationToken cancellationToken = default);
}

/// <summary>
/// A <c>/play</c> answer. When <see cref="Prompt"/> is set nothing was queued yet — the caller
/// asks the user to confirm and calls back with <c>confirmPlaylist: true</c>.
/// </summary>
public sealed record PlayOutcome(bool Success, string Message, PlaylistPrompt? Prompt = null)
{
    public static PlayOutcome Ok(string message) => new(true, message);

    public static PlayOutcome Fail(string message) => new(false, message);
}

/// <param name="Query">Echoed back so the confirm path resolves exactly the same link.</param>
public sealed record PlaylistPrompt(string Name, int TrackCount, string Query);
