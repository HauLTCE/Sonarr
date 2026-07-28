using Sonarr.Domain.Entities.Social;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>social.quote_board</c> — the lines members deliberately saved (docs/04).
/// </summary>
public interface IQuoteRepository
{
    /// <summary>
    /// Quotes from this guild containing any of <paramref name="words"/>, newest first.
    /// </summary>
    /// <remarks>
    /// Guild-scoped with no exceptions: a quote board is one server's inside joke and must never
    /// surface in another. Empty <paramref name="words"/> returns nothing rather than everything —
    /// "no search terms" is not a request for a random quote.
    /// </remarks>
    Task<IReadOnlyList<QuoteBoard>> SearchAsync(
        long guildId, IReadOnlyList<string> words, int limit, CancellationToken ct = default);
}
