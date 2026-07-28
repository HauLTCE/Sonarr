using System.Text;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Builds the reply text: optional mood fragment + core line + optional callback tail.
/// </summary>
/// <remarks>
/// docs/10: the old engine said one line from one pool, which is why she sounded the same
/// whether she was fond of you or furious. Composition layers a mood-coloured opener and an
/// episodic callback around the core line — each part is still an authored pool draw, so
/// nothing here generates text.
/// <para>Every part is optional except the core: if a fragment cannot render, the reply is
/// just the core line rather than nothing.</para>
/// </remarks>
/// <param name="shakySlots">
/// Slots whose remembered value she is not sure of. Their value is wrapped in an authored hedge
/// before it reaches a template, so "you're Sam" comes out as "you're Sam, i think".
/// </param>
public sealed class ReplyComposer(
    PersonaGraph persona, LinePicker picker, IReadOnlySet<string>? shakySlots = null)
{
    /// <summary>The mode-covered pool the opener is drawn from (see <c>sonarr.yaml</c>).</summary>
    public const string MoodFragmentPool = "mood_fragment";

    /// <summary>The pool the callback tail is drawn from (<c>persona/pools/memory.yaml</c>).</summary>
    public const string CallbackPool = "callback_tail";

    /// <summary>
    /// The pool a quote-board line is drawn from, when the quote is someone else's
    /// (<c>persona/pools/memory.yaml</c>).
    /// </summary>
    /// <remarks>
    /// A separate pool rather than a second source behind <see cref="CallbackPool"/>, because
    /// every line in that pool claims the quote as hers — "i already told you", "my notes say".
    /// Feeding a member's saved line through those would have her taking credit for it, and the
    /// point of the board is who said it.
    /// </remarks>
    public const string QuoteBoardPool = "quote_board_tail";

    /// <summary>Capture name the remembered quote is substituted into: <c>{$quote}</c>.</summary>
    public const string CallbackCapture = "quote";

    /// <summary>Capture name the quote's author is substituted into: <c>{$who}</c>.</summary>
    public const string AuthorCapture = "who";

    /// <summary>The pool a shaky remembered value is wrapped in (<c>persona/pools/memory.yaml</c>).</summary>
    public const string HedgePool = "fact_hedge";

    /// <summary>Capture name the shaky value is substituted into: <c>{$value}</c>.</summary>
    public const string HedgeCapture = "value";

    /// <summary>1-in-N turns carry a mood fragment. Every turn would be a verbal tic.</summary>
    private const int FragmentOdds = 3;

    /// <summary>1-in-N turns carry a callback, and only when one was offered.</summary>
    private const int CallbackOdds = 2;

    /// <summary>
    /// Whether this turn would use a callback if one were offered.
    /// </summary>
    /// <remarks>
    /// Public so the adapter can ask before paying for the lookup: finding a callback costs an
    /// embedding and a pgvector query, and half the turns would throw the answer away. The draw
    /// is seeded, so asking here returns exactly what <see cref="Compose"/> will draw later.
    /// </remarks>
    public static bool WantsCallback(IDeterministicRandom rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.Next("compose:callback", CallbackOdds) == 0;
    }

    private readonly PersonaGraph _persona = persona
        ?? throw new ArgumentNullException(nameof(persona));

    private readonly LinePicker _picker = picker ?? throw new ArgumentNullException(nameof(picker));

    private readonly IReadOnlySet<string> _shaky = shakySlots ?? NoShakySlots;

    /// <summary>
    /// Composes a reply for <paramref name="candidate"/>, or null when the pool could not
    /// produce a renderable line at all (the caller falls back to the activity's pool).
    /// </summary>
    /// <param name="callback">
    /// A quote the adapter supplies — a relevance-gated pgvector episode of her own, or a line off
    /// the guild's quote board. Null on the common path; the engine never searches for one itself.
    /// </param>
    public string? Compose(
        MatchCandidate candidate,
        ConversationState state,
        string modeId,
        IDeterministicRandom rng,
        RecalledQuote? callback = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);

        IReadOnlyDictionary<string, string> slots = Hedged(state, modeId, rng);
        string? core = _picker.Pick(candidate.Intent.Pool, modeId, rng, slots, candidate.Captures);
        if (core is null)
        {
            return null;
        }

        // The intent's own template, when present, is appended to the pool line — that is how
        // a generic pool line ("sure.") carries the specific bit ("nice to meet you, Sam").
        if (candidate.Intent.Template is { } template
            && TemplateRenderer.TryRender(
                template, slots, candidate.Captures, out string rendered))
        {
            core = Join(core, rendered);
        }

        return Wrap(core, slots, modeId, rng, callback);
    }

    /// <summary>
    /// Composes from a bare pool id — the fallback path when nothing matched, and the path
    /// side-effect acknowledgments use.
    /// </summary>
    public string? ComposeFromPool(
        string poolId,
        ConversationState state,
        string modeId,
        IDeterministicRandom rng,
        RecalledQuote? callback = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);

        IReadOnlyDictionary<string, string> slots = Hedged(state, modeId, rng);
        string? core = _picker.Pick(poolId, modeId, rng, slots);
        return core is null ? null : Wrap(core, slots, modeId, rng, callback);
    }

    /// <summary>
    /// Draws one bare authored line — no mood fragment, no callback tail. Null when the pool is
    /// absent or nothing in it renders.
    /// </summary>
    /// <remarks>
    /// For text that is appended to an already-composed reply (the tier moment), where wrapping
    /// again would stack a second opener onto the same message.
    /// </remarks>
    public string? Line(
        string poolId, ConversationState state, string modeId, IDeterministicRandom rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);
        return _picker.Pick(poolId, modeId, rng, Hedged(state, modeId, rng));
    }

    /// <summary>
    /// The render slots with every shaky value wrapped in an authored hedge.
    /// </summary>
    /// <remarks>
    /// docs/04: a fact's confidence is "reinforced on repeat mention → hedging behavior". Done at
    /// the value rather than by pairing every recall pool with an unsure variant, because the
    /// uncertainty belongs to the fact, not to the sentence — one hedge pool covers every line
    /// that ever substitutes a remembered slot, including ones authored later.
    /// <para>The hedge wraps the value in place, so word order and punctuation stay the author's:
    /// "you're Sam" becomes "you're Sam, i think" without the pool knowing hedging exists.</para>
    /// </remarks>
    private IReadOnlyDictionary<string, string> Hedged(
        ConversationState state, string modeId, IDeterministicRandom rng)
    {
        if (_shaky.Count == 0)
        {
            return state.RenderSlots;
        }

        Dictionary<string, string> slots = new(state.RenderSlots, StringComparer.Ordinal);
        foreach (string slot in _shaky)
        {
            if (!slots.TryGetValue(slot, out string? value))
            {
                continue;
            }

            // One hedge per turn, not per slot: the draw is seeded on the pool id, so a reply
            // holding two shaky facts hedges both the same way. That reads as one uncertain
            // sentence rather than a list of disclaimers.
            string? hedged = _picker.Pick(
                HedgePool,
                modeId,
                rng,
                NoSlots,
                new Dictionary<string, string>(StringComparer.Ordinal) { [HedgeCapture] = value });
            if (hedged is not null)
            {
                slots[slot] = hedged;
            }
        }

        return slots;
    }

    private static readonly IReadOnlySet<string> NoShakySlots =
        new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> NoSlots =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private string Wrap(
        string core,
        IReadOnlyDictionary<string, string> slots,
        string modeId,
        IDeterministicRandom rng,
        RecalledQuote? callback)
    {
        StringBuilder reply = new();

        // Fragment odds are drawn against a fixed purpose so the decision replays with the
        // turn, independent of how many pools this turn happened to touch.
        if (_persona.Pools.ContainsKey(_picker.Resolve(MoodFragmentPool))
            && rng.Next("compose:fragment", FragmentOdds) == 0)
        {
            string? fragment = _picker.Pick(MoodFragmentPool, modeId, rng, slots);
            if (fragment is not null && !Echoes(fragment, core))
            {
                reply.Append(fragment);
            }
        }

        Append(reply, core);

        // The adapter supplies the remembered quote; the framing around it is authored, same as
        // everything else. No pool, no renderable line, no tail — the core reply still ships.
        if (callback is { Quote: { } quote } && !string.IsNullOrWhiteSpace(quote) && WantsCallback(rng))
        {
            string? tail = _picker.Pick(
                callback.Author is null ? CallbackPool : QuoteBoardPool,
                modeId,
                rng,
                slots,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CallbackCapture] = quote.Trim(),
                    [AuthorCapture] = callback.Author?.Trim() ?? string.Empty,
                });
            if (tail is not null)
            {
                Append(reply, tail);
            }
        }

        return reply.ToString();
    }

    /// <summary>
    /// Whether a mood fragment would repeat the first word of the line it is about to sit in front
    /// of — "sure." in front of "sure, whatever you say." reads as two messages glued together.
    /// </summary>
    /// <remarks>
    /// Four of the corpus review's BROKEN-OUTPUT findings were this, all of them the four-word
    /// default fragment pool ("sure.", "ok.", "right.", "mm.") landing on a pool line that opens
    /// the same way. Only the first word is compared: the fragment is one word by construction,
    /// and looking deeper would suppress fragments that read fine.
    /// </remarks>
    private static bool Echoes(string fragment, string core) =>
        FirstWord(fragment).Equals(FirstWord(core), StringComparison.OrdinalIgnoreCase)
        && FirstWord(fragment).Length > 0;

    /// <summary>Leading run of letters, so "sure." and "sure," both give "sure".</summary>
    private static string FirstWord(string text)
    {
        ReadOnlySpan<char> span = text.AsSpan().TrimStart();
        int end = 0;
        while (end < span.Length && char.IsLetter(span[end]))
        {
            end++;
        }

        return new string(span[..end]);
    }

    private static void Append(StringBuilder reply, string part)
    {
        if (reply.Length > 0)
        {
            reply.Append(' ');
        }

        reply.Append(part.Trim());
    }

    private static string Join(string left, string right) => $"{left.Trim()} {right.Trim()}";
}
