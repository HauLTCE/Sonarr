using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Repositories.Chat;

/// <inheritdoc cref="IIntentEmbeddingRepository"/>
public sealed class IntentEmbeddingRepository(SonarrDbContext db) : IIntentEmbeddingRepository
{
    public async Task<IReadOnlyList<IntentEmbedding>> GetAllAsync(CancellationToken ct = default)
        => await db.IntentEmbeddings.AsNoTracking().ToListAsync(ct);

    /// <remarks>
    /// One transaction: a half-synced cache would leave the matcher scoring against examples the
    /// persona no longer contains, which is worse than an empty cache (it re-embeds and recovers).
    /// </remarks>
    public async Task SyncAsync(IReadOnlyList<IntentEmbedding> current, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);

        HashSet<string> keep = [.. current.Select(e => e.ContentHash)];
        List<IntentEmbedding> existing = await db.IntentEmbeddings.ToListAsync(ct);
        Dictionary<string, IntentEmbedding> byHash = existing.ToDictionary(
            e => e.ContentHash, StringComparer.Ordinal);

        foreach (IntentEmbedding row in current)
        {
            if (byHash.TryGetValue(row.ContentHash, out IntentEmbedding? found))
            {
                // Same text, so the vector is the same by construction; only the owning intent
                // can have moved (an example copied to a different intent).
                found.IntentId = row.IntentId;
            }
            else
            {
                db.IntentEmbeddings.Add(row);
            }
        }

        db.IntentEmbeddings.RemoveRange(existing.Where(e => !keep.Contains(e.ContentHash)));
        await db.SaveChangesAsync(ct);
    }
}
