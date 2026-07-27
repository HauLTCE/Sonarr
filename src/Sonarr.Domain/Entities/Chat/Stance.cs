namespace Sonarr.Domain.Entities.Chat;

/// <summary>chat.stance — her opinions registry, keyed by topic.</summary>
public class Stance : AuditedEntity
{
    public string Topic { get; set; } = string.Empty;

    /// <summary>The position she holds.</summary>
    public string StanceText { get; set; } = string.Empty;

    /// <summary>Persona line pool to draw phrasing from.</summary>
    public string PoolRef { get; set; } = string.Empty;

    public ICollection<StanceAgreement> Agreements { get; set; } = [];
}
