using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// chat.intent_embedding — the semantic matcher's example vectors, cached so a restart does
/// not re-run the model over every authored phrasing.
/// </summary>
/// <remarks>
/// The cache key is a hash of the example text (docs/04), so an edited persona re-embeds only
/// the lines that changed and a renamed intent costs nothing.
/// </remarks>
public interface IIntentEmbeddingRepository
{
    /// <summary>Every cached row. Small (one per authored example), read once at warmup.</summary>
    Task<IReadOnlyList<IntentEmbedding>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Inserts or updates rows, then deletes any whose hash is no longer authored.</summary>
    Task SyncAsync(
        IReadOnlyList<IntentEmbedding> current,
        CancellationToken ct = default);
}
