using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Matching;

namespace Sonarr.Application.Chat;

/// <summary>
/// Finds a saved line off the guild's quote board worth dropping into a reply.
/// </summary>
/// <remarks>
/// docs/04 and docs/07: the chat engine "can also recall" <c>social.quote_board</c>. Keyword
/// overlap, not embeddings — the board is small, its whole appeal is the exact wording, and a
/// paraphrase match would surface a quote nobody in the room connects to what was just said.
/// <para>Attribution is the reason this is separate from <see cref="CallbackRetriever"/>: a board
/// row belongs to a member, so the tail has to name them (docs/06 counts a saved quote as data
/// the author consented to). The author arrives as a mention id and stays one — resolving it to a
/// display name would need a gateway lookup here, and Discord renders the mention anyway.</para>
/// <para>Fails open, same as every other recall: no database, no board, no tail.</para>
/// </remarks>
public sealed class QuoteBoardRecall(IQuoteRepository quotes, ILogger<QuoteBoardRecall> log)
{
    /// <summary>Words shorter than this are noise to match on, index or not.</summary>
    public const int MinWordLength = 4;

    /// <summary>
    /// How many content words of the message to search on. A long message would otherwise match
    /// most of the board on one common word and stop meaning anything.
    /// </summary>
    public const int MaxWords = 6;

    /// <summary>Candidates to fetch. The newest match wins; the rest are for the tie.</summary>
    public const int TopK = 3;

    /// <summary>Longer quotes get cut: a tail is an aside, not a transcript.</summary>
    public const int MaxQuoteChars = CallbackRetriever.MaxQuoteChars;

    /// <summary>
    /// A quote board line to hang a tail off, or null when nothing on the board relates. Never throws.
    /// </summary>
    public async Task<RecalledQuote?> RecallAsync(
        long guildId, string? text, CancellationToken ct = default)
    {
        IReadOnlyList<string> words = Keywords(text);
        if (words.Count == 0)
        {
            return null;
        }

        try
        {
            IReadOnlyList<QuoteBoard> found = await quotes
                .SearchAsync(guildId, words, TopK, ct).ConfigureAwait(false);

            return found
                .Where(q => !string.IsNullOrWhiteSpace(q.Content))
                .Select(q => new RecalledQuote(Trim(q.Content), $"<@{(ulong)q.AuthorId}>"))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Quote board lookup failed; replying without a tail.");
            return null;
        }
    }

    /// <summary>
    /// The words from a message worth matching a quote on: long enough to mean something, deduped,
    /// capped. Reuses the engine's normalizer so this sees the same tokens the matcher did.
    /// </summary>
    private static IReadOnlyList<string> Keywords(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? []
            : [.. Normalizer.Normalize(text).Tokens
                .Where(t => t.Length >= MinWordLength)
                .Distinct(StringComparer.Ordinal)
                .Take(MaxWords)];

    private static string Trim(string quote)
    {
        string clean = quote.Trim();
        return clean.Length <= MaxQuoteChars
            ? clean
            : string.Concat(clean.AsSpan(0, MaxQuoteChars - 1).TrimEnd(), "…");
    }
}
