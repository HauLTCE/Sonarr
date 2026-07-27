namespace Sonarr.Domain.Music;

/// <summary>
/// The numbers the music feature argues about, in one place (docs/07-commands.md#music,
/// docs/08-background-services.md). Domain constants so the service, the modules and the tests
/// all quote the same figure.
/// </summary>
public static class MusicRules
{
    /// <summary>Below this many human listeners <c>/skip</c> just skips; at or above it votes.</summary>
    public const int VoteSkipThreshold = 8;

    public const int MinVolume = 0;

    public const int MaxVolume = 150;

    public const int DefaultVolume = 100;

    /// <summary>Guard on <c>/playlist</c> names — also the DB column's practical limit.</summary>
    public const int MaxPlaylistNameLength = 60;

    /// <summary>A saved playlist is a convenience, not an archive.</summary>
    public const int MaxPlaylistTracks = 500;

    /// <summary>Rows in <c>/musicstats</c>, <c>/mytracks</c> and <c>/toptracks</c>.</summary>
    public const int StatsTop = 10;

    /// <summary>Empty channel grace period before the bot leaves (docs/08).</summary>
    public static readonly TimeSpan EmptyChannelDisconnectDelay = TimeSpan.FromSeconds(300);

    /// <summary>Snapshot cadence for <c>MusicSessionSnapshotter</c> (docs/08).</summary>
    public static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(30);

    /// <summary>Majority of listeners, minimum 2 — one person voting is not a vote.</summary>
    public static int VotesNeeded(int listeners) => Math.Max(2, (listeners / 2) + 1);
}
