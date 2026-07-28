using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Repositories.Chat;

/// <inheritdoc cref="IEpisodeRepository"/>
public sealed class EpisodeRepository(SonarrDbContext db) : IEpisodeRepository
{
    /// <summary>Backfill/prune batch size — big enough to be quick, small enough not to hold a long transaction.</summary>
    public const int BatchSize = 200;

    public async Task<IReadOnlyList<EpisodeMatch>> SearchAsync(
        long guildId,
        long userId,
        float[] query,
        int limit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0 || limit <= 0)
        {
            return [];
        }

        Vector vector = new(query);

        // CosineDistance is 1 - similarity, so ordering ascending is nearest-first and the
        // similarity the caller gates on comes straight back out of it.
        var rows = await db.Episodes
            .AsNoTracking()
            .Where(e => e.GuildId == guildId && e.UserId == userId && e.Embedding != null)
            .Select(e => new { Episode = e, Distance = e.Embedding!.CosineDistance(vector) })
            .OrderBy(r => r.Distance)
            .Take(limit)
            .ToListAsync(ct);

        return [.. rows.Select(r => new EpisodeMatch(r.Episode, 1 - r.Distance))];
    }

    public async Task<IReadOnlyList<Episode>> GetUnembeddedAsync(
        int limit, CancellationToken ct = default)
        => await db.Episodes
            .AsNoTracking()
            .Where(e => e.Embedding == null)
            .OrderBy(e => e.Id)
            .Take(limit <= 0 ? BatchSize : limit)
            .ToListAsync(ct);

    public async Task SetEmbeddingsAsync(
        IReadOnlyList<(long Id, float[] Vector)> embeddings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        if (embeddings.Count == 0)
        {
            return;
        }

        Dictionary<long, float[]> byId = embeddings.ToDictionary(e => e.Id, e => e.Vector);
        List<Episode> rows = await db.Episodes.Where(e => byId.Keys.Contains(e.Id)).ToListAsync(ct);
        foreach (Episode row in rows)
        {
            row.Embedding = new Vector(byId[row.Id]);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <remarks>
    /// docs/04 wants "newest N per user + anything referenced by a fact". There is no
    /// episode↔fact foreign key, but there is a join that means the same thing: a fact records the
    /// <c>learned_at_turn</c> it was heard on, and an episode records the <c>turn</c> it happened
    /// on, so the episode where a fact came from is (guild, user, turn) — that is the row whose
    /// deletion would leave her holding a fact she cannot say where she got.
    /// <para>Active facts only: a superseded fact is kept for trajectory (docs/04), not for recall,
    /// and letting a dead fact pin an episode forever would make the cap unenforceable for anyone
    /// who changes their mind a lot.</para>
    /// </remarks>
    public async Task<int> PruneAsync(int keepPerUser, CancellationToken ct = default)
    {
        if (keepPerUser <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keepPerUser), "must keep at least one episode");
        }

        // Rank per person, newest first, and delete everything past the cap that no fact points at.
        // One statement: pulling ids into memory first would mean a round trip per user.
        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM chat.episode WHERE id IN (
                SELECT id FROM (
                    SELECT id, guild_id, user_id, turn, row_number() OVER (
                        PARTITION BY guild_id, user_id ORDER BY happened_at DESC, id DESC) AS rn
                    FROM chat.episode
                ) ranked WHERE rn > {keepPerUser}
                  AND NOT EXISTS (
                    SELECT 1 FROM chat.fact f
                    WHERE f.active
                      AND f.guild_id = ranked.guild_id
                      AND f.user_id = ranked.user_id
                      AND f.learned_at_turn = ranked.turn)
            )
            """,
            ct);
    }
}
