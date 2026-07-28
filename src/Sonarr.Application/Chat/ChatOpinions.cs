using System.Globalization;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Chat;

/// <summary>
/// What <c>/opinion</c> reads: the conversation's topic, matched against her opinion registry.
/// </summary>
/// <remarks>
/// docs/10: "<c>/opinion</c> → embedding of recent channel topic (metadata-safe: embeds only the
/// topic centroid of the last few messages, discarded after) → stance registry." The centroid is
/// a local, never persisted, never logged — the vector exists for the length of one call and the
/// message text never leaves this method.
/// <para>No turn is spent and no register moves: asking her for a take is not a conversation, the
/// same way <see cref="ChatIntrospection"/> is not.</para>
/// <para>Fails open. No model, no match, no database — she falls back to the authored
/// <see cref="FallbackPool"/>, which is a real answer and not an error message.</para>
/// </remarks>
public sealed class ChatOpinions(
    PersonaHolder persona,
    ITextEmbedder embedder,
    IPersonRepository people,
    ILogger<ChatOpinions> log)
{
    /// <summary>Her non-answer, for when the room is not talking about anything she has a take on.</summary>
    public const string FallbackPool = "neutral_opinion";

    /// <summary>Appended when she already knows which side you took on this one.</summary>
    public const string AgreedPool = "opinion_you_agreed";

    public const string DisagreedPool = "opinion_you_disagreed";

    /// <summary>
    /// Cosine floor for "the room is talking about this". Lower than
    /// <see cref="CallbackRetriever.RelevanceFloor"/> on purpose: that compares a sentence to a
    /// sentence, this compares a conversation to a two-word topic label, which scores lower for
    /// the same subject. Raise it if she starts having opinions about the wrong thing.
    /// </summary>
    public const double TopicFloor = 0.35;

    /// <summary>Messages considered. A take on the conversation, not on the channel's history.</summary>
    public const int RecentMessages = 15;

    /// <summary>Per-message cut before embedding. A wall of text is still one contribution.</summary>
    public const int MaxMessageChars = 300;

    /// <summary>
    /// Her take on what the channel is currently talking about, always in her own words.
    /// </summary>
    /// <param name="recent">
    /// Recent message texts, any order. Read by the caller (the ring buffer is metadata-only) and
    /// used for nothing but the centroid.
    /// </param>
    public async Task<string> OpinionAsync(
        long guildId,
        long userId,
        IReadOnlyList<string> recent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recent);

        PersonaGraph graph = persona.Current;
        StanceDef? topic = Match(graph, recent);
        if (topic is null)
        {
            return Draw(graph, FallbackPool, userId, Seed(FallbackPool)) ?? "no comment.";
        }

        string take = Draw(graph, topic.Pool, userId, Seed(topic.Topic))
            // A stance pointing at a pool that does not exist is an unfinished persona, not a
            // broken command — the validator already reports it.
            ?? Draw(graph, FallbackPool, userId, Seed(FallbackPool))
            ?? "no comment.";

        string? side = await SideAsync(graph, guildId, userId, topic.Topic, ct).ConfigureAwait(false);
        return side is null ? take : take + ' ' + side;
    }

    /// <summary>
    /// The reminder that you already picked a side on this one, or null if you never did.
    /// </summary>
    /// <remarks>
    /// The read half of <c>chat.stance_agreement</c> (docs/04: "she remembers whose side you
    /// took"). Only your own row is ever looked at — who else agreed with her is between her and
    /// them (docs/06).
    /// </remarks>
    private async Task<string?> SideAsync(
        PersonaGraph graph, long guildId, long userId, string topic, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<StanceAgreement> sides = await people
                .GetStanceAgreementsAsync(guildId, userId, ct).ConfigureAwait(false);

            StanceAgreement? mine = sides.FirstOrDefault(s =>
                string.Equals(s.Topic, topic, StringComparison.Ordinal));

            return mine is null
                ? null
                : Draw(graph, mine.Agreed ? AgreedPool : DisagreedPool, userId, Seed(topic));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // She still has the opinion. Whether you agreed with it is a garnish.
            log.LogWarning(ex, "Stance agreement lookup failed; answering without it.");
            return null;
        }
    }

    /// <summary>
    /// The opinion the conversation is closest to, or null when it is closest to nothing.
    /// </summary>
    /// <remarks>
    /// One <see cref="ITextEmbedder.EmbedBatch"/> for the messages and one for the topic labels,
    /// so the model session overhead is paid twice per command rather than once per string. The
    /// labels are re-embedded each call: <c>/opinion</c> is rare, the strings are two words long,
    /// and a cache would need invalidating on every persona hot-reload.
    /// <para>The centroid is the mean of the message vectors, renormalized — the direction the
    /// conversation points in. It goes out of scope when this returns, which is the whole
    /// metadata-safety claim.</para>
    /// </remarks>
    private StanceDef? Match(PersonaGraph graph, IReadOnlyList<string> recent)
    {
        string[] texts =
        [
            .. recent
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Select(t => t.Length <= MaxMessageChars ? t : t[..MaxMessageChars])
                .Take(RecentMessages),
        ];

        if (texts.Length == 0 || graph.Stances.Count == 0)
        {
            return null;
        }

        try
        {
            float[]? centroid = Centroid(embedder.EmbedBatch(texts));
            if (centroid is null)
            {
                return null;
            }

            // Topic ids are snake_case keys; the words are what the model can compare against.
            IReadOnlyList<float[]> labels = embedder.EmbedBatch(
                [.. graph.Stances.Select(s => s.Topic.Replace('_', ' '))]);

            StanceDef? best = null;
            double bestScore = TopicFloor;
            for (int i = 0; i < graph.Stances.Count && i < labels.Count; i++)
            {
                double score = Dot(centroid, labels[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = graph.Stances[i];
                }
            }

            return best;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Topic embedding unavailable; falling back to a generic take.");
            return null;
        }
    }

    /// <summary>Mean of the message vectors, unit length. Null when there is nothing to average.</summary>
    private static float[]? Centroid(IReadOnlyList<float[]> vectors)
    {
        float[]? sum = null;
        foreach (float[] vector in vectors)
        {
            if (vector.Length == 0 || vector.All(v => v == 0))
            {
                continue;
            }

            sum ??= new float[vector.Length];
            if (sum.Length != vector.Length)
            {
                continue;
            }

            for (int i = 0; i < vector.Length; i++)
            {
                sum[i] += vector[i];
            }
        }

        if (sum is null)
        {
            return null;
        }

        double length = Math.Sqrt(sum.Sum(v => (double)v * v));
        if (length == 0)
        {
            return null;
        }

        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] = (float)(sum[i] / length);
        }

        return sum;
    }

    private static double Dot(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return 0;
        }

        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    /// <summary>
    /// Line seed. Per topic, so her take on crypto is the same take every time you ask, and two
    /// topics in one channel do not draw the same index.
    /// </summary>
    private static long Seed(string topic) => (long)(StableHash.Of(topic) % int.MaxValue);

    private static string? Draw(PersonaGraph graph, string poolId, long userId, long seed) =>
        new LinePicker(graph).Pick(
            poolId,
            modeId: null,
            new TurnSeededRandom(seed, StableHash.Of(userId.ToString(CultureInfo.InvariantCulture))),
            EmptySlots);

    private static readonly IReadOnlyDictionary<string, string> EmptySlots =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
