namespace Sonarr.Elaine.Conversation;

/// <summary>
/// A line worth bringing up again, plus who said it when it was not her.
/// </summary>
/// <remarks>
/// The adapter finds these; the engine only decides whether to use one. <paramref name="Author"/>
/// is what makes the difference visible in the reply: her own episode is quoted as hers ("like i
/// said before"), a line off the quote board belongs to a member and has to be attributed, or she
/// is putting words in someone's mouth. Same data, two authored pools — see
/// <see cref="ReplyComposer.CallbackPool"/> and <see cref="ReplyComposer.QuoteBoardPool"/>.
/// </remarks>
/// <param name="Quote">The quoted text, already trimmed to tail length by the adapter.</param>
/// <param name="Author">
/// How to name the speaker, already rendered by the adapter, or null when the line is hers.
/// </param>
public sealed record RecalledQuote(string Quote, string? Author);
