using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Infrastructure.Embeddings;

/// <summary>
/// <see cref="ITextEmbedder"/> on ONNX Runtime + all-MiniLM-L6-v2 (384-dim, mean-pooled).
/// </summary>
/// <remarks>
/// docs/08: "lazy-loaded singleton". Lazy matters twice over — the model is ~90 MB of RSS the
/// bot should not pay for until someone actually says something off-script, and a missing model
/// file must not stop the bot from booting. Registered as a singleton; the ONNX session is
/// thread-safe for concurrent <c>Run</c>, the tokenizer is stateless, so no locking here.
/// <para>Measured on the dev box (i7, 2 intra-op threads): 4.3 ms for a one-sentence input,
/// inside the docs/03 10–50 ms budget with room for the J2900 being several times slower.</para>
/// </remarks>
public sealed class OnnxTextEmbedder : ITextEmbedder, IDisposable
{
    /// <summary>all-MiniLM-L6-v2's hidden size. Also what <c>vector(384)</c> in Postgres expects.</summary>
    public const int Dim = 384;

    private readonly OnnxEmbedderOptions _options;
    private readonly Lazy<Model> _model;

    public OnnxTextEmbedder(OnnxEmbedderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _model = new Lazy<Model>(() => Load(options), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int Dimensions => Dim;

    /// <summary>True once the model has actually been loaded — for /status, not for logic.</summary>
    public bool IsLoaded => _model.IsValueCreated;

    /// <summary>Model files are present. Checked by the self-test probe so a missing model is red, not a crash.</summary>
    public bool IsAvailable => File.Exists(_options.ModelFile) && File.Exists(_options.VocabFile);

    public float[] Embed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[Dim];
        }

        return EmbedBatch([text])[0];
    }

    public IReadOnlyList<float[]> EmbedBatch(IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return [];
        }

        Model model = _model.Value;

        // One row per text, padded to the longest — the mask keeps padding out of the mean, so
        // batching cannot change a single item's vector.
        List<int[]> tokens = [.. texts.Select(t => Tokenize(model, t))];
        int rows = tokens.Count;
        int width = Math.Max(1, tokens.Max(t => t.Length));

        DenseTensor<long> ids = new([rows, width]);
        DenseTensor<long> mask = new([rows, width]);
        DenseTensor<long> types = new([rows, width]);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < tokens[r].Length; c++)
            {
                ids[r, c] = tokens[r][c];
                mask[r, c] = 1;
            }
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output = model.Session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", types),
        ]);

        Tensor<float> hidden = output[0].AsTensor<float>();
        float[][] vectors = new float[rows][];
        for (int r = 0; r < rows; r++)
        {
            vectors[r] = MeanPool(hidden, r, tokens[r].Length);
        }

        return vectors;
    }

    public void Dispose()
    {
        if (_model.IsValueCreated)
        {
            _model.Value.Session.Dispose();
        }
    }

    /// <summary>
    /// Mean of the token vectors, then L2-normalized. Mean pooling is what
    /// sentence-transformers trained this checkpoint with; CLS-only would silently
    /// degrade every similarity by using an untrained head.
    /// </summary>
    private static float[] MeanPool(Tensor<float> hidden, int row, int tokenCount)
    {
        float[] pooled = new float[Dim];
        int used = Math.Max(1, tokenCount);
        for (int t = 0; t < used; t++)
        {
            for (int d = 0; d < Dim; d++)
            {
                pooled[d] += hidden[row, t, d];
            }
        }

        double norm = 0;
        for (int d = 0; d < Dim; d++)
        {
            pooled[d] /= used;
            norm += pooled[d] * pooled[d];
        }

        norm = Math.Sqrt(norm);
        if (norm > 0)
        {
            for (int d = 0; d < Dim; d++)
            {
                pooled[d] = (float)(pooled[d] / norm);
            }
        }

        return pooled;
    }

    private int[] Tokenize(Model model, string text)
    {
        IReadOnlyList<int> encoded = model.Tokenizer.EncodeToIds(text ?? string.Empty);
        return encoded.Count <= _options.MaxTokens
            ? [.. encoded]
            : [.. encoded.Take(_options.MaxTokens)];
    }

    private static Model Load(OnnxEmbedderOptions options)
    {
        if (!File.Exists(options.ModelFile) || !File.Exists(options.VocabFile))
        {
            throw new FileNotFoundException(
                $"Embedding model not found at '{options.ModelPath}'. "
                + "Run scripts/fetch-model.sh, or leave the semantic tier off.",
                options.ModelFile);
        }

        SessionOptions session = new()
        {
            IntraOpNumThreads = options.Threads,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        return new Model(
            new InferenceSession(options.ModelFile, session),
            BertTokenizer.Create(options.VocabFile));
    }

    private sealed record Model(InferenceSession Session, BertTokenizer Tokenizer);
}
