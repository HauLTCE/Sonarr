using System.Text;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Every real message in the private corpus, put to the current engine.
/// </summary>
/// <remarks>
/// The floor, not the bar. These are the checks that need no judgement — she answers, the answer
/// is rendered, and the same input twice gives the same words. Whether the answer is *right* is
/// not decidable here, which is what <c>CorpusReviewSheet</c> and the sub-agent pass are for.
/// <para>Nothing asserts against <see cref="CorpusRow.LegacyOut"/>. The old replies came out of a
/// different engine; they say what was asked, not what the answer should be.</para>
/// </remarks>
public class CorpusReplyTests
{
    private static PersonaGraph Graph => SeedPersona.Graph;

    /// <summary>
    /// One message on a fresh conversation, so a row's verdict never depends on the row before it.
    /// </summary>
    /// <remarks>
    /// Single-shot on purpose: multi-turn is <see cref="CorpusSessionTests"/>, which replays the
    /// real sessions rather than the corpus's dedupe order. A fixed salt per row keyed off the
    /// text keeps two different messages from drawing the same line every time while staying
    /// reproducible run to run.
    /// </remarks>
    private static TurnResult Ask(string text)
    {
        ChatEngine engine = new(Graph);
        ConversationState fresh = ConversationState.Fresh(Graph.Root, Salt(text));
        return engine.Turn(fresh, new TurnInput { Text = text });
    }

    /// <summary>A stable per-message salt: FNV-1a over the text, so it is the same on every run.</summary>
    internal static ulong Salt(string text)
    {
        ulong hash = 14695981039346656037;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash = (hash ^ b) * 1099511628211;
        }

        return hash;
    }

    [CorpusFact]
    public void SheAnswersEveryRealMessageAndNeverThrows()
    {
        List<string> broken = [];

        foreach (CorpusRow row in Corpus.Rows)
        {
            try
            {
                TurnResult result = Ask(row.In);

                // Silence is a legitimate answer in general, but not to a message addressed to her:
                // every corpus row was said to her face, so every one of them deserves words.
                if (result.IsSilent)
                {
                    broken.Add($"silent: '{row.Label}'");
                }
            }
            catch (Exception ex)
            {
                broken.Add($"{ex.GetType().Name}: '{row.Label}' — {ex.Message}");
            }
        }

        Assert.Empty(broken);
    }

    [CorpusFact]
    public void NoReplyIsEmptyAndNoTemplateEscapesUnrendered()
    {
        List<string> bad = [];

        foreach (CorpusRow row in Corpus.Rows)
        {
            string text = Ask(row.In).Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                bad.Add($"blank reply to '{row.Label}'");
                continue;
            }

            // A leftover brace means a slot was authored that the composer could not fill — the
            // reply reads as a bug report to whoever is talking to her.
            if (text.Contains('{', StringComparison.Ordinal)
                || text.Contains('}', StringComparison.Ordinal))
            {
                bad.Add($"unrendered slot in reply to '{row.Label}': {text}");
            }
        }

        Assert.Empty(bad);
    }

    [CorpusFact]
    public void TheSameMessageTwiceGivesTheSameWords()
    {
        // The engine has no clock and no ambient RNG, so this must hold exactly rather than
        // usually. It is also what makes a review sheet worth reviewing: the reply a sub-agent
        // judged is the reply the next run produces.
        List<string> drifted = [];

        foreach (CorpusRow row in Corpus.Rows)
        {
            TurnResult first = Ask(row.In);
            TurnResult second = Ask(row.In);

            if (first.Text != second.Text || first.IntentId != second.IntentId)
            {
                drifted.Add($"'{row.Label}': '{first.Text}' then '{second.Text}'");
            }
        }

        Assert.Empty(drifted);
    }

    /// <summary>
    /// Prints what the corpus reaches and what it falls through, and asserts nothing about the
    /// numbers.
    /// </summary>
    /// <remarks>
    /// The fallthrough rate is the finding, and it is a finding whether it is good or bad — a
    /// message that draws the activity's fallback pool got a reply that was not *about* it.
    /// Asserting it at zero would turn "cover the gap" into "stuff the persona until the test
    /// stops complaining", so the number is reported and the judgement stays with a person.
    /// </remarks>
    [CorpusFact]
    public void CoverageAndFallthroughAreReported()
    {
        Dictionary<string, int> byIntent = new(StringComparer.Ordinal);
        List<CorpusRow> fellThrough = [];

        foreach (CorpusRow row in Corpus.Rows)
        {
            TurnResult result = Ask(row.In);

            if (result.IntentId is null)
            {
                fellThrough.Add(row);
                continue;
            }

            byIntent[result.IntentId] = byIntent.GetValueOrDefault(result.IntentId) + 1;
        }

        int intents = Graph.Intents.Count;
        double rate = 100.0 * fellThrough.Count / Corpus.Rows.Count;

        StringBuilder report = new();
        report.AppendLine(
            $"corpus: {Corpus.Rows.Count} distinct messages, {intents} intents in the persona");
        report.AppendLine(
            $"reached: {byIntent.Count}/{intents} intents; fell through: {fellThrough.Count} "
            + $"({rate:F1}%)");

        foreach ((string intent, int count) in byIntent.OrderByDescending(p => p.Value))
        {
            report.AppendLine($"  {count,4}  {intent}");
        }

        report.AppendLine("  -- never reached by the corpus:");
        foreach (string intent in Graph.Intents
            .Select(i => i.Id)
            .Where(id => !byIntent.ContainsKey(id))
            .Order(StringComparer.Ordinal))
        {
            report.AppendLine($"        {intent}");
        }

        report.AppendLine("  -- fell through to a fallback pool:");
        foreach (CorpusRow row in fellThrough.OrderByDescending(r => r.Seen))
        {
            report.AppendLine($"     x{row.Seen,-3} {row.Label}");
        }

        // The only channel a passing xunit test has. Deliberate: this is a report, and a report
        // that only appears on failure is a report nobody reads.
        Console.WriteLine(report.ToString());

        Assert.NotEmpty(byIntent);
    }
}
