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

    /// <summary>
    /// The newest <paramref name="limit"/> episodes for one person since
    /// <paramref name="since"/>, returned oldest-first — a transcript, not a search.
    /// </summary>
    /// <remarks>
    /// Chronological rather than by similarity, because the caller (<c>sonarr chat -u</c>) is a
    /// person reading down the page instead of the engine looking something up, and because
    /// un-embedded rows have to appear: an episode is recorded on the turn it happens and embedded
    /// by a background job later, so <see cref="SearchAsync"/> cannot see the last few minutes.
    /// </remarks>
    Task<IReadOnlyList<Episode>> GetRecentAsync(
        long guildId,
        long userId,
        DateTimeOffset since,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// The newest <paramref name="limit"/> episodes across all users of a guild since
    /// <paramref name="since"/>, oldest-first — what <c>sonarr episodes</c> reads to show what
    /// she has been saying lately, with <see cref="Episode.UserId"/> for attribution.
    /// </summary>
    Task<IReadOnlyList<Episode>> GetRecentAsync(
        long guildId,
        DateTimeOffset since,
        int limit,
        CancellationToken ct = default);

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
