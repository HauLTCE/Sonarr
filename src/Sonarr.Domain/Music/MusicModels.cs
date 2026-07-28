namespace Sonarr.Domain.Music;

/// <summary>How the queue repeats (<c>/loop track|queue|off</c>).</summary>
public enum LoopMode
{
    Off,
    Track,
    Queue,
}

/// <summary>The filter presets <c>/filter</c> offers. <see cref="Clear"/> removes all of them.</summary>
public enum MusicFilter
{
    Clear,
    Bassboost,
    Nightcore,
    Karaoke,
    Speed,
}

/// <summary>Radio mode (<c>/autoplay</c>). <see cref="Smart"/> seeds from the requester's history.</summary>
public enum AutoplayMode
{
    Off,
    On,
    Smart,
}

/// <summary>One track, as everything outside the Lavalink client sees it.</summary>
/// <param name="Title">Display title.</param>
/// <param name="Author">Uploader/artist.</param>
/// <param name="Uri">Canonical URL — the identity used for ratings and history.</param>
/// <param name="Identifier">What we hand back to Lavalink to re-resolve this track (<c>/restore-queue</c>).</param>
/// <param name="DurationMs">Length; 0 for a live stream.</param>
/// <param name="RequesterId">Who asked for it. 0 when the bot picked it (autoplay).</param>
/// <param name="ArtworkUri">Thumbnail for the now-playing embed, when the source has one.</param>
public sealed record TrackInfo(
    string Title,
    string Author,
    string Uri,
    string Identifier,
    long DurationMs,
    ulong RequesterId,
    string? ArtworkUri = null)
{
    public bool IsStream => DurationMs <= 0;

    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);
}

/// <summary>
/// Who is asking and from where. The controller fills this in; nothing below it sees a Discord type.
/// </summary>
/// <param name="GuildId">Guild the command ran in.</param>
/// <param name="UserId">Caller.</param>
/// <param name="VoiceChannelId">The caller's voice channel, or <c>null</c> when they are not in one.</param>
/// <param name="TextChannelId">Where now-playing updates belong.</param>
/// <param name="IsDj">Resolved by the controller from the <c>dj_role</c> config (or Manage Server).</param>
public sealed record MusicContext(
    ulong GuildId,
    ulong UserId,
    ulong? VoiceChannelId,
    ulong TextChannelId,
    bool IsDj);

/// <summary>
/// The standard answer: did it work, and the one sentence the user reads. Controllers do not
/// compose music prose — the service owns the wording so Discord and the panel agree.
/// </summary>
public sealed record MusicResult(bool Success, string Message)
{
    public static MusicResult Ok(string message) => new(true, message);

    public static MusicResult Fail(string message) => new(false, message);
}

/// <summary>What a <c>/play</c> query turned into.</summary>
public enum TrackResolutionKind
{
    /// <summary>Nothing matched.</summary>
    Empty,

    /// <summary>The source errored (Lavalink said so).</summary>
    Failed,

    /// <summary>A single track — a direct link, or the best search hit.</summary>
    Track,

    /// <summary>A playlist link: needs confirmation before it lands in the queue.</summary>
    Playlist,
}

/// <param name="Kind">Which shape came back.</param>
/// <param name="Tracks">Resolved tracks, in playlist order.</param>
/// <param name="PlaylistName">Set for <see cref="TrackResolutionKind.Playlist"/>.</param>
public sealed record TrackResolution(
    TrackResolutionKind Kind,
    IReadOnlyList<TrackInfo> Tracks,
    string? PlaylistName = null)
{
    public static TrackResolution Empty { get; } = new(TrackResolutionKind.Empty, []);

    public static TrackResolution Failed { get; } = new(TrackResolutionKind.Failed, []);
}

/// <summary>Live player state, read straight off the Lavalink client by the gateway.</summary>
public sealed record PlayerSnapshot(
    ulong GuildId,
    ulong VoiceChannelId,
    TrackInfo? Current,
    TimeSpan Position,
    bool IsPaused,
    int Volume,
    LoopMode Loop,
    bool FairQueue,
    AutoplayMode Autoplay,
    IReadOnlyList<TrackInfo> Queue,
    IReadOnlyList<TrackInfo> History);

/// <summary>One page of <c>/queue</c>.</summary>
public sealed record QueuePage(
    int Page,
    int TotalPages,
    int TotalTracks,
    TrackInfo? Current,
    IReadOnlyList<QueueEntry> Entries,
    TimeSpan Remaining)
{
    /// <summary>Tracks per page — one Discord embed's worth.</summary>
    public const int PageSize = 10;
}

/// <param name="Position">1-based position as the user types it into <c>/remove</c>.</param>
public sealed record QueueEntry(int Position, TrackInfo Track);

/// <summary>Everything the now-playing embed renders.</summary>
/// <param name="TracksUntilYours">
/// How many tracks before the viewer's next request, <c>null</c> when they have none queued.
/// </param>
public sealed record NowPlayingView(
    TrackInfo Track,
    TimeSpan Position,
    bool IsPaused,
    int Volume,
    LoopMode Loop,
    int QueueLength,
    int? TracksUntilYours,
    int Likes,
    int Dislikes);

/// <summary>Outcome of <c>/skip</c>: either it skipped, or it is counting votes.</summary>
/// <param name="Skipped">True when the track actually changed.</param>
/// <param name="Votes">Votes gathered so far.</param>
/// <param name="Needed">Votes required (majority of listeners).</param>
public sealed record SkipOutcome(bool Skipped, int Votes, int Needed, string Message);
