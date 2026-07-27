namespace Sonarr.Domain.Levels;

/// <summary>Why a message earned no XP — the handler logs it at debug, users never see it.</summary>
public enum XpSkipReason
{
    /// <summary>XP was granted.</summary>
    None,

    /// <summary>Inside the 60 s window (or Redis is down, which fails closed).</summary>
    Cooldown,

    /// <summary>The channel's weight is 0 — an admin muted XP there.</summary>
    ChannelExcluded,
}

/// <summary>
/// The outcome of an XP grant: enough for the caller to announce a level-up and hand out a role
/// without asking anything else. Domain type — the handler maps it onto Discord.
/// </summary>
/// <param name="XpGained">0 when <paramref name="Skipped"/> is set.</param>
/// <param name="TotalXp">Cumulative XP after the grant.</param>
/// <param name="PreviousLevel">Level before the grant.</param>
/// <param name="Level">Level after the grant.</param>
/// <param name="StreakDays">Streak length after the day-touch.</param>
/// <param name="StreakExtended">The day rolled over on this message.</param>
/// <param name="FirstMessageBonus">The once-a-day bonus was included in <paramref name="XpGained"/>.</param>
/// <param name="RewardRoleIds">Reward roles the member has now earned (every level up to <paramref name="Level"/>).</param>
public sealed record XpAward(
    long XpGained,
    long TotalXp,
    int PreviousLevel,
    int Level,
    int StreakDays,
    bool StreakExtended,
    bool FirstMessageBonus,
    IReadOnlyList<ulong> RewardRoleIds,
    XpSkipReason Skipped = XpSkipReason.None)
{
    /// <summary>Nothing happened: cooldown, excluded channel, or a bot/DM the caller filtered.</summary>
    public static XpAward Skip(XpSkipReason reason) => new(0, 0, 0, 0, 0, false, false, [], reason);

    public bool Granted => Skipped == XpSkipReason.None;

    public bool LeveledUp => Granted && Level > PreviousLevel;
}
