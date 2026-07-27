using System.Text.Json;
using System.Text.Json.Nodes;
using Sonarr.Application.Chat;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// The row ⇄ engine mapping. Everything the persona declares has to survive a round trip, or she
/// wakes up from a restart with a different mood than she went to sleep with.
/// </summary>
public sealed class ChatStateTests
{
    private static PersonaRoot Root => Build.Graph.Root;

    [Fact]
    public void NewPerson_starts_a_stranger_at_the_persona_baselines()
    {
        ConversationState state = ChatState.FromPerson(
            ChatState.NewPerson((long)Build.Guild, (long)Build.User, Root), Root, 0);

        foreach ((string register, double baseline) in Root.Personality.Baselines)
        {
            Assert.Equal(baseline, state.Registers[register], 4);
        }
    }

    [Fact]
    public void ApplyTo_then_FromPerson_round_trips_every_declared_register()
    {
        // Every register the persona declares, moved off its baseline by a distinct amount.
        Dictionary<string, double> moved = Root.Personality.Baselines
            .Select((b, i) => (b.Key, Value: b.Value + 1 + (i * 0.25)))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        ConversationState before = ChatState.FromPerson(new Person(), Root, 7) with
        {
            Registers = Registers.From(moved),
        };

        Person row = new();
        ChatState.ApplyTo(row, before, Root);
        ConversationState after = ChatState.FromPerson(row, Root, 7);

        foreach ((string register, double value) in moved)
        {
            Assert.Equal(value, after.Registers[register], 4);
        }
    }

    [Fact]
    public void ApplyTo_survives_the_jsonb_serialization_the_column_uses()
    {
        Person row = new();
        ChatState.ApplyTo(row, StateWith(Registers.Names.Fondness, 6.5), Root);

        // The registers column is serialized text, so a register that only round-trips in memory
        // is not actually persisted.
        PersonRegisters reread = JsonSerializer.Deserialize<PersonRegisters>(
            JsonSerializer.Serialize(row.Registers, Json), Json)!;

        Assert.Equal(6.5, reread.ToDictionary()[Registers.Names.Fondness], 4);
        foreach (string register in Root.Personality.Baselines.Keys)
        {
            Assert.True(
                reread.ToDictionary().ContainsKey(register),
                $"register {register} did not survive jsonb serialization");
        }
    }

    [Fact]
    public void FromPerson_gives_a_register_added_after_the_row_was_written_its_baseline()
    {
        // A row written before the persona declared amusement/confidence/energy: only the five
        // typed registers are in the payload.
        Person row = new()
        {
            Registers = PersonRegisters.From(
                new Dictionary<string, double>(StringComparer.Ordinal) { [Registers.Names.Anger] = 9 }),
        };

        ConversationState state = ChatState.FromPerson(row, Root, 0);

        Assert.Equal(9, state.Registers[Registers.Names.Anger], 4);
        foreach ((string register, double baseline) in Root.Personality.Baselines)
        {
            // The typed five are always present in the payload, so only the persona's extra
            // registers can be absent — those are the ones that must not read back as 0.
            if (!PersonRegisters.Names.Typed.Contains(register))
            {
                Assert.Equal(baseline, state.Registers[register], 4);
            }
        }
    }

    [Fact]
    public void ApplyTo_recomputes_the_tier_from_trust_rather_than_trusting_the_row()
    {
        Person row = new() { RelationshipTier = "nonsense" };

        ChatState.ApplyTo(row, StateWith(Registers.Names.Trust, 20), Root);

        Assert.Equal(
            ModeSelector.SelectTier(Root, 20)?.Id,
            row.RelationshipTier);
        Assert.NotEqual("nonsense", row.RelationshipTier);
    }

    [Fact]
    public void ApplyTo_persists_the_fired_log_so_cooldowns_outlive_a_restart()
    {
        ConversationState state = ChatState.FromPerson(new Person(), Root, 0) with
        {
            Turn = 12,
            Fired = FiredLog.Empty.Record("APOLOGY", 4),
        };

        Person row = new();
        ChatState.ApplyTo(row, state, Root);

        Assert.Equal(4, ChatState.FromPerson(row, Root, 0).Fired.LastTurn("APOLOGY"));
    }

    [Fact]
    public void ToSnapshot_then_FromSnapshot_keeps_topics_and_pending_questions()
    {
        ConversationState state = ChatState.FromPerson(new Person(), Root, 3) with
        {
            Turn = 5,
            Topics = TopicStack.Empty.Advance("music"),
            Pending = PendingQuestions.Empty.Ask("job", 5),
        };

        ChatStateSnapshot snapshot = RoundTrip(ChatState.ToSnapshot(state));
        ConversationState restored = ChatState.FromSnapshot(snapshot, Root, 3);

        Assert.Equal("music", Assert.Single(restored.Topics.Entries).Topic);
        Assert.Equal("job", restored.Pending.Newest?.Slot);
        Assert.Equal(5, restored.Turn);
    }

    [Fact]
    public void The_row_alone_forgets_topics_which_is_why_the_snapshot_exists()
    {
        ConversationState state = ChatState.FromPerson(new Person(), Root, 0) with
        {
            Topics = TopicStack.Empty.Advance("music"),
        };

        Person row = new();
        ChatState.ApplyTo(row, state, Root);

        Assert.Empty(ChatState.FromPerson(row, Root, 0).Topics.Entries);
    }

    [Fact]
    public void FromPerson_ignores_jsonb_entries_it_cannot_parse()
    {
        Person row = new()
        {
            Slots = new JsonObject { ["pet"] = "cat", ["broken"] = new JsonArray(1, 2) },
            FiredLog = new JsonObject { ["GREET"] = 3, ["broken"] = "not a turn" },
            ActivityStack = new JsonArray("idle", new JsonObject()),
        };

        ConversationState state = ChatState.FromPerson(row, Root, 0);

        Assert.Equal("cat", state.Slots["pet"]);
        Assert.False(state.Slots.ContainsKey("broken"));
        Assert.Equal(3, state.Fired.LastTurn("GREET"));
        Assert.Null(state.Fired.LastTurn("broken"));
        Assert.Equal("idle", state.Activities.Current);
    }

    private static ConversationState StateWith(string register, double value)
        => ChatState.FromPerson(new Person(), Root, 0) is { } state
            ? state with { Registers = state.Registers.With([new AffectDelta(register, value - state.Registers[register])]) }
            : throw new InvalidOperationException();

    /// <summary>Through JSON, because the hot cache is Redis text, not an object reference.</summary>
    private static ChatStateSnapshot RoundTrip(ChatStateSnapshot snapshot)
        => JsonSerializer.Deserialize<ChatStateSnapshot>(JsonSerializer.Serialize(snapshot, Json), Json)!;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
