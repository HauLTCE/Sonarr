using Pgvector;

namespace Sonarr.Domain.Entities.Chat;

/// <summary>
/// chat.intent_embedding — cache for the semantic intent matcher.
/// Rebuilt only when the persona file changes; keyed by hash of the example text.
/// </summary>
public class IntentEmbedding : AuditedEntity
{
    /// <summary>SHA-256 of the example text; PK.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public string IntentId { get; set; } = string.Empty;

    public string Example { get; set; } = string.Empty;

    /// <summary>vector(384).</summary>
    public Vector? Embedding { get; set; }
}
