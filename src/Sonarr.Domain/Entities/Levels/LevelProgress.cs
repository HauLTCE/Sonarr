namespace Sonarr.Domain.Entities.Levels;

/// <summary>levels.progress — XP/level state per (guild, user).</summary>
public class LevelProgress : AuditedEntity
{
    public long GuildId { get; set; }

    public long UserId { get; set; }

    public long Xp { get; set; }

    public int Level { get; set; }

    /// <summary>Backstop for the 60s XP cooldown; the hot path uses Redis rl:xp.</summary>
    public DateTimeOffset? LastMessageXpAt { get; set; }

    /// <summary>Only accrued while others are present and unmuted (anti-AFK).</summary>
    public long VoiceSeconds { get; set; }

    public int StreakDays { get; set; }

    public DateOnly? StreakLastDay { get; set; }

    /// <summary>Day the once-per-day first-message bonus was last granted.</summary>
    public DateOnly? FirstMsgBonusDay { get; set; }
}
