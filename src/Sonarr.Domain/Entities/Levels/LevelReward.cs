namespace Sonarr.Domain.Entities.Levels;

/// <summary>levels.reward — role granted when a member reaches <see cref="Level"/>.</summary>
public class LevelReward : AuditedEntity
{
    public long GuildId { get; set; }

    public int Level { get; set; }

    public long RoleId { get; set; }
}
