using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Topic stack, pending-question queue, and fired log — the three pieces that replace
/// single-slot memory and the per-message fired log the old engine reset every turn.
/// </summary>
public class MemoryStateTests
{
    [Fact]
    public void TopicStack_KeepsRecentTopicsInsteadOfOverwritingOne()
    {
        TopicStack topics = TopicStack.Empty.Advance("games").Advance("work");

        Assert.Equal("work", topics.Current);

        // v1 had one slot, so "games" would be gone and a callback had nothing to reach for.
        Assert.True(topics.WeightOf("games") > 0);
    }

    [Fact]
    public void TopicStack_DecaysAndForgetsWhatIsNotMentioned()
    {
        TopicStack topics = TopicStack.Empty.Advance("games");
        for (int i = 0; i < 12; i++)
        {
            topics = topics.Advance(null);
        }

        Assert.Null(topics.Current);
        Assert.Empty(topics.Entries);
    }

    [Fact]
    public void TopicStack_RementionRefreshesToFullWeight()
    {
        TopicStack topics = TopicStack.Empty.Advance("games").Advance("work").Advance("games");

        Assert.Equal("games", topics.Current);
        Assert.Equal(1.0, topics.WeightOf("games"));
    }

    [Fact]
    public void TopicStack_CapsAtFive()
    {
        TopicStack topics = TopicStack.Empty;
        foreach (string topic in new[] { "a", "b", "c", "d", "e", "f", "g" })
        {
            topics = topics.Advance(topic);
        }

        Assert.Equal(TopicStack.Capacity, topics.Entries.Count);
        Assert.Equal("g", topics.Current);
        Assert.Equal(0, topics.WeightOf("a"));
    }

    [Fact]
    public void PendingQuestions_AnsweringClosesTheQuestion()
    {
        PendingQuestions pending = PendingQuestions.Empty.Ask("name", 1);
        Assert.True(pending.Any);
        Assert.False(pending.Answer("name").Any);
    }

    [Fact]
    public void PendingQuestions_UnansweredGoesStaleAndIsReported()
    {
        PendingQuestions pending = PendingQuestions.Empty.Ask("name", 1);

        (PendingQuestions still, IReadOnlyList<PendingQuestion> ignoredEarly) = pending.Expire(2);
        Assert.Empty(ignoredEarly);
        Assert.True(still.Any);

        (PendingQuestions cleared, IReadOnlyList<PendingQuestion> ignored) =
            pending.Expire(1 + PendingQuestions.StaleAfterTurns + 1);
        Assert.False(cleared.Any);
        Assert.Equal("name", Assert.Single(ignored).Slot);
    }

    [Fact]
    public void PendingQuestions_RepeatAskRedatesRatherThanStacks()
    {
        PendingQuestions pending = PendingQuestions.Empty.Ask("name", 1).Ask("name", 9);

        Assert.Equal(9, Assert.Single(pending.Queue).AskedAtTurn);
    }

    [Fact]
    public void PendingQuestions_CapsSoSheDoesNotInterrogate()
    {
        PendingQuestions pending = PendingQuestions.Empty
            .Ask("name", 1).Ask("age", 2).Ask("topic", 3);

        Assert.Equal(PendingQuestions.Capacity, pending.Queue.Count);
        Assert.Equal("topic", pending.Newest?.Slot);
    }

    [Fact]
    public void FiredLog_OncePersistsAcrossTurns()
    {
        // The bug docs/10 names: the old log lived on the message context, so `once:` meant
        // "once per message" and never actually held.
        IntentDef once = Intent("GREET_FIRST", once: true);
        FiredLog log = FiredLog.Empty.Record(once.Id, 1);

        Assert.False(log.IsEligible(once, 2));
        Assert.False(log.IsEligible(once, 5_000));
    }

    [Fact]
    public void FiredLog_CooldownCountsLogicalTurns()
    {
        IntentDef joke = Intent("JOKE", cooldown: 5);
        FiredLog log = FiredLog.Empty.Record(joke.Id, 10);

        Assert.False(log.IsEligible(joke, 14));
        Assert.True(log.IsEligible(joke, 15));
    }

    [Fact]
    public void FiredLog_UnfiredIntentIsAlwaysEligible()
    {
        Assert.True(FiredLog.Empty.IsEligible(Intent("X", once: true), 1));
    }

    [Fact]
    public void FiredLog_EvictsTheOldestOnceFull()
    {
        FiredLog log = FiredLog.Empty;
        for (int i = 0; i <= FiredLog.MaxEntries; i++)
        {
            log = log.Record($"intent{i}", i);
        }

        Assert.Equal(FiredLog.MaxEntries, log.Entries.Count);
        Assert.False(log.HasFired("intent0"));
        Assert.True(log.HasFired($"intent{FiredLog.MaxEntries}"));
    }

    private static IntentDef Intent(string id, bool once = false, int cooldown = 0) => new()
    {
        Id = id,
        DeclarationIndex = 0,
        Patterns = [],
        Pool = "neutral_statement",
        Once = once,
        Cooldown = cooldown,
        Location = new PersonaLocation("test", ""),
    };
}
