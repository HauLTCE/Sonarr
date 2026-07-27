namespace Sonarr.Domain.Levels;

/// <summary>
/// How much XP anything is worth, and the anti-AFK rule for voice. Flat numbers on purpose:
/// the old bot rolled 15-25 per message, which made "why did I get less than him" unanswerable
/// and the curve untestable.
/// </summary>
public static class XpRules
{
    /// <summary>Per message, once per <see cref="Cooldown"/> window.</summary>
    public const int MessageXp = 15;

    /// <summary>Added once a day, on the first message that day (the streak-touch day).</summary>
    public const int FirstMessageBonusXp = 25;

    /// <summary>Per full minute of qualifying voice time.</summary>
    public const int VoiceXpPerMinute = 5;

    /// <summary>Humans that must be in the channel before voice time counts (docs/08).</summary>
    public const int VoiceMinimumHumans = 2;

    /// <summary>The XP-per-message window, mirrored by <c>rl:xp:{guild}:{user}</c>.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>How often <c>VoiceXpAccrual</c> runs, and therefore the credit granted per tick.</summary>
    public static readonly TimeSpan VoiceTick = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The anti-AFK rule: voice time counts only while somebody else is there to talk to and the
    /// user can actually be heard. Server-mute counts as muted — it is still silence.
    /// </summary>
    /// <param name="humanCount">Non-bot members in the voice channel, including this user.</param>
    /// <param name="muted">Self- or server-muted.</param>
    public static bool VoiceTimeCounts(int humanCount, bool muted)
        => humanCount >= VoiceMinimumHumans && !muted;

    /// <summary>Voice XP for a stretch of qualifying time, rounded down to whole minutes.</summary>
    public static long VoiceXpFor(TimeSpan qualifyingTime)
        => qualifyingTime <= TimeSpan.Zero ? 0L : (long)qualifyingTime.TotalMinutes * VoiceXpPerMinute;

    /// <summary>
    /// Applies the guild's event multiplier and the channel weight to a base amount.
    /// Weight is a percentage so the whole config surface stays integer-typed.
    /// </summary>
    public static long Scale(long baseXp, int multiplier, int channelWeightPercent)
    {
        var scaled = baseXp * Math.Max(multiplier, 1) * Math.Max(channelWeightPercent, 0) / 100L;
        return Math.Max(scaled, 0L);
    }
}
