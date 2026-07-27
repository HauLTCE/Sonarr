namespace Sonarr.Domain.Abstractions;

/// <summary>
/// Turns text into a unit-length vector. One ONNX session behind it (docs/08: lazy-loaded
/// singleton), used by the chat pipeline's semantic tier, <c>/opinion</c> and the episode
/// backfill job.
/// </summary>
/// <remarks>
/// Non-generative: the model only ever produces numbers, never words. That is the line
/// docs/README draws — embeddings are allowed, text generation is not.
/// </remarks>
public interface ITextEmbedder
{
    /// <summary>Vector width. 384 for the shipped model, and what <c>vector(384)</c> expects.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds one string, L2-normalized so cosine similarity is a plain dot product.
    /// Returns an all-zero vector for text with nothing in it.
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose: this is CPU work, a few milliseconds, and wrapping it in a Task
    /// would only add a thread hop. Callers on a request path should batch instead.
    /// </remarks>
    float[] Embed(string? text);

    /// <summary>
    /// Embeds several strings in one go. Same result as calling <see cref="Embed"/> per item;
    /// exists so the backfill job pays the session overhead once per batch.
    /// </summary>
    IReadOnlyList<float[]> EmbedBatch(IReadOnlyList<string> texts);
}
