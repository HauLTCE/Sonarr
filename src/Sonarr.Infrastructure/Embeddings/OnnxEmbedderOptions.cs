namespace Sonarr.Infrastructure.Embeddings;

/// <summary>
/// Where the model lives and how much CPU it may take. Defaults are the shipped
/// all-MiniLM-L6-v2 layout (docs/03-stack.md).
/// </summary>
public sealed record OnnxEmbedderOptions
{
    /// <summary>Directory holding <c>model.onnx</c> and <c>vocab.txt</c>.</summary>
    public required string ModelPath { get; init; }

    /// <summary>
    /// Intra-op threads. Two, not four: the J2900 has four cores and Lavalink wants them —
    /// measured on the dev box, 2 threads cost ~1 ms over 4 on a one-sentence input.
    /// </summary>
    public int Threads { get; init; } = 2;

    /// <summary>
    /// Token ceiling per input. BERT's own limit is 512; a Discord message that needs more
    /// than 128 word-pieces is a wall of text whose tail adds nothing to the topic vector,
    /// and truncating keeps the worst case bounded.
    /// </summary>
    public int MaxTokens { get; init; } = 128;

    public string ModelFile => Path.Combine(ModelPath, "model.onnx");

    public string VocabFile => Path.Combine(ModelPath, "vocab.txt");
}
