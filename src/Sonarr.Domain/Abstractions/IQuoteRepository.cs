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

    /// <summary>
    /// Saves one quote and returns its id.
    /// </summary>
    /// <remarks>
    /// Both ids are kept on purpose: <c>AuthorId</c> is whose words these are and
    /// <c>SavedBy</c> is who pressed save, and they are usually different people. docs/06 counts a
    /// saved quote as consented content, so the record of who did the saving is the thing that makes
    /// a later "who put this on the board" answerable.
    /// </remarks>
    Task<long> SaveAsync(QuoteBoard quote, CancellationToken ct = default);

    /// <summary>
    /// One random quote from this guild's board, or <c>null</c> if the board is empty. Optionally
    /// restricted to one author.
    /// </summary>
    Task<QuoteBoard?> RandomAsync(long guildId, long? authorId = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes one quote, but only if it is this guild's and <paramref name="requestedBy"/> either
    /// saved it or said it. Returns whether a row went.
    /// </summary>
    /// <remarks>
    /// The ownership test is in the query rather than in a read-then-delete, so a caller cannot
    /// remove somebody else's row by passing an id they guessed. Mods have channel-level tools for
    /// anything beyond that; docs/06 gives the person quoted the right to take their own words down.
    /// </remarks>
    Task<bool> DeleteAsync(long guildId, long quoteId, long requestedBy, CancellationToken ct = default);
}
