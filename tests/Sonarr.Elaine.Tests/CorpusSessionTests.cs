using System.Text;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The real conversations, replayed through one state each.
/// </summary>
/// <remarks>
/// <see cref="CorpusReplyTests"/> puts every distinct message to a fresh state, which is the wrong
/// shape for half the engine: registers only drift, activities only nest, and topics only carry
/// across turns. These sessions are the actual sequences people typed — 30 and 41 turns deep in
/// places — so they exercise the paths a single-shot suite structurally cannot reach.
/// <para>Still no assertion against what she said. The checks here are about the replay surviving
/// real input: no throw at any depth, no blank turn, and the same session twice giving the same
/// transcript. What she *should* have said is a judgement call, and it lives in the review sheet.
/// </para>
/// </remarks>
public class CorpusSessionTests
{
    private static PersonaGraph Graph => SeedPersona.Graph;

    /// <summary>Replays one session and returns every reply, in order.</summary>
    /// <remarks>
    /// Salted off the session pseudonym rather than off the text, so two people who said the same
    /// thing do not walk the same line sequence — the same reason the adapter salts per person.
    /// </remarks>
    private static List<TurnResult> Replay(CorpusSession session)
    {
        ChatEngine engine = new(Graph);
        ConversationState state =
            ConversationState.Fresh(Graph.Root, CorpusReplyTests.Salt(session.Session));

        List<TurnResult> replies = [];
        foreach (CorpusTurn turn in session.Turns)
        {
            TurnResult result = engine.Turn(state, new TurnInput { Text = turn.In });
            state = result.State;
            replies.Add(result);
        }

        return replies;
    }

    [CorpusSessionFact]
    public void EveryRealSessionReplaysWithoutThrowing()
    {
        List<string> broken = [];

        foreach (CorpusSession session in Corpus.Sessions)
        {
            try
            {
                List<TurnResult> replies = Replay(session);

                // The turn counter is the one thing a caller can be sure of: it is monotonic per
                // person, and an off-by-one here would break the RNG seed and every decay step
                // that reads it.
                for (int i = 0; i < replies.Count; i++)
                {
                    if (replies[i].State.Turn != i + 1)
                    {
                        broken.Add(
                            $"{session.Session} turn {i + 1}: counter is {replies[i].State.Turn}");
                    }
                }
            }
            catch (Exception ex)
            {
                broken.Add($"{session.Session}: {ex.GetType().Name} — {ex.Message}");
            }
        }

        Assert.Empty(broken);
    }

    [CorpusSessionFact]
    public void NoTurnDeepInASessionGoesBlankOrLeavesASlotUnrendered()
    {
        List<string> bad = [];

        foreach (CorpusSession session in Corpus.Sessions)
        {
            List<TurnResult> replies = Replay(session);

            for (int i = 0; i < replies.Count; i++)
            {
                TurnResult reply = replies[i];
                string where = $"{session.Session} turn {i + 1} ('{Short(session.Turns[i].In)}')";

                // Silence is not in itself a bug the way it is on turn 1 — she can go quiet on
                // someone mid-conversation — but empty words where words were produced is.
                if (!reply.IsSilent && string.IsNullOrWhiteSpace(reply.Text))
                {
                    bad.Add($"blank but not silent: {where}");
                    continue;
                }

                if (reply.Text is { } text
                    && (text.Contains('{', StringComparison.Ordinal)
                        || text.Contains('}', StringComparison.Ordinal)))
                {
                    bad.Add($"unrendered slot: {where} → {text}");
                }
            }
        }

        Assert.Empty(bad);
    }

    [CorpusSessionFact]
    public void ReplayingASessionTwiceGivesTheSameTranscript()
    {
        // Determinism at depth, which is stronger than the single-turn version: it also says the
        // state threaded between turns is a pure function of what came before. Without this the
        // review sheet's multi-turn half would be judging one sample of many.
        List<string> drifted = [];

        foreach (CorpusSession session in Corpus.Sessions)
        {
            List<TurnResult> first = Replay(session);
            List<TurnResult> second = Replay(session);

            for (int i = 0; i < first.Count; i++)
            {
                if (first[i].Text != second[i].Text || first[i].IntentId != second[i].IntentId)
                {
                    drifted.Add(
                        $"{session.Session} turn {i + 1}: '{first[i].Text}' then '{second[i].Text}'");
                }
            }
        }

        Assert.Empty(drifted);
    }

    /// <summary>
    /// Prints what the sessions do to her over time, and asserts nothing about the numbers.
    /// </summary>
    /// <remarks>
    /// Mood drift, activity nesting and trust movement are the whole point of carrying state, and
    /// none of them has a right answer a test can hold. A session that ends in the mode it started
    /// in after 41 turns is worth a look; so is one that ends SEETHING after four. Reported, not
    /// asserted, for the same reason the fallthrough rate is.
    /// <para>Read the end mode against the fallthrough column, not on its own. A turn that matches
    /// nothing reaches <c>Fallback</c>, which applies no affect — while decay runs on every turn
    /// either way. So a 30-turn session with 24 fallthroughs moved her mood six times against
    /// thirty steps of decay, and landing back on the baseline mode is the arithmetic rather than a
    /// finding about her mood. Every session here ending NEUTRAL is the coverage gap showing up in
    /// a second place.</para>
    /// </remarks>
    [CorpusSessionFact]
    public void SessionDriftIsReported()
    {
        StringBuilder report = new();
        report.AppendLine(
            $"sessions: {Corpus.Sessions.Count}, "
            + $"{Corpus.Sessions.Sum(s => s.Turns.Count)} turns total");

        foreach (CorpusSession session in Corpus.Sessions.OrderByDescending(s => s.Turns.Count))
        {
            List<TurnResult> replies = Replay(session);
            ConversationState end = replies[^1].State;

            int fellThrough = replies.Count(r => r.IntentId is null);
            string mode = ModeSelector.Select(Graph.Root, end.Registers).Id;
            string activities = string.Join(">", end.Activities.Layers);

            report.AppendLine(
                $"  {session.Session}  {replies.Count,3} turns  "
                + $"fallthrough {fellThrough,3}/{replies.Count,-3}  "
                + $"end mode {mode,-9} activities {activities}");

            // The slots she managed to learn are the clearest signal that the conversation went
            // somewhere: a 30-turn session that taught her nothing probably never matched.
            if (!end.Slots.IsEmpty || end.AssignedNickname is not null)
            {
                report.AppendLine(
                    $"        learned: {string.Join(", ", end.Slots.Select(p => $"{p.Key}={p.Value}"))}"
                    + (end.AssignedNickname is null ? "" : $" nickname={end.AssignedNickname}"));
            }
        }

        Console.WriteLine(report.ToString());

        Assert.NotEmpty(Corpus.Sessions);
    }

    private static string Short(string text) => text.Length <= 40 ? text : text[..37] + "...";
}
