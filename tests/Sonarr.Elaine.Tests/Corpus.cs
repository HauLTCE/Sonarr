using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// One real user message, as <c>scripts/extract-chat-turns.py corpus</c> wrote it.
/// </summary>
/// <param name="In">The message, mentions stripped, casing as first typed.</param>
/// <param name="Seen">How many times this message was said across the whole journal.</param>
/// <param name="LegacyCategory">
/// What the Python bot's classifier decided, or null when that format did not record it.
/// </param>
/// <param name="LegacyFired">The newer format's <c>fired=</c> field, or null.</param>
/// <param name="LegacyOut">
/// The Python bot's reply. Reference only, never an assertion target: the persona was migrated
/// but the engine was rewritten, so matching this would pin the old bot's behaviour instead of
/// testing the new one's. It is evidence of what was asked.
/// </param>
public sealed record CorpusRow(
    [property: JsonPropertyName("in")] string In,
    [property: JsonPropertyName("seen")] int Seen,
    [property: JsonPropertyName("legacy_category")] string? LegacyCategory,
    [property: JsonPropertyName("legacy_fired")] string? LegacyFired,
    [property: JsonPropertyName("legacy_out")] string? LegacyOut)
{
    /// <summary>Short single-line form for a failure message: enough to find the row, no more.</summary>
    public string Label => In.Length <= 60 ? In : In[..57] + "...";
}

/// <summary>
/// The private corpus of real messages, read off disk if it is there.
/// </summary>
/// <remarks>
/// <c>training-data/</c> is gitignored because it holds real user data, so this file exists on
/// the maintainer's machine and on nobody else's. Every test that reads it is marked
/// <see cref="CorpusFactAttribute"/>, which skips rather than fails when the corpus is absent —
/// a suite that red-fails on a clean checkout is a broken CI, not a guard. The regression guard
/// that does run everywhere is the committed fixture beside it.
/// </remarks>
internal static class Corpus
{
    /// <summary>Where the extractor writes it, relative to the repo root.</summary>
    private const string RelativePath = "training-data/corpus.jsonl";

    private static readonly Lazy<IReadOnlyList<CorpusRow>> LazyRows = new(Load);

    /// <summary>The corpus, or empty when it is not on this machine.</summary>
    public static IReadOnlyList<CorpusRow> Rows => LazyRows.Value;

    public static bool Available => Rows.Count > 0;

    /// <summary>Absolute path to <c>training-data/</c>, whether or not the corpus is in it.</summary>
    public static string Directory =>
        Path.Combine(RepoRoot() ?? AppContext.BaseDirectory, "training-data");

    private static IReadOnlyList<CorpusRow> Load()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return [];
        }

        string path = Path.Combine(root, RelativePath);
        if (!File.Exists(path))
        {
            return [];
        }

        List<CorpusRow> rows = [];
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // A malformed corpus is a bug in the extractor, so it throws rather than skipping the
            // row: silently reading 400 of 438 messages would look exactly like a pass.
            CorpusRow row = JsonSerializer.Deserialize<CorpusRow>(line)
                ?? throw new InvalidDataException($"null corpus row in {path}: {line}");

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Walks up from the test binary looking for the repo root. Anchored on <c>persona/</c> rather
    /// than <c>training-data/</c> — the latter is the thing that may be missing.
    /// </summary>
    private static string? RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.Directory.Exists(Path.Combine(dir.FullName, "persona")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when the private corpus is absent.
/// </summary>
/// <remarks>
/// xunit 2.x has no <c>Assert.Skip</c>, and a runtime early-return would report green while
/// checking nothing. Deciding in the attribute means the runner says "skipped" out loud, with the
/// reason, which is the honest signal on a machine without the data.
/// </remarks>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (!Corpus.Available)
        {
            Skip = $"no private corpus at {Corpus.Directory} — see scripts/extract-chat-turns.py";
        }
    }
}
