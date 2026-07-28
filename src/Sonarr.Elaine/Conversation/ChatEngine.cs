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

        ReplyComposer composer = new(
            _persona, new LinePicker(_persona, input.ActiveOverlays), input.ShakySlots);
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
        if (text is null)
        {
            return Fallback(next, composer, modeId, rng, input);
        }

        (next, text) = MarkTierChange(state, next, text, composer, modeId, rng);
        return (next, text, intent.Id);
    }

    /// <summary>
    /// When this turn's affect crossed a tier boundary, appends the authored moment for the new
    /// tier — once per tier per person, ever.
    /// </summary>
    /// <remarks>
    /// docs/07: a tier-up gets a line, not a number in an embed. Both tiers are derivable
    /// in-engine from the trust register, so this needs no adapter plumbing and no new
    /// <see cref="TurnResult"/> field — it is still just text.
    /// <para>The once-per-tier gate reuses the persisted fired-log under a synthetic
    /// <c>tier:&lt;id&gt;</c> key: trust oscillates around a threshold, and "we're close now"
    /// lands once or it is noise. That gate is also what keeps demotions quiet — sliding back
    /// to a tier you already reached says nothing, while the drop into nemesis is a first
    /// arrival and does get its line.</para>
    /// </remarks>
    private (ConversationState, string) MarkTierChange(
        ConversationState before,
        ConversationState after,
        string text,
        ReplyComposer composer,
        string modeId,
        IDeterministicRandom rng)
    {
        string trust = Registers.Names.Trust;
        TierDef? from = ModeSelector.SelectTier(_persona.Root, before.Registers[trust]);
        TierDef? to = ModeSelector.SelectTier(_persona.Root, after.Registers[trust]);
        if (to is null || from?.Id == to.Id)
        {
            return (after, text);
        }

        // No direction check: the fired-log gate already makes each tier's line a first-arrival
        // moment, so sliding back down to a tier she has already announced is silent while the
        // first drop into nemesis is not.
        after = Nickname(after, modeId, rng);

        string key = TierFiredKey(to.Id);
        if (after.Fired.HasFired(key))
        {
            return (after, text);
        }

        string? moment = composer.Line($"{TierMomentPoolPrefix}{to.Id}", after, modeId, rng);
        return moment is null
            ? (after, text)
            : (after with { Fired = after.Fired.Record(key, after.Turn) }, $"{text} {moment}");
    }

    /// <summary>
    /// Picks a nickname for someone who has moved up a tier without ever giving a name.
    /// </summary>
    /// <remarks>
    /// v1 did this on the spot every time it needed one, so "trouble" became "rando" became
    /// "nobody" mid-conversation. Drawn once, stored, never redrawn — that is the whole feature.
    /// <para>A stored <c>name</c> wins: she calls you what you told her to.</para>
    /// </remarks>
    private ConversationState Nickname(
        ConversationState state, string modeId, IDeterministicRandom rng)
    {
        if (state.AssignedNickname is not null
            || state.Slots.ContainsKey(NameSlot)
            || !_persona.Pools.TryGetValue(NicknamePool, out PoolDef? pool))
        {
            return state;
        }

        IReadOnlyList<string> options = pool.For(modeId);
        return options.Count == 0
            ? state
            : state with { AssignedNickname = options[rng.Next(NicknamePool, options.Count)] };
    }

    /// <summary>Pool the assigned nickname is drawn from — bare words, not lines.</summary>
    public const string NicknamePool = "assigned_nickname";

    /// <summary>The slot a real, self-declared name lives in.</summary>
    private const string NameSlot = "name";

    /// <summary>Pool id for a tier's promotion line: <c>tier_up_&lt;tier id&gt;</c>.</summary>
    public const string TierMomentPoolPrefix = "tier_up_";

    /// <summary>Fired-log key that makes a tier moment once-per-person.</summary>
    public static string TierFiredKey(string tierId) => $"tier:{tierId}";

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
