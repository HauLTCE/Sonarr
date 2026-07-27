namespace Sonarr.Domain.Levels;

/// <summary>
/// Everything <c>/level</c> and <c>/rank</c> show for one member. The bar is rendered from
/// <see cref="Fraction"/> at the controller — no image library on the way (the J2900 has no
/// spare CPU for GDI/SkiaSharp, docs/03-stack.md).
/// </summary>
/// <param name="Rank"><c>1</c>-based position in the guild by XP; <c>0</c> when the member has no row yet.</param>
public sealed record LevelCard(
    ulong GuildId,
    ulong UserId,
    long Xp,
    int Level,
    long XpIntoLevel,
    long XpForLevel,
    int Rank,
    int StreakDays,
    long VoiceSeconds,
    DateTimeOffset? LastMessageXpAt)
{
    /// <summary>0.0-1.0 progress through the current level.</summary>
    public double Fraction => XpForLevel <= 0 ? 0d : Math.Clamp((double)XpIntoLevel / XpForLevel, 0d, 1d);

    /// <summary>XP still needed for the next level.</summary>
    public long XpToNextLevel => Math.Max(0L, XpForLevel - XpIntoLevel);

    /// <summary>A member who has never earned anything — <c>/rank</c> says so instead of showing zeros.</summary>
    public static LevelCard Empty(ulong guildId, ulong userId)
    {
        LevelCurve.Progress(0, LevelCurve.FirstLevel, out var into, out var span);
        return new LevelCard(guildId, userId, 0, LevelCurve.FirstLevel, into, span, 0, 0, 0, null);
    }
}

/// <summary>One row of <c>/leaderboard</c>.</summary>
public sealed record LeaderboardEntry(int Rank, ulong UserId, long Xp, int Level);

/// <summary>
/// A page of <c>/leaderboard</c>. Season pages come from <c>levels.season_result</c> (frozen
/// standings), the live page from <c>levels.progress</c>.
/// </summary>
public sealed record LeaderboardPage(
    IReadOnlyList<LeaderboardEntry> Entries,
    int Page,
    int PageCount,
    long? SeasonId,
    string Title);

/// <summary><c>/compare</c>: two cards plus the gap, so the controller does no arithmetic.</summary>
public sealed record LevelComparison(LevelCard Left, LevelCard Right)
{
    public long XpGap => Math.Abs(Left.Xp - Right.Xp);

    /// <summary>Whoever is ahead on XP. Equal XP resolves to the left card.</summary>
    public ulong LeaderId => Left.Xp >= Right.Xp ? Left.UserId : Right.UserId;
}

/// <summary>
/// <c>/userstats</c>: the activity summary. Message count and last-active come from
/// <c>core.member</c> (batched out of Redis by the ActivityFlusher), the rest from levels.
/// </summary>
public sealed record MemberStats(
    ulong GuildId,
    ulong UserId,
    long MessageCount,
    DateTimeOffset? FirstSeenAt,
    DateTimeOffset? LastActiveAt,
    LevelCard Level);
