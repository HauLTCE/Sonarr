using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Pgvector;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Chat;

/// <summary>
/// The authored example phrasings, embedded once and kept in memory for the semantic tier.
/// </summary>
/// <remarks>
/// docs/04: the vectors live in <c>chat.intent_embedding</c>, keyed by a hash of the example
/// text, "rebuilt only when the persona file changes". So a restart costs one SELECT, and a
/// persona edit costs the model only the lines that actually changed.
/// <para>Fail-open throughout: no model, no database, no examples — she falls back to the
/// lexical tier alone, which is exactly how she behaved before this existed.</para>
/// </remarks>
public sealed class SemanticIntentIndex(
    PersonaHolder persona,
    ITextEmbedder embedder,
    ILogger<SemanticIntentIndex> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<IntentExampleVector> _vectors = [];
    private string _builtFor = "";

    /// <summary>Example vectors currently loaded. Empty until <see cref="WarmAsync"/> succeeds.</summary>
    public IReadOnlyList<IntentExampleVector> Vectors => _vectors;

    /// <summary>
    /// Brings the index in line with the live persona: reuses cached vectors, embeds what is
    /// new, drops what is gone. Safe to call repeatedly — a no-op when nothing changed.
    /// </summary>
    /// <param name="repository">
    /// Passed in rather than injected: this index is a singleton (the vectors outlive any request)
    /// and the repository is scoped, so the caller owns the scope for the duration of the warm.
    /// </param>
    public async Task WarmAsync(IIntentEmbeddingRepository repository, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        List<(string IntentId, string Example)> authored = Authored(persona.Current);
        string fingerprint = Fingerprint(authored);
        if (fingerprint == _builtFor)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (fingerprint == _builtFor)
            {
                return;
            }

            _vectors = await BuildAsync(repository, authored, ct).ConfigureAwait(false);
            _builtFor = fingerprint;
            log.LogInformation(
                "Semantic tier ready: {Vectors} example vectors over {Intents} intents.",
                _vectors.Count,
                _vectors.Select(v => v.IntentId).Distinct(StringComparer.Ordinal).Count());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The lexical tier is the product; the semantic tier is a rescue. A missing model
            // file or an unreachable database must not stop her from answering.
            log.LogWarning(ex, "Semantic tier unavailable, lexical matching only.");
            _vectors = [];
            _builtFor = fingerprint;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// A matcher for one turn. The message is embedded lazily, inside
    /// <see cref="ISemanticMatcher.Rescue"/> — which the engine only calls on a lexical miss,
    /// so the common path never touches the model.
    /// </summary>
    public ISemanticMatcher MatcherFor(PersonaRoot root, string? text)
    {
        ArgumentNullException.ThrowIfNull(root);

        return _vectors.Count == 0 || string.IsNullOrWhiteSpace(text)
            ? NullSemanticMatcher.Instance
            : new LazyMatcher(this, embedder, log, root, text);
    }

    private async Task<IReadOnlyList<IntentExampleVector>> BuildAsync(
        IIntentEmbeddingRepository repository,
        List<(string IntentId, string Example)> authored,
        CancellationToken ct)
    {
        if (authored.Count == 0)
        {
            return [];
        }

        IReadOnlyList<IntentEmbedding> cached = await repository.GetAllAsync(ct).ConfigureAwait(false);
        Dictionary<string, float[]> byHash = new(StringComparer.Ordinal);
        foreach (IntentEmbedding row in cached)
        {
            if (row.Embedding is { } vector && vector.Memory.Length == embedder.Dimensions)
            {
                byHash[row.ContentHash] = vector.ToArray();
            }
        }

        List<IntentEmbedding> rows = [];
        List<IntentExampleVector> result = [];
        List<(string Hash, string IntentId, string Example)> missing = [];

        foreach ((string intentId, string example) in authored)
        {
            string hash = Hash(example);
            if (byHash.TryGetValue(hash, out float[]? vector))
            {
                result.Add(new IntentExampleVector(intentId, vector));
                rows.Add(Row(hash, intentId, example, vector));
            }
            else
            {
                missing.Add((hash, intentId, example));
            }
        }

        if (missing.Count > 0)
        {
            log.LogInformation("Embedding {Count} new persona example(s).", missing.Count);
            IReadOnlyList<float[]> fresh = embedder.EmbedBatch([.. missing.Select(m => m.Example)]);
            for (int i = 0; i < missing.Count; i++)
            {
                result.Add(new IntentExampleVector(missing[i].IntentId, fresh[i]));
                rows.Add(Row(missing[i].Hash, missing[i].IntentId, missing[i].Example, fresh[i]));
            }

            await repository.SyncAsync(rows, ct).ConfigureAwait(false);
        }
        else if (rows.Count != cached.Count)
        {
            // Nothing new to embed, but examples were removed: prune so the cache does not keep
            // rescuing to an intent the persona no longer describes that way.
            await repository.SyncAsync(rows, ct).ConfigureAwait(false);
        }

        return result;
    }

    private static IntentEmbedding Row(string hash, string intentId, string example, float[] vector)
        => new()
        {
            ContentHash = hash,
            IntentId = intentId,
            Example = example,
            Embedding = new Vector(vector),
        };

    private static List<(string IntentId, string Example)> Authored(PersonaGraph graph)
        => [.. graph.Intents
            .SelectMany(i => i.SemanticExamples.Select(e => (i.Id, Example: e.Trim())))
            .Where(p => p.Example.Length > 0)];

    /// <summary>
    /// Cheap "did the persona's examples change" key. Not a security boundary — it only has to
    /// notice an edit, and the per-example hashes below are what the cache is actually keyed on.
    /// </summary>
    private static string Fingerprint(List<(string IntentId, string Example)> authored)
        => authored.Count.ToString(CultureInfo.InvariantCulture)
            + ":" + Hash(string.Join('\n', authored.Select(a => a.IntentId + '=' + a.Example)));

    private static string Hash(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// Defers the message embedding to the moment the engine asks for a rescue. Single-turn,
    /// single-threaded by construction: one of these is created per turn.
    /// </summary>
    private sealed class LazyMatcher(
        SemanticIntentIndex index,
        ITextEmbedder embedder,
        ILogger log,
        PersonaRoot root,
        string text) : ISemanticMatcher
    {
        private ISemanticMatcher? _inner;

        public MatchCandidate? Rescue(
            Normalized input, MatchContext context, IReadOnlyList<IntentDef> eligible)
        {
            if (_inner is null)
            {
                try
                {
                    _inner = new EmbeddingSemanticMatcher(root, embedder.Embed(text), index.Vectors);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Embedding failed this turn, no semantic rescue.");
                    _inner = NullSemanticMatcher.Instance;
                }
            }

            return _inner.Rescue(input, context, eligible);
        }
    }
}
