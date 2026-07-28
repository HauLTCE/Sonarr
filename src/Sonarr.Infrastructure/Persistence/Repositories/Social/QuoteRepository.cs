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

    public async Task<long> SaveAsync(QuoteBoard quote, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(quote);

        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return quote.QuoteId;
    }

    public async Task<QuoteBoard?> RandomAsync(
        long guildId, long? authorId = null, CancellationToken ct = default)
        // ORDER BY random() reads the whole matching set, which is exactly what the
        // (guild_id, author_id) index makes cheap here — a board is tens of rows. The alternatives
        // (count-then-offset, or a random id probe) are two round trips or skew towards gaps.
        // ponytail: past a few thousand rows per guild, switch to TABLESAMPLE or a random-offset read.
        => await db.Quotes
            .AsNoTracking()
            .Where(q => q.GuildId == guildId && (authorId == null || q.AuthorId == authorId))
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<bool> DeleteAsync(
        long guildId, long quoteId, long requestedBy, CancellationToken ct = default)
        // Ownership lives in the WHERE clause: a guessed id belonging to somebody else matches
        // nothing rather than deleting their row.
        => await db.Quotes
            .Where(q => q.QuoteId == quoteId
                && q.GuildId == guildId
                && (q.SavedBy == requestedBy || q.AuthorId == requestedBy))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false) > 0;
}
