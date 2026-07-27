using Pgvector;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// Deterministic stand-in for the ONNX embedder: a hashed bag-of-words vector, unit length.
/// Not semantic, but stable and cheap — same text gives the same vector, different text gives
/// a different one, which is all the index and the retriever's rules need.
/// </summary>
/// <remarks>
/// 64 buckets, not 8: with a handful of buckets two unrelated sentences land on the same one and
/// look similar, which would make a relevance-gate test pass or fail on a hash collision.
/// <para>Its own FNV-1a rather than <c>string.GetHashCode</c>: that one is seeded per process, so
/// which words collide changed on every run and the relevance-gate tests failed at random.</para>
/// </remarks>
internal sealed class FakeTextEmbedder(int dimensions = 64) : ITextEmbedder
{
    public int Dimensions { get; } = dimensions;

    /// <summary>How many texts have been embedded, so a test can prove the cache saved work.</summary>
    public int Embedded { get; private set; }

    /// <summary>Next Embed throws, standing in for a corrupt or missing model file.</summary>
    public bool Fail { get; set; }

    public float[] Embed(string? text)
    {
        if (Fail)
        {
            throw new InvalidOperationException("model unavailable");
        }

        Embedded++;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[Dimensions];
        }

        float[] v = new float[Dimensions];
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            v[Bucket(word) % Dimensions] += 1f;
        }

        double length = Math.Sqrt(v.Sum(x => (double)x * x));
        if (length == 0)
        {
            return v;
        }

        for (int i = 0; i < v.Length; i++)
        {
            v[i] = (float)(v[i] / length);
        }

        return v;
    }

    public IReadOnlyList<float[]> EmbedBatch(IReadOnlyList<string> texts)
        => [.. texts.Select(Embed)];

    /// <summary>FNV-1a, so the same word lands in the same bucket in every process.</summary>
    private static int Bucket(string word)
    {
        uint hash = 2166136261;
        foreach (char c in word)
        {
            hash = (hash ^ c) * 16777619;
        }

        return (int)(hash & int.MaxValue);
    }
}

/// <summary>In-memory chat.intent_embedding. Counts syncs so a test can see the prune happen.</summary>
internal sealed class FakeIntentEmbeddingRepository : IIntentEmbeddingRepository
{
    private List<IntentEmbedding> _rows = [];

    public int Syncs { get; private set; }

    public bool Fail { get; set; }

    public int Count => _rows.Count;

    public Task<IReadOnlyList<IntentEmbedding>> GetAllAsync(CancellationToken ct = default)
        => Fail
            ? throw new InvalidOperationException("database unreachable")
            : Task.FromResult<IReadOnlyList<IntentEmbedding>>([.. _rows]);

    public Task SyncAsync(IReadOnlyList<IntentEmbedding> current, CancellationToken ct = default)
    {
        Syncs++;
        _rows = [.. current];
        return Task.CompletedTask;
    }
}

/// <summary>In-memory chat.episode with a real cosine search over whatever was seeded.</summary>
internal sealed class FakeEpisodeRepository : IEpisodeRepository
{
    private readonly List<Episode> _episodes = [];

    public bool Fail { get; set; }

    public Episode Seed(long guildId, long userId, string quote, long turn, float[]? vector = null)
    {
        Episode row = new()
        {
            Id = _episodes.Count + 1,
            GuildId = guildId,
            UserId = userId,
            Quote = quote,
            SentimentTag = string.Empty,
            Turn = turn,
            HappenedAt = Build.Now,
            Embedding = vector is null ? null : new Vector(vector),
        };
        _episodes.Add(row);
        return row;
    }

    public Task<IReadOnlyList<EpisodeMatch>> SearchAsync(
        long guildId, long userId, float[] query, int limit, CancellationToken ct = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("database unreachable");
        }

        return Task.FromResult<IReadOnlyList<EpisodeMatch>>(
            [.. _episodes
                .Where(e => e.GuildId == guildId && e.UserId == userId && e.Embedding is not null)
                .Select(e => new EpisodeMatch(e, Cosine(query, e.Embedding!.Memory.Span)))
                .OrderByDescending(m => m.Similarity)
                .Take(limit)]);
    }

    public Task<IReadOnlyList<Episode>> GetUnembeddedAsync(int limit, CancellationToken ct = default)
        => Fail
            ? throw new InvalidOperationException("database unreachable")
            : Task.FromResult<IReadOnlyList<Episode>>(
                [.. _episodes.Where(e => e.Embedding is null).OrderBy(e => e.Id).Take(limit)]);

    public Task SetEmbeddingsAsync(
        IReadOnlyList<(long Id, float[] Vector)> embeddings, CancellationToken ct = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("database unreachable");
        }

        foreach ((long id, float[] vector) in embeddings)
        {
            Episode? row = _episodes.Find(e => e.Id == id);
            row?.Embedding = new Vector(vector);
        }

        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(int keepPerUser, CancellationToken ct = default)
    {
        int removed = 0;
        foreach (var group in _episodes.GroupBy(e => (e.GuildId, e.UserId)).ToList())
        {
            foreach (Episode stale in group.OrderByDescending(e => e.Turn).Skip(keepPerUser).ToList())
            {
                _episodes.Remove(stale);
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    private static double Cosine(float[] a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }
}
