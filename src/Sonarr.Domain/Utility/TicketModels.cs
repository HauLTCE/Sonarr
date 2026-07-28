using System.Globalization;
using System.Text;

namespace Sonarr.Domain.Utility;

/// <summary>Result of opening a ticket.</summary>
public sealed record TicketResult(bool Success, string Message, long TicketId = 0)
{
    public static TicketResult Ok(string message, long ticketId) => new(true, message, ticketId);

    public static TicketResult Rejected(string? reason) => new(false, reason ?? "That didn't work.");
}

/// <summary>One message in a ticket transcript. Whatever the caller can read, in order.</summary>
public sealed record TranscriptLine(DateTimeOffset At, string Author, string Content);

/// <summary>
/// Renders a ticket transcript as plain text, ready to be attached to the mod-log post
/// (docs/07-commands.md: "close button saves transcript").
/// </summary>
/// <remarks>
/// Plain text rather than JSON or HTML: the audience is a moderator scrolling back through what
/// was said, and a .txt attachment renders in Discord's own preview.
/// </remarks>
public static class TicketTranscript
{
    /// <summary>
    /// Discord attachments are capped at 8 MB on an unboosted guild; this is the practical ceiling
    /// long before that, and a ticket that ran past it has already been read by someone.
    /// </summary>
    public const int MaxLines = 1000;

    public static string Render(long ticketId, IReadOnlyList<TranscriptLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        StringBuilder text = new();
        text.Append("Ticket #").Append(ticketId.ToString(CultureInfo.InvariantCulture)).AppendLine();
        text.Append("Closed ").Append(DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)).AppendLine();
        text.Append(lines.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" message(s)");
        text.AppendLine(new string('-', 40));

        foreach (TranscriptLine line in lines.Take(MaxLines))
        {
            text.Append('[')
                .Append(line.At.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                .Append("] ")
                .Append(line.Author)
                .Append(": ")
                // Newlines inside a message would look like new messages.
                .AppendLine(line.Content.ReplaceLineEndings(" ⏎ "));
        }

        if (lines.Count > MaxLines)
        {
            text.Append("… ")
                .Append((lines.Count - MaxLines).ToString(CultureInfo.InvariantCulture))
                .AppendLine(" earlier message(s) not included");
        }

        return text.ToString();
    }
}
