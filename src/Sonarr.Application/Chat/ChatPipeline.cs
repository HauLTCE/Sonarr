using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Chat;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Chat;

/// <summary>
/// The chat adapter: gate → load → understand → decide → persist → deliver (docs/10).
/// </summary>
/// <remarks>
/// Everything wall-clock, stateful or I/O-shaped happens here so <c>Sonarr.Elaine</c> stays a
/// pure function. Order matters and is the docs' order: the cheap rejections come before any
/// database read, and the transaction commits before she says anything, so she never claims to
/// remember something the commit lost.
/// </remarks>
public sealed class ChatPipeline(
    PersonaHolder persona,
    IPersonRepository people,
    ISessionCache cache,
    IFeatureGate features,
    IClock clock,
    IGuildConfigService config,
    ILogger<ChatPipeline> log,
    SemanticIntentIndex? semantic = null,
    CallbackRetriever? callbacks = null) : IChatPipeline
{
    /// <summary>Replies per channel per hour. Above this she has said enough (docs/05 budget).</summary>
    public const int EngagementBudget = 30;

    /// <summary>Typing-delay pacing: fast enough not to stall, slow enough to look typed.</summary>
    public const int TypingMsPerChar = 18;

    public static readonly TimeSpan MinTypingDelay = TimeSpan.FromMilliseconds(600);

    public static readonly TimeSpan MaxTypingDelay = TimeSpan.FromSeconds(4);

    public async Task<ChatDecision> HandleAsync(ChatRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = clock.UtcNow;

        // Ambient messages still feed the ring buffer (rapid-fire and two-people-talking
        // detection need them) but never get a reply.
        await cache.PushRecentMessageAsync(
            request.ChannelId,
            new RecentMessage(request.UserId, now, request.Addressed),
            ct).ConfigureAwait(false);

        if (!request.Addressed)
        {
            return ChatDecision.NotAddressed;
        }

        if (!await features.IsEnabledAsync(FeatureNames.Chat, request.GuildId, ct).ConfigureAwait(false))
        {
            return ChatDecision.Skip(ChatDecision.SkipReasons.FeatureOff);
        }

        if (await cache.GetEngagementAsync(request.ChannelId, ct).ConfigureAwait(false) >= EngagementBudget)
        {
            return ChatDecision.Skip(ChatDecision.SkipReasons.BudgetSpent);
        }

        PersonaGraph graph = persona.Current;
        long guildId = (long)request.GuildId;
        long userId = (long)request.UserId;

        Person person = await people.GetAsync(guildId, userId, ct).ConfigureAwait(false)
            ?? ChatState.NewPerson(guildId, userId, graph.Root);

        ChatSessionState? session = await cache.GetSessionAsync(request.GuildId, request.UserId, ct)
            .ConfigureAwait(false);

        // Her calendar is the room's, not the container's: 3–6 am and "the mood of the day" are
        // about when the people talking to her are awake. Config is Redis-cached, so this is a
        // dictionary lookup on the warm path, and an unset or unresolvable zone lands on UTC —
        // the same fallback reminders use, and a dev box without ICU can only ever get that one.
        ClockSignals signals = ClockSignals.From(
            graph,
            TimeZoneInfo.ConvertTime(now, await GuildZoneAsync(request.GuildId, ct).ConfigureAwait(false)),
            session?.LastTurnAt ?? person.UpdatedAt);

        // Salt mixes the person with the day so two people on the same turn hear different
        // lines, and the same person hears a different one tomorrow.
        ulong salt = signals.DaySeed
            ^ StableHash.Of(request.UserId.ToString(CultureInfo.InvariantCulture));

        ChatStateSnapshot? hot = await cache.GetHotPersonAsync<ChatStateSnapshot>(
            request.GuildId, request.UserId, ct).ConfigureAwait(false);

        // The row is the authority for durable fields; the snapshot additionally carries topics
        // and pending questions, so a live conversation keeps them.
        ConversationState state = hot is not null && hot.Turn >= person.LogicalClock
            ? ChatState.FromSnapshot(hot, graph.Root, salt)
            : ChatState.FromPerson(person, graph.Root, salt);

        Normalized normalized = Normalizer.Normalize(request.Text);
        bool repeatPing = await IsRepeatPingAsync(request, normalized, ct).ConfigureAwait(false);

        // The matcher embeds nothing until the engine asks for a rescue, and the engine only
        // asks on a lexical miss — so the common path stays a few milliseconds (docs/10 budget).
        ISemanticMatcher? matcher = semantic?.MatcherFor(graph.Root, request.Text);

        TurnResult result = new ChatEngine(graph, matcher).Turn(state, new TurnInput
        {
            Text = request.Text,
            ActiveOverlays = signals.Overlays,
            ExtraDecaySteps = signals.ExtraDecaySteps,
            Callback = await CallbackAsync(request, state, salt, ct).ConfigureAwait(false),
            ShakySlots = await ShakySlotsAsync(guildId, userId, state, ct).ConfigureAwait(false),
        });

        // Read from the pre-turn state: its fired log still holds what she said last, which is the
        // opinion an "agreed" this turn is agreeing with.
        StanceTaken? stance = ChatStances.Taken(graph, state, result.IntentId);

        IReadOnlyList<AffectDelta> adapterAffect = ChatAffect.Deltas(
            graph.Root,
            new AffectSignals(
                result.IntentId,
                normalized.Style,
                repeatPing,
                TurnsSinceLastApology(state, result.State.Turn),
                stance?.Agreed));

        ConversationState next = result.State with
        {
            Registers = result.State.Registers.With(adapterAffect),
        };

        string? reaction = result.Reaction ?? ChatAffect.Reaction(result.IntentId);
        if (result.Text is null && reaction is null)
        {
            // Nothing to say and nothing to persist worth a transaction: she was addressed but
            // the persona had no line, which is a persona gap, not a turn.
            log.LogDebug("Chat had no line for {IntentId} in {GuildId}", result.IntentId, request.GuildId);
            return ChatDecision.Skip(ChatDecision.SkipReasons.NothingToSay);
        }

        ChatState.ApplyTo(person, next, graph.Root);
        await people.SaveTurnAsync(
            new ChatTurnWrite
            {
                Person = person,
                Facts = [.. result.LearnedSlots.Select(s => new FactWrite(s.Key, s.Value, next.Turn))],
                Episodes = Episodes(result, next, now),
                Events = Events(state, next, result, now),
                Stance = stance is null
                    ? null
                    : new StanceWrite(
                        stance.Stance.Topic,
                        stance.Stance.Stance,
                        stance.Stance.Pool,
                        stance.Agreed),
            },
            ct).ConfigureAwait(false);

        await AfterCommitAsync(request, next, normalized, now, ct).ConfigureAwait(false);

        string hash = Hash(normalized);
        await cache.MarkRepliedAsync(request.ChannelId, request.MessageId, hash, ct).ConfigureAwait(false);

        return new ChatDecision(result.Text, reaction, TypingDelayFor(result.Text), hash, null);
    }

    /// <summary>
    /// The episodic quote to offer the composer as a callback tail, or null.
    /// </summary>
    /// <remarks>
    /// The odds are drawn here, before the lookup, using the same seed the engine will use for
    /// the same turn — so on the turns that would discard a callback anyway we never pay for the
    /// embedding or the vector query. That is the whole reason
    /// <see cref="ReplyComposer.WantsCallback"/> is public.
    /// </remarks>
    /// <summary>
    /// The zone whose calendar decides her overlays and mood of the day, UTC when the guild has
    /// not set one.
    /// </summary>
    /// <remarks>
    /// Deliberately the guild's zone and never the speaker's: an overlay replaces a pool for the
    /// whole room, so if it followed whoever happened to be typing she would be in October for
    /// one person and not for the next. <c>InvariantGlobalization</c> means IANA ids only resolve
    /// where tzdata exists (see ZoneResolver) — hence the UTC fallback rather than a throw.
    /// </remarks>
    private async Task<TimeZoneInfo> GuildZoneAsync(ulong guildId, CancellationToken ct)
    {
        ConfigValue? configured = await config.GetAsync(guildId, ConfigKeys.Timezone, ct)
            .ConfigureAwait(false);

        return configured?.Raw is { } id
               && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out TimeZoneInfo? zone)
            ? zone
            : TimeZoneInfo.Utc;
    }

    private async Task<string?> CallbackAsync(
        ChatRequest request, ConversationState state, ulong salt, CancellationToken ct)
    {
        long turn = state.Turn + 1;
        if (callbacks is null || !ReplyComposer.WantsCallback(new TurnSeededRandom(turn, salt)))
        {
            return null;
        }

        return await callbacks.QuoteAsync(
            (long)request.GuildId, (long)request.UserId, request.Text, turn, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Which of the slots she is about to substitute are facts she only heard once.
    /// </summary>
    /// <remarks>
    /// Confidence lives in <c>chat.fact</c>, so the threshold comparison happens here and the
    /// engine receives names only (docs/10 contract: nothing database-shaped crosses into
    /// <c>Sonarr.Elaine</c>). Fact predicates and slot names are the same vocabulary — a fact is
    /// what a slot value was learned as — so the intersection needs no mapping table.
    /// <para>Skipped entirely when she is holding no slots, which is the common case for a
    /// stranger: no slots, no possible hedge, no query.</para>
    /// </remarks>
    private async Task<IReadOnlySet<string>> ShakySlotsAsync(
        long guildId, long userId, ConversationState state, CancellationToken ct)
    {
        if (state.Slots.Count == 0)
        {
            return EmptySlotNames;
        }

        IReadOnlyList<Fact> facts = await people.GetFactsAsync(guildId, userId, ct)
            .ConfigureAwait(false);

        return facts
            .Where(f => f.Confidence < HedgeBelowConfidence && state.Slots.ContainsKey(f.Predicate))
            .Select(f => f.Predicate)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Below this she hedges. Sits above the initial 0.6 and below one reinforcement (0.75), so a
    /// fact heard once is shaky and a fact you repeated once is not — which is the whole point.
    /// </summary>
    public const float HedgeBelowConfidence = 0.7f;

    private static readonly IReadOnlySet<string> EmptySlotNames =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Typing delay proportional to the reply, clamped so she is neither instant nor slow.</summary>
    public static TimeSpan TypingDelayFor(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TimeSpan.Zero;
        }

        TimeSpan scaled = TimeSpan.FromMilliseconds(text.Length * TypingMsPerChar);
        return scaled < MinTypingDelay ? MinTypingDelay
            : scaled > MaxTypingDelay ? MaxTypingDelay
            : scaled;
    }

    /// <summary>
    /// Cache writes that must not be able to undo the commit: every one fails open
    /// (ISessionCache contract), so Redis being down costs at most "the conversation feels
    /// reset" rather than a lost turn.
    /// </summary>
    private async Task AfterCommitAsync(
        ChatRequest request,
        ConversationState next,
        Normalized normalized,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await cache.SetHotPersonAsync(request.GuildId, request.UserId, ChatState.ToSnapshot(next), ct)
            .ConfigureAwait(false);
        await cache.TouchSessionAsync(request.GuildId, request.UserId, now, ct).ConfigureAwait(false);
        await cache.IncrementEngagementAsync(request.ChannelId, ct).ConfigureAwait(false);
        await cache.SetLastPingHashAsync(request.GuildId, request.UserId, Hash(normalized), ct)
            .ConfigureAwait(false);

        if (next.Pending.Newest is { } pending)
        {
            await cache.SetPendingQuestionAsync(request.GuildId, request.UserId, pending.Slot, ct)
                .ConfigureAwait(false);
        }
        else
        {
            await cache.ClearPendingQuestionAsync(request.GuildId, request.UserId, ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> IsRepeatPingAsync(
        ChatRequest request,
        Normalized normalized,
        CancellationToken ct)
    {
        string? previous = await cache.GetLastPingHashAsync(request.GuildId, request.UserId, ct)
            .ConfigureAwait(false);
        return previous is not null && previous == Hash(normalized);
    }

    /// <summary>
    /// Stable hash of the normalized text. Content-derived, not the content: what goes into
    /// Redis for edit and repeat-ping detection is a number, never the message.
    /// </summary>
    private static string Hash(Normalized normalized) =>
        StableHash.Of(normalized.Lower).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Only what she said is stored as an episode, never the user's message — docs/07: no
    /// message-content logging. The quote is her own authored line, so recall stays authored too.
    /// </summary>
    private static List<Episode> Episodes(
        TurnResult result,
        ConversationState next,
        DateTimeOffset now)
        => result.Text is null
            ? []
            : [new Episode
            {
                Quote = result.Text,
                SentimentTag = result.ModeId ?? string.Empty,
                Turn = next.Turn,
                HappenedAt = now,
            }];

    /// <summary>One event per turn that actually moved a register, so trajectory stays explainable.</summary>
    private static List<RelationshipEvent> Events(
        ConversationState before,
        ConversationState after,
        TurnResult result,
        DateTimeOffset now)
    {
        JsonObject delta = [];
        foreach ((string register, double value) in after.Registers.Values)
        {
            double moved = value - before.Registers[register];
            if (Math.Abs(moved) > 0.0001)
            {
                delta[register] = JsonValue.Create(Math.Round(moved, 4));
            }
        }

        return delta.Count == 0
            ? []
            : [new RelationshipEvent
            {
                Delta = delta,
                Cause = result.IntentId ?? string.Empty,
                Turn = after.Turn,
                At = now,
            }];
    }

    /// <summary>
    /// Turns since the last apology, from the fired log — the persisted log is what makes the
    /// sincerity check work across restarts instead of resetting every message.
    /// </summary>
    private static long? TurnsSinceLastApology(ConversationState before, long turn)
        => before.Fired.LastTurn(ChatAffect.Intents.Apology) is { } last ? turn - last : null;
}
