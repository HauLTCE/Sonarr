namespace Sonarr.Domain.Music;

/// <summary>One row of "most played" (<c>/musicstats</c>).</summary>
public sealed record TrackPlayCount(string Title, string Uri, int Plays);

/// <summary>One row of "top requesters" (<c>/musicstats</c>).</summary>
public sealed record RequesterPlayCount(ulong UserId, int Plays);

/// <summary>A track with its 👍/👎 tally (<c>/toptracks</c>).</summary>
public sealed record RatedTrack(string Title, string Uri, int Likes, int Dislikes)
{
    public int Score => Likes - Dislikes;
}

/// <summary>
/// One of the user's own votes, with the guild's tally beside it — the panel's "my ratings".
/// </summary>
/// <param name="Vote">+1 or -1: what this user said.</param>
public sealed record MyRating(string Title, string Uri, short Vote, int Likes, int Dislikes, DateTimeOffset At)
{
    public int Score => Likes - Dislikes;
}

/// <summary>The <c>/musicstats</c> answer for one guild.</summary>
public sealed record MusicStats(
    int TotalPlays,
    TimeSpan ListeningTime,
    IReadOnlyList<TrackPlayCount> MostPlayed,
    IReadOnlyList<RequesterPlayCount> TopRequesters)
{
    public static MusicStats Empty { get; } = new(0, TimeSpan.Zero, [], []);
}
