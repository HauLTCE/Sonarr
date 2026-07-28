using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;

namespace Sonarr.Infrastructure.Persistence.Repositories.Social;

/// <inheritdoc cref="IQuoteRepository"/>
public sealed class QuoteRepository(SonarrDbContext db) : IQuoteRepository
{
    public async Task<IReadOnlyList<QuoteBoard>> SearchAsync(
        long guildId, IReadOnlyList<string> words, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Count == 0 || limit <= 0)
        {
            return [];
        }

        // ILIKE ANY over the board rather than a vector search: a board is tens of rows per guild,
        // not thousands of episodes, and the caller already reduced the message to content words —
        // so this is one scan of a small table instead of an embedding per turn. Npgsql folds the
        // Any() into a single `content ILIKE ANY (@patterns)`.
        // ponytail: fine to a few thousand rows per guild; past that add a pg_trgm GIN index on
        // content before reaching for embeddings.
        string[] patterns = [.. words.Select(w => $"%{w}%")];

        return await db.Quotes
            .AsNoTracking()
            .Where(q => q.GuildId == guildId
                && patterns.Any(p => EF.Functions.ILike(q.Content, p)))
            .OrderByDescending(q => q.QuoteId)
            .Take(limit)
            .ToListAsync(ct);
    }
}
