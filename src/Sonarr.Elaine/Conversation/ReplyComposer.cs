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
public sealed class ReplyComposer(PersonaGraph persona, LinePicker picker)
{
    /// <summary>The mode-covered pool the opener is drawn from (see <c>sonarr.yaml</c>).</summary>
    public const string MoodFragmentPool = "mood_fragment";

    /// <summary>1-in-N turns carry a mood fragment. Every turn would be a verbal tic.</summary>
    private const int FragmentOdds = 3;

    /// <summary>1-in-N turns carry a callback, and only when one was offered.</summary>
    private const int CallbackOdds = 2;

    private readonly PersonaGraph _persona = persona
        ?? throw new ArgumentNullException(nameof(persona));

    private readonly LinePicker _picker = picker ?? throw new ArgumentNullException(nameof(picker));

    /// <summary>
    /// Composes a reply for <paramref name="candidate"/>, or null when the pool could not
    /// produce a renderable line at all (the caller falls back to the activity's pool).
    /// </summary>
    /// <param name="callback">
    /// An authored callback tail the adapter supplies from a relevance-gated pgvector episode
    /// lookup. Null on the common path; the engine never searches for one itself.
    /// </param>
    public string? Compose(
        MatchCandidate candidate,
        ConversationState state,
        string modeId,
        IDeterministicRandom rng,
        string? callback = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);

        string? core = _picker.Pick(
            candidate.Intent.Pool, modeId, rng, state.Slots, candidate.Captures);
        if (core is null)
        {
            return null;
        }

        // The intent's own template, when present, is appended to the pool line — that is how
        // a generic pool line ("sure.") carries the specific bit ("nice to meet you, Sam").
        if (candidate.Intent.Template is { } template
            && TemplateRenderer.TryRender(
                template, state.Slots, candidate.Captures, out string rendered))
        {
            core = Join(core, rendered);
        }

        return Wrap(core, state, modeId, rng, callback);
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
        string? callback = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);

        string? core = _picker.Pick(poolId, modeId, rng, state.Slots);
        return core is null ? null : Wrap(core, state, modeId, rng, callback);
    }

    private string Wrap(
        string core,
        ConversationState state,
        string modeId,
        IDeterministicRandom rng,
        string? callback)
    {
        StringBuilder reply = new();

        // Fragment odds are drawn against a fixed purpose so the decision replays with the
        // turn, independent of how many pools this turn happened to touch.
        if (_persona.Pools.ContainsKey(_picker.Resolve(MoodFragmentPool))
            && rng.Next("compose:fragment", FragmentOdds) == 0)
        {
            string? fragment = _picker.Pick(MoodFragmentPool, modeId, rng, state.Slots);
            if (fragment is not null)
            {
                reply.Append(fragment);
            }
        }

        Append(reply, core);

        if (!string.IsNullOrWhiteSpace(callback)
            && rng.Next("compose:callback", CallbackOdds) == 0)
        {
            Append(reply, callback);
        }

        return reply.ToString();
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
