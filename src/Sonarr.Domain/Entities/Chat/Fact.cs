namespace Sonarr.Domain.Entities.Chat;

/// <summary>
/// chat.fact — a first-class remembered fact with confidence. Repeat mentions
/// reinforce <see cref="Confidence"/>, which drives hedging ("didn't you say…?").
/// </summary>
public class Fact : AuditedEntity
{
    public long Id { get; set; }

    public long GuildId { get; set; }

    public long UserId { get; set; }

    /// <summary>job, pet, favorite_food, birthday, …</summary>
    public string Predicate { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>real (float4) in [0,1]; reinforced on repeat mention.</summary>
    public float Confidence { get; set; }

    /// <summary>Person logical clock value at learn time.</summary>
    public long LearnedAtTurn { get; set; }

    public DateTimeOffset LearnedAt { get; set; }

    /// <summary>Soft delete: superseded facts stay for trajectory.</summary>
    public bool Active { get; set; } = true;
}
