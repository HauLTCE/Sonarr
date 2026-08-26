using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Chat;

/// <summary>
/// Translates between the engine's immutable <see cref="ConversationState"/> and the two
/// places it is stored: the <c>chat.person</c> row (durable) and the <c>chat:hot</c> cache
/// snapshot (transient).
/// </summary>
/// <remarks>
/// The row is the authority for everything <c>chat.person</c> has a column for. The topic
/// stack and the pending-question queue have no column — both are conversation-scoped
/// (docs/05 gives pending questions a 10 min TTL, and a topic five turns cold is noise), so
/// they live in the snapshot and are simply absent after a restart.
/// <para>ponytail: topic/pending are hot-cache-only; if she should resume a topic across a
/// restart, add a <c>topics</c> jsonb column to <c>chat.person</c> and map it here.</para>
/// </remarks>
public static class ChatState
{
    /// <summary>
    /// A row for someone she has never spoken to, seeded from the persona baselines.
    /// </summary>
    /// <remarks>
    /// The baselines have to go in here rather than being left to <see cref="FromPerson"/>: the
    /// five typed registers are always present in the jsonb payload, so a default
    /// <see cref="PersonRegisters"/> reads back as five explicit zeros and is indistinguishable
    /// from "she has decided she feels nothing about you". Starting a stranger at fondness 0 when
    /// the persona says 3 makes her colder to everyone she meets than she was authored to be.
    /// </remarks>
    public static Person NewPerson(long guildId, long userId, PersonaRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new Person
        {
            GuildId = guildId,
            UserId = userId,
            DialogueState = root.StartActivity,
            Registers = PersonRegisters.From(root.Personality.Baselines),
            RelationshipTier = ModeSelector.SelectTier(
                root, root.Personality.Baselines.GetValueOrDefault(Registers.Names.Trust))?.Id ?? string.Empty,
        };
    }

    /// <summary>Loads the row into engine state. Registers missing from the row take their baseline.</summary>
    public static ConversationState FromPerson(Person person, PersonaRoot root, ulong salt)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(root);

        return new ConversationState
        {
            Turn = person.LogicalClock,
            Activities = ActivityStack.Restore(ReadStrings(person.ActivityStack), root.StartActivity),
            Registers = MergeWithBaselines(root, person.Registers.ToDictionary()),
            Topics = TopicStack.Empty,
            Pending = PendingQuestions.Empty,
            Fired = FiredLog.Restore(ReadLongs(person.FiredLog)),
            Slots = ReadSlots(person.Slots),
            AssignedNickname = person.AssignedNickname,
            Salt = salt,
        };
    }

    /// <summary>Writes engine state back onto the tracked row. Derived fields are recomputed, never trusted.</summary>
    public static void ApplyTo(Person person, ConversationState state, PersonaRoot root)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(root);

        person.LogicalClock = state.Turn;
        person.DialogueState = state.Activities.Current;
        person.Registers = PersonRegisters.From(state.Registers.Values);
        person.Slots = WriteSlots(state.Slots);
        person.FiredLog = WriteLongs(state.Fired.Entries);
        person.ActivityStack = new JsonArray([.. state.Activities.Layers.Select(l => JsonValue.Create(l))]);
        person.AssignedNickname = state.AssignedNickname;

        // Tier is derived from trust, so it is only stored to let SQL read it (/relationship) — the
        // engine always recomputes it from the registers.
        person.RelationshipTier =
            ModeSelector.SelectTier(root, state.Registers[Registers.Names.Trust])?.Id ?? string.Empty;
    }

    /// <summary>Restores the full state — including the transient parts — from a cache snapshot.</summary>
    public static ConversationState FromSnapshot(ChatStateSnapshot snapshot, PersonaRoot root, ulong salt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(root);

        return new ConversationState
        {
            Turn = snapshot.Turn,
            Activities = ActivityStack.Restore(snapshot.Activities, root.StartActivity),
            Registers = MergeWithBaselines(root, snapshot.Registers),
            Topics = TopicStack.Restore(snapshot.Topics.Select(t => new TopicEntry(t.Topic, t.Weight))),
            Pending = PendingQuestions.Restore(
                snapshot.Pending.Select(p => new PendingQuestion(p.Slot, p.AskedAtTurn))),
            Fired = FiredLog.Restore(snapshot.Fired),
            Slots = snapshot.Slots.ToImmutableDictionary(StringComparer.Ordinal),
            AssignedNickname = snapshot.AssignedNickname,
            Salt = salt,
        };
    }

    public static ChatStateSnapshot ToSnapshot(ConversationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ChatStateSnapshot
        {
            Turn = state.Turn,
            Activities = [.. state.Activities.Layers],
            Registers = new Dictionary<string, double>(state.Registers.Values, StringComparer.Ordinal),
            Topics = [.. state.Topics.Entries.Select(e => new TopicSnapshot(e.Topic, e.Weight))],
            Pending = [.. state.Pending.Queue.Select(q => new PendingSnapshot(q.Slot, q.AskedAtTurn))],
            Fired = new Dictionary<string, long>(state.Fired.Entries, StringComparer.Ordinal),
            Slots = new Dictionary<string, string>(state.Slots, StringComparer.Ordinal),
            AssignedNickname = state.AssignedNickname,
        };
    }

    /// <summary>
    /// Baselines first, stored values on top: a register added to the persona after this row
    /// was written starts at its baseline instead of a silent 0.
    /// </summary>
    private static Registers MergeWithBaselines(PersonaRoot root, IReadOnlyDictionary<string, double> stored)
    {
        Dictionary<string, double> merged = new(root.Personality.Baselines, StringComparer.Ordinal);
        foreach ((string register, double value) in stored)
        {
            merged[register] = value;
        }

        return Registers.From(merged);
    }

    // Every read below tolerates a hand-edited or older-shaped jsonb payload by skipping what
    // it cannot parse: a bad row must not be able to stop her replying.
    private static IEnumerable<string> ReadStrings(JsonArray array) =>
        array.OfType<JsonValue>()
            .Select(v => v.TryGetValue(out string? s) ? s : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))!;

    private static IEnumerable<KeyValuePair<string, long>> ReadLongs(JsonObject json)
    {
        foreach ((string key, JsonNode? node) in json)
        {
            // Not TryGetValue<long>: a JsonValue holding an int (which is what an in-memory
            // JsonObject built by ApplyTo or a hand-edited row can hold) refuses to hand itself
            // over as a long, and a silently-dropped fired log means every cooldown resets.
            if (node is JsonValue value
                && value.GetValueKind() == JsonValueKind.Number
                && long.TryParse(
                    value.ToJsonString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long turn))
            {
                yield return new KeyValuePair<string, long>(key, turn);
            }
        }
    }

    private static ImmutableDictionary<string, string> ReadSlots(JsonObject json)
    {
        ImmutableDictionary<string, string>.Builder slots =
            ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach ((string key, JsonNode? node) in json)
        {
            if (node is JsonValue value && value.TryGetValue(out string? text)
                && !string.IsNullOrWhiteSpace(text))
            {
                slots[key] = text;
            }
        }

        return slots.ToImmutable();
    }

    private static JsonObject WriteSlots(IReadOnlyDictionary<string, string> slots)
    {
        JsonObject json = [];
        foreach ((string key, string value) in slots)
        {
            json[key] = JsonValue.Create(value);
        }

        return json;
    }

    private static JsonObject WriteLongs(IReadOnlyDictionary<string, long> entries)
    {
        JsonObject json = [];
        foreach ((string key, long value) in entries)
        {
            json[key] = JsonValue.Create(value);
        }

        return json;
    }
}
