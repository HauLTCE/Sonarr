using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Domain.Abstractions;

/// <summary>One recalled episode and how close it was.</summary>
/// <param name="Similarity">Cosine similarity in 0..1; 1 is identical.</param>
public sealed record EpisodeMatch(Episode Episode, double Similarity);

/// <summary>
/// chat.episode reads that the turn write path does not cover: vector recall, the backfill
/// job, and the retention cap.
/// </summary>
public interface IEpisodeRepository
{
    /// <summary>
    /// Top-K episodes for one person by cosine distance, nearest first. Only rows that already
    /// have an embedding are considered — an un-backfilled episode is simply not recallable yet.
    /// </summary>
    Task<IReadOnlyList<EpisodeMatch>> SearchAsync(
        long guildId,
        long userId,
        float[] query,
        int limit,
        CancellationToken ct = default);

    /// <summary>Oldest episodes still missing an embedding, for the backfill job.</summary>
    Task<IReadOnlyList<Episode>> GetUnembeddedAsync(int limit, CancellationToken ct = default);

    /// <summary>Stores embeddings for rows the backfill job just computed.</summary>
    Task SetEmbeddingsAsync(
        IReadOnlyList<(long Id, float[] Vector)> embeddings,
        CancellationToken ct = default);

    /// <summary>
    /// Retention: deletes episodes beyond the newest <paramref name="keepPerUser"/> per person,
    /// except any a fact references. Returns how many rows went.
    /// </summary>
    Task<int> PruneAsync(int keepPerUser, CancellationToken ct = default);
}
