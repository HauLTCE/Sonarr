using System.Globalization;
using System.Text;
using System.Text.Json;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Writes the review sheet: every real message, the engine's reply, the intent it fired, and the
/// legacy reply beside it.
/// </summary>
/// <remarks>
/// "The answer must be perfect and in-context" is not a regex, so this test asserts almost
/// nothing — it produces the artefact a reviewer (a person, or a sub-agent giving a second
/// opinion) reads. Keeping it as a test rather than a script means it uses the same engine
/// wiring the rest of the suite does, so the reply on the sheet is the reply under test.
/// <para>The sheet lands in <c>training-data/</c>, which is gitignored: it contains real user
/// messages, so it is exactly as private as the corpus it came from.</para>
/// </remarks>
public class CorpusReviewSheetTests
{
    private static PersonaGraph Graph => SeedPersona.Graph;

    /// <summary>Plain text, one block per message — the form a reviewer can read top to bottom.</summary>
    public const string SheetFile = "single-turn.txt";

    /// <summary>The same rows as JSON, for a reviewer that would rather parse than read.</summary>
    public const string JsonFile = "single-turn.jsonl";

    [CorpusFact]
    public void WriteTheSheet()
    {
        ChatEngine engine = new(Graph);

        StringBuilder text = new();
        text.AppendLine("Review sheet: her reply to every real message in the corpus.");
        text.AppendLine();
        text.AppendLine("Each block is one message on a FRESH conversation — no history, no mood");
        text.AppendLine("carried in, turn 1 every time. Judge three things and nothing else:");
        text.AppendLine("  1. does the reply answer the message");
        text.AppendLine("  2. does it stay in her voice");
        text.AppendLine("  3. would a person reading it be confused");
        text.AppendLine();
        text.AppendLine("intent=(fallback) means nothing matched and an activity's fallback pool");
        text.AppendLine("answered — a reply that was not about the message. That is a finding on its");
        text.AppendLine("own, but not automatically a bad reply: some messages have no answer.");
        text.AppendLine();
        text.AppendLine("legacy= is the OLD Python bot's reply, for context only. The engine was");
        text.AppendLine("rewritten; that line is evidence of what was asked, not a target.");
        text.AppendLine();
        text.AppendLine(new string('=', 78));
        text.AppendLine();

        // No BOM: `Encoding.UTF8` emits one, and a JSONL file whose first line starts with a BOM
        // fails to parse in every reader that does not ask for utf-8-sig.
        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);

        string run = Environment.GetEnvironmentVariable("ELAINE_REVIEW_RUN")
            ?? DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string runDirectory = Path.Combine(Corpus.Directory, "checks", run);
        Directory.CreateDirectory(runDirectory);

        string jsonPath = Path.Combine(runDirectory, JsonFile);
        using StreamWriter json = new(
            new FileStream(jsonPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), utf8);

        int index = 0;
        foreach (CorpusRow row in Corpus.Rows)
        {
            index++;
            ConversationState fresh =
                ConversationState.Fresh(Graph.Root, CorpusReplyTests.Salt(row.In));
            TurnResult result = engine.Turn(fresh, new TurnInput { Text = row.In });

            string intent = result.IntentId ?? "(fallback)";

            text.AppendLine($"[{index}] said x{row.Seen}   intent={intent}   mode={result.ModeId}");
            text.AppendLine($"  them:  {row.In}");
            text.AppendLine($"  her:   {result.Text ?? "(silence)"}");
            if (result.Reaction is not null)
            {
                text.AppendLine($"  react: {result.Reaction}");
            }

            if (row.LegacyOut is not null)
            {
                text.AppendLine($"  legacy: {row.LegacyOut}");
            }

            text.AppendLine();

            json.WriteLine(JsonSerializer.Serialize(new
            {
                n = index,
                said = row.In,
                seen = row.Seen,
                intent,
                sideEffects = result.SideEffectIntentIds,
                mode = result.ModeId,
                reply = result.Text,
                reaction = result.Reaction,
                legacy = row.LegacyOut,
                legacyCategory = row.LegacyCategory,
            }));
        }

        string sheetPath = Path.Combine(runDirectory, SheetFile);
        using (StreamWriter sheet = new(
            new FileStream(sheetPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), utf8))
        {
            sheet.Write(text);
        }

        Console.WriteLine($"wrote {index} rows to {sheetPath} and {jsonPath}");

        Assert.Equal(Corpus.Rows.Count, index);
    }

    [CorpusSessionFact]
    public void WriteTheSessionSheet()
    {
        string run = Environment.GetEnvironmentVariable("ELAINE_REVIEW_RUN")
            ?? DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string runDirectory = Path.Combine(Corpus.Directory, "checks", run);
        Directory.CreateDirectory(runDirectory);

        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
        string path = Path.Combine(runDirectory, "sessions.jsonl");
        using StreamWriter output = new(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), utf8);

        int rows = 0;
        foreach (CorpusSession session in Corpus.Sessions)
        {
            ChatEngine engine = new(Graph);
            ConversationState state =
                ConversationState.Fresh(Graph.Root, CorpusReplyTests.Salt(session.Session));

            for (int i = 0; i < session.Turns.Count; i++)
            {
                CorpusTurn turn = session.Turns[i];
                TurnResult result = engine.Turn(state, new TurnInput { Text = turn.In });
                state = result.State;
                rows++;

                output.WriteLine(JsonSerializer.Serialize(new
                {
                    session = session.Session,
                    turn = i + 1,
                    said = turn.In,
                    intent = result.IntentId ?? "(fallback)",
                    sideEffects = result.SideEffectIntentIds,
                    mode = result.ModeId,
                    reply = result.Text,
                }));
            }
        }

        Console.WriteLine($"wrote {rows} session turns to {path}");
        Assert.Equal(Corpus.Sessions.Sum(s => s.Turns.Count), rows);
    }
}
