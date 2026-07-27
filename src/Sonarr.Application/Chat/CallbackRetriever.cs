using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Chat;

/// <summary>
/// Finds the episode worth bringing up again: "weren't you complaining about this last month?"
/// </summary>
/// <remarks>
/// docs/10: <c>compose: fragment + core + callback (pgvector top-K episode, relevance-gated)</c>.
/// The gate is the whole feature. An ungated callback is a bot quoting itself at random, which
/// reads as broken; a gated one is the thing that makes her feel like she remembers you.
/// <para>Everything here fails open — no model, no vectors, no database, no answer: the reply
/// simply has no tail, which is the common case anyway.</para>
/// </remarks>
public sealed class CallbackRetriever(
    ITextEmbedder embedder,
    IEpisodeRepository episodes,
    ILogger<CallbackRetriever> log)
{
    /// <summary>The authored pool the tail is drawn from (persona/pools/memory.yaml).</summary>
    public const string Pool = "callback_tail";

    /// <summary>Slot the recalled quote is substituted into.</summary>
    public const string Slot = "callback";

    /// <summary>
    /// Cosine similarity a memory needs before she will bring it up. Tuned against the measured
    /// spread of this model: paraphrases of the same complaint land near 0.55+, while unrelated
    /// small talk sits around 0.2, so this admits "same subject" and rejects "also English".
    /// </summary>
    public const double RelevanceFloor = 0.55;

    /// <summary>How many neighbours to consider. The best one either clears the floor or none do.</summary>
    public const int TopK = 3;

    /// <summary>
    /// A memory has to be this many turns old to count as a memory. Quoting something from two
    /// turns ago is not recall, it is repeating herself.
    /// </summary>
    public const int MinAgeTurns = 8;

    /// <summary>Longer quotes get cut: a tail is an aside, not a transcript.</summary>
    public const int MaxQuoteChars = 120;

    /// <summary>
    /// The quote to build a callback tail from, or null when nothing relevant and old enough
    /// exists. Never throws.
    /// </summary>
    /// <param name="currentTurn">This turn's logical clock, for the age gate.</param>
    public async Task<string?> QuoteAsync(
        long guildId,
        long userId,
        string? text,
        long currentTurn,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            float[] query = embedder.Embed(text);
            if (query.Length == 0)
            {
                return null;
            }

            IReadOnlyList<EpisodeMatch> matches = await episodes
                .SearchAsync(guildId, userId, query, TopK, ct).ConfigureAwait(false);

            return matches
                .Where(m => m.Similarity >= RelevanceFloor
                    && currentTurn - m.Episode.Turn >= MinAgeTurns
                    && !string.IsNullOrWhiteSpace(m.Episode.Quote))
                .Select(m => Trim(m.Episode.Quote))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Callback lookup failed; replying without a tail.");
            return null;
        }
    }

    private static string Trim(string quote)
    {
        string clean = quote.Trim();
        return clean.Length <= MaxQuoteChars
            ? clean
            : string.Concat(clean.AsSpan(0, MaxQuoteChars - 1).TrimEnd(), "…");
    }
}
