namespace Sonarr.Domain.Entities.Chat;

/// <summary>
/// chat.stance_agreement — whose side you took on a topic, and she remembers.
/// </summary>
public class StanceAgreement : AuditedEntity
{
    public long GuildId { get; set; }

    public long UserId { get; set; }

    public string Topic { get; set; } = string.Empty;

    public bool Agreed { get; set; }

    public Stance? Stance { get; set; }
}
