using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Which authored pool a recalled quote lands in. The engine never searches for a quote — it only
/// decides whether to use the one the adapter offers, and whose voice it belongs to.
/// </summary>
/// <remarks>
/// Attribution is the whole point of the split. Every <c>callback_tail</c> line claims the quote
/// as hers ("i already told you"), which is right for her own episode and a lie for a line off
/// <c>social.quote_board</c> — that one belongs to a member, so it draws from
/// <c>quote_board_tail</c>, where every line names them.
/// </remarks>
public class QuoteRecallTests
{
    private static PersonaGraph Graph => SeedPersona.Graph;

    private const string Quote = "the microwave is a portal";

    private const string Author = "<@777>";

    /// <summary>
    /// The reply on the first salt whose seeded draw actually takes the tail — the odds are 1-in-2,
    /// so a fixed salt would test the coin and not the pool.
    /// </summary>
    private static string ReplyWithTail(RecalledQuote quote)
    {
        foreach (ulong salt in Enumerable.Range(1, 40).Select(i => (ulong)i))
        {
            TurnResult result = new ChatEngine(Graph).Turn(
                ConversationState.Fresh(Graph.Root, salt),
                new TurnInput { Text = "hello", Callback = quote });

            if (result.Text is { } text && text.Contains(Quote, StringComparison.Ordinal))
            {
                return text;
            }
        }

        throw new InvalidOperationException("no salt in 40 produced a callback tail");
    }

    [Fact]
    public void A_quote_off_the_board_is_credited_to_whoever_said_it()
    {
        string reply = ReplyWithTail(new RecalledQuote(Quote, Author));

        Assert.Contains(Author, reply, StringComparison.Ordinal);
    }

    [Fact]
    public void She_never_claims_a_members_line_as_her_own()
    {
        // The failure this guards: reusing callback_tail for the board, so a saved line comes back
        // as "i already told you: the microwave is a portal".
        string reply = ReplyWithTail(new RecalledQuote(Quote, Author));

        Assert.Contains(
            Graph.Pools[ReplyComposer.QuoteBoardPool].Lines
                .Select(line => line
                    .Replace("{$quote}", Quote, StringComparison.Ordinal)
                    .Replace("{$who}", Author, StringComparison.Ordinal)),
            rendered => reply.EndsWith(rendered, StringComparison.Ordinal));
    }

    [Fact]
    public void Her_own_memory_still_comes_back_as_hers()
    {
        string reply = ReplyWithTail(new RecalledQuote(Quote, null));

        // No author, no attribution: the line is hers and callback_tail says so.
        Assert.DoesNotContain("<@", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_board_line_names_the_author_and_the_quote()
    {
        // If one line forgot {$who}, that line would be the misattribution the split exists to
        // prevent — and it would only show up in production, on that one draw.
        foreach (string line in Graph.Pools[ReplyComposer.QuoteBoardPool].Lines)
        {
            Assert.Contains("{$who}", line, StringComparison.Ordinal);
            Assert.Contains("{$quote}", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_quote_means_no_tail()
    {
        TurnResult result = new ChatEngine(Graph).Turn(
            ConversationState.Fresh(Graph.Root, 7),
            new TurnInput { Text = "hello", Callback = new RecalledQuote("   ", Author) });

        Assert.DoesNotContain(Author, result.Text ?? string.Empty, StringComparison.Ordinal);
    }
}
