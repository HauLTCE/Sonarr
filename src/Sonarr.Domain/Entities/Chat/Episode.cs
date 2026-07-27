using Pgvector;

namespace Sonarr.Domain.Entities.Chat;

/// <summary>
/// chat.episode — episodic memory, semantically searchable via pgvector (ivfflat).
/// Capped per user by the retention service: newest N + anything a fact references.
/// </summary>
public class Episode : AuditedEntity
{
    public long Id { get; set; }

    public long GuildId { get; set; }

    public long UserId { get; set; }

    public string Quote { get; set; } = string.Empty;

    public string SentimentTag { get; set; } = string.Empty;

    /// <summary>Person logical clock value when this happened.</summary>
    public long Turn { get; set; }

    /// <summary>vector(384); null until the backfill job embeds it.</summary>
    public Vector? Embedding { get; set; }

    public DateTimeOffset HappenedAt { get; set; }
}
