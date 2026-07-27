using System.Collections.Immutable;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// The engine: <c>(state, input) → (state, reply)</c>, and nothing else.
/// </summary>
/// <remarks>
/// Pure by construction. No clock, no RNG of its own, no I/O, no Discord types — the adapter
/// loads state, calls <see cref="Turn"/>, persists what comes back, and posts the text. That
/// is what makes a stored turn replayable and what keeps a persona edit from ever becoming a
/// server action (docs/10 contract 4).
/// </remarks>
public sealed class ChatEngine(PersonaGraph persona, ISemanticMatcher? semantic = null)
{
    private readonly PersonaGraph _persona = persona
        ?? throw new ArgumentNullException(nameof(persona));

    private readonly IntentRecognizer _recognizer = new(persona, semantic);

    /// <summary>Runs one turn. Never throws for ordinary input, including empty text.</summary>
    public TurnResult Turn(ConversationState state, TurnInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);

        long turn = state.Turn + 1;
        IDeterministicRandom rng = new TurnSeededRandom(turn, state.Salt);

        // Decay first: the mood she answers with is the mood after the time away, not the one
        // the last message left behind.
        Registers registers = state.Registers.Decay(_persona.Root, 1 + input.ExtraDecaySteps);
        ConversationState working = state with { Turn = turn, Registers = registers };

        (PendingQuestions pending, IReadOnlyList<PendingQuestion> ignored) =
            working.Pending.Expire(turn);
        working = working with { Pending = pending };

        string modeId = ModeSelector.Select(_persona.Root, registers).Id;
        MatchOutcome outcome = _recognizer.Recognize(input.Text, working.ToMatchContext(_persona.Root));
        MatchCandidate? primary = outcome.Ranked.FirstOrDefault(
            c => working.Fired.IsEligible(c.Intent, turn));

        ReplyComposer composer = new(_persona, new LinePicker(_persona, input.ActiveOverlays));
        (ConversationState next, string? text, string? intentId) =
            primary is null
                ? Fallback(working, composer, modeId, rng, input)
                : Apply(working, primary, composer, modeId, rng, input);

        return new TurnResult
        {
            State = next,
            Text = text,
            IntentId = intentId,
            ModeId = modeId,
            SideEffectIntentIds = [.. outcome.SideEffects
                .Where(c => c.IntentId != intentId)
                .Select(c => c.IntentId)],
            LearnedSlots = primary is null ? NoSlots : Learned(primary),
            IgnoredQuestions = ignored,
        };
    }

    private (ConversationState, string?, string?) Apply(
        ConversationState state,
        MatchCandidate primary,
        ReplyComposer composer,
        string modeId,
        IDeterministicRandom rng,
        TurnInput input)
    {
        IntentDef intent = primary.Intent;

        ActivityStack activities = state.Activities;
        if (intent.PopActivity)
        {
            activities = activities.Pop();
        }

        if (intent.PushActivity is { } push)
        {
            activities = activities.Push(push);
        }

        Dictionary<string, string> learned = Learned(primary);
        ImmutableDictionary<string, string> slots = state.Slots.SetItems(learned);

        PendingQuestions pending = state.Pending;
        // An answered slot closes its question before a new one opens, so answering "what's
        // your name" while she asks again does not leave the old entry behind.
        foreach (string slot in learned.Keys)
        {
            pending = pending.Answer(slot);
        }

        foreach (string slot in intent.Asks)
        {
            pending = pending.Ask(slot, state.Turn);
        }

        ConversationState next = state with
        {
            Activities = activities,
            Registers = state.Registers.With(intent.Affect),
            Topics = state.Topics.Advance(intent.Topic),
            Pending = pending,
            Fired = state.Fired.Record(intent.Id, state.Turn),
            Slots = slots,
        };

        // Compose against the post-update state so a name learned this turn is available to
        // this turn's own template.
        string? text = composer.Compose(primary, next, modeId, rng, input.Callback);
        return text is null
            ? Fallback(next, composer, modeId, rng, input)
            : (next, text, intent.Id);
    }

    /// <summary>
    /// Nothing matched (or the winning pool could render nothing): the current activity's
    /// fallback pool answers. She always says something when addressed directly.
    /// </summary>
    private (ConversationState, string?, string?) Fallback(
        ConversationState state,
        ReplyComposer composer,
        string modeId,
        IDeterministicRandom rng,
        TurnInput input)
    {
        ActivityDef? activity = _persona.Root.Activities
            .FirstOrDefault(a => a.Id == state.Activities.Current);
        ConversationState next = state with { Topics = state.Topics.Advance(null) };
        if (activity is null)
        {
            return (next, null, null);
        }

        return (next,
            composer.ComposeFromPool(activity.FallbackPool, next, modeId, rng, input.Callback),
            null);
    }

    private static Dictionary<string, string> Learned(MatchCandidate candidate)
    {
        Dictionary<string, string> learned = new(StringComparer.Ordinal);
        foreach ((string slot, string capture) in candidate.Intent.Learns)
        {
            if (candidate.Captures.TryGetValue(capture, out string? value)
                && !string.IsNullOrWhiteSpace(value))
            {
                learned[slot] = value.Trim();
            }
        }

        return learned;
    }

    private static readonly IReadOnlyDictionary<string, string> NoSlots =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
