namespace Sonarr.Domain.Abstractions;

/// <summary>
/// Everything the transparency page shows and the two delete buttons remove (docs/06).
/// </summary>
/// <remarks>
/// One repository rather than a read through every slice's own: the my-data page is defined by
/// docs/06's table, so the query set and the deletion set are the same list read twice. Splitting
/// it across nine repositories is how the page and the doc drift apart.
/// </remarks>
public interface IUserDataRepository
{
    /// <summary>The whole payload for one user, across every guild — the panel's "My data".</summary>
    Task<UserDataExport> ExportAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// Wipes what the chat engine holds: person, facts, episodes, relationship events, stance
    /// agreements. She genuinely forgets — tier resets because the row is gone.
    /// </summary>
    Task<UserDeletion> DeleteChatMemoryAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// The above plus levels progress, music history/ratings/prefs, saved quotes, reminders,
    /// capsules, RSVPs, panel sessions and the member row.
    /// </summary>
    /// <remarks>
    /// <c>mod.case</c> rows are deliberately <em>not</em> deleted (docs/06: audit integrity, a
    /// guild-owner decision), and neither are aggregated stats, which are not keyed to a person.
    /// </remarks>
    Task<UserDeletion> DeleteEverythingAsync(long userId, CancellationToken ct = default);
}

/// <summary>Row counts per area, so the panel can say what actually went.</summary>
public sealed record UserDeletion(IReadOnlyDictionary<string, int> Deleted)
{
    public int Total => Deleted.Values.Sum();
}

/// <summary>
/// The export payload. Shapes are flat and named after docs/06's table, so a reader can line the
/// JSON up against the doc row by row.
/// </summary>
public sealed record UserDataExport(
    long UserId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<UserDataMembership> Memberships,
    IReadOnlyList<UserDataLevels> Levels,
    IReadOnlyList<UserDataFact> Facts,
    UserDataCounts Counts,
    IReadOnlyList<UserDataSession> Sessions);

public sealed record UserDataMembership(
    long GuildId,
    string GuildName,
    string Username,
    string DisplayName,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastActiveAt,
    long MessageCount,
    string? Timezone,
    DateOnly? Birthday);

public sealed record UserDataLevels(long GuildId, long Xp, int Level, long VoiceMinutes, int StreakDays);

/// <param name="Predicate">What she thinks she knows ("job", "pet").</param>
/// <param name="Value">The value she stored — only ever something the user told her directly.</param>
public sealed record UserDataFact(
    long GuildId, string Predicate, string Value, double Confidence, DateTimeOffset LearnedAt);

/// <summary>
/// Counts for the bulky areas. The page links to them rather than inlining thousands of rows, and
/// docs/06 promises visibility, not a single enormous JSON blob.
/// </summary>
public sealed record UserDataCounts(
    int ChatEpisodes,
    int RelationshipEvents,
    int TracksPlayed,
    int TrackRatings,
    int SavedQuotes,
    int Reminders,
    int Capsules,
    int ModCases);

public sealed record UserDataSession(
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string? UserAgent);
