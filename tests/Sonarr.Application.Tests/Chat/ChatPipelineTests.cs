using Sonarr.Application.Chat;
using Sonarr.Domain.Chat;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Conversation;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// The adapter's gate and ordering contract (docs/10): the cheap rejections come before any
/// database read, and the transaction commits before she says anything.
/// </summary>
public sealed class ChatPipelineTests
{
    [Fact]
    public async Task An_ambient_message_gets_no_reply_but_still_feeds_the_ring_buffer()
    {
        FakeSessionCache cache = new();
        FakePersonRepository people = new();

        ChatDecision decision = await Build.Pipeline(people, cache)
            .HandleAsync(Build.Request("just talking to my friend", addressed: false));

        Assert.Equal(ChatDecision.SkipReasons.NotAddressed, decision.Skipped);
        Assert.True(decision.IsSilent);
        Assert.Single(await cache.GetRecentMessagesAsync(Build.Channel));

        // Ambient traffic must not cost a database read on a Pentium.
        Assert.Empty(people.Writes);
    }

    [Fact]
    public async Task The_kill_switch_stops_her_before_the_database()
    {
        FakePersonRepository people = new();

        ChatDecision decision = await Build.Pipeline(people, features: new FakeFeatureGate().Off(FeatureNames.Chat))
            .HandleAsync(Build.Request("hey sonarr"));

        Assert.Equal(ChatDecision.SkipReasons.FeatureOff, decision.Skipped);
        Assert.Empty(people.Writes);
    }

    [Fact]
    public async Task A_spent_engagement_budget_stops_her_before_the_database()
    {
        FakeSessionCache cache = new();
        cache.SeedEngagement(Build.Channel, ChatPipeline.EngagementBudget);
        FakePersonRepository people = new();

        ChatDecision decision = await Build.Pipeline(people, cache).HandleAsync(Build.Request("hey sonarr"));

        Assert.Equal(ChatDecision.SkipReasons.BudgetSpent, decision.Skipped);
        Assert.Empty(people.Writes);
    }

    [Fact]
    public async Task One_below_the_budget_still_gets_a_reply()
    {
        FakeSessionCache cache = new();
        cache.SeedEngagement(Build.Channel, ChatPipeline.EngagementBudget - 1);

        ChatDecision decision = await Build.Pipeline(cache: cache).HandleAsync(Build.Request("hello"));

        Assert.NotEqual(ChatDecision.SkipReasons.BudgetSpent, decision.Skipped);
    }

    [Fact]
    public async Task A_reply_commits_the_turn_and_counts_against_the_budget()
    {
        FakePersonRepository people = new();
        FakeSessionCache cache = new();

        ChatDecision decision = await Build.Pipeline(people, cache).HandleAsync(Build.Request("hello"));

        Assert.NotNull(decision.Text);
        ChatTurnWrite write = Assert.Single(people.Writes);
        Assert.Equal((long)Build.Guild, write.Person.GuildId);
        Assert.Equal(1, write.Person.LogicalClock);
        Assert.Equal(1, cache.Engagement[Build.Channel]);
        Assert.NotNull(await cache.GetSessionAsync(Build.Guild, Build.User));
        Assert.True(cache.HasHotPerson(Build.Guild, Build.User));
    }

    [Fact]
    public async Task She_says_nothing_when_the_commit_fails()
    {
        FakePersonRepository people = new() { FailNextSave = true };
        FakeSessionCache cache = new();

        // She must not claim a memory the transaction lost, so the throw propagates rather than
        // being swallowed into a reply — the gateway handler logs it and drops the message.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build.Pipeline(people, cache).HandleAsync(Build.Request("hello")));

        Assert.False(cache.HasHotPerson(Build.Guild, Build.User));
        Assert.Equal(0, cache.Engagement.GetValueOrDefault(Build.Channel));
    }

    [Fact]
    public async Task A_replied_message_is_marked_so_the_edit_watcher_can_see_a_change()
    {
        FakeSessionCache cache = new();

        ChatDecision decision = await Build.Pipeline(cache: cache).HandleAsync(Build.Request("hello"));

        Assert.Equal(
            decision.StateHash,
            await cache.GetRepliedStateHashAsync(Build.Channel, Build.Message));
    }

    [Fact]
    public async Task The_second_turn_reads_the_hot_cache_instead_of_the_row()
    {
        FakePersonRepository people = new();
        FakeSessionCache cache = new();
        ChatPipeline pipeline = Build.Pipeline(people, cache);

        await pipeline.HandleAsync(Build.Request("hello"));
        await pipeline.HandleAsync(Build.Request("how are you", message: Build.Message + 1));

        // The logical clock advances across turns whichever path loaded the state, which is what
        // makes cooldowns and the fired log behave the same warm or cold.
        Assert.Equal(2, people.Writes[^1].Person.LogicalClock);
    }

    [Fact]
    public async Task A_stale_hot_snapshot_loses_to_the_row()
    {
        FakePersonRepository people = new();
        FakeSessionCache cache = new();
        ChatPipeline pipeline = Build.Pipeline(people, cache);

        await pipeline.HandleAsync(Build.Request("hello"));

        // Another process advanced the row past what the snapshot knows (a restart mid-conversation,
        // or a second bot instance): the row is the authority.
        Person row = (await people.GetAsync((long)Build.Guild, (long)Build.User))!;
        row.LogicalClock = 50;

        await pipeline.HandleAsync(Build.Request("still there", message: Build.Message + 1));

        Assert.Equal(51, people.Writes[^1].Person.LogicalClock);
    }

    [Fact]
    public async Task A_turn_only_stores_her_own_line_as_an_episode()
    {
        FakePersonRepository people = new();
        const string secret = "my password is hunter2";

        ChatDecision decision = await Build.Pipeline(people).HandleAsync(Build.Request(secret));

        Assert.All(people.Episodes, e => Assert.DoesNotContain("hunter2", e.Quote, StringComparison.Ordinal));
        if (decision.Text is not null && people.Episodes.Count > 0)
        {
            Assert.Equal(decision.Text, people.Episodes[0].Quote);
        }
    }

    [Fact]
    public async Task A_repeat_ping_is_noticed_from_the_last_ping_hash()
    {
        FakeSessionCache cache = new();
        FakePersonRepository people = new();
        ChatPipeline pipeline = Build.Pipeline(people, cache);

        await pipeline.HandleAsync(Build.Request("hello"));
        double boredomAfterFirst = Boredom(people.Writes[^1].Person);

        await pipeline.HandleAsync(Build.Request("hello", message: Build.Message + 1));

        // Same content twice: boredom moves up despite a turn of decay pulling it down.
        Assert.True(
            Boredom(people.Writes[^1].Person) > boredomAfterFirst,
            "pinging twice with nothing new should register");
    }

    [Fact]
    public async Task A_name_she_heard_once_is_recalled_hedged_and_a_confirmed_one_is_not()
    {
        FakePersonRepository people = new();
        ChatPipeline pipeline = Build.Pipeline(people, new FakeSessionCache());

        // Said once: chat.fact starts below the hedge threshold, so the recall wears a hedge.
        await pipeline.HandleAsync(Build.Request("my name is Hau"));
        string once = await Recall(pipeline);

        // Said again: UpsertFactAsync reinforces past the threshold and she stops qualifying it.
        await pipeline.HandleAsync(Build.Request("my name is Hau"));
        string twice = await Recall(pipeline);

        Assert.Contains("Hau", once, StringComparison.Ordinal);
        Assert.Contains("Hau", twice, StringComparison.Ordinal);
        Assert.True(IsHedged(once), $"a fact heard once should hedge: {once}");
        Assert.False(IsHedged(twice), $"a confirmed fact should not hedge: {twice}");
    }

    [Fact]
    public async Task A_stranger_costs_no_fact_query()
    {
        FakePersonRepository people = new();

        await Build.Pipeline(people, new FakeSessionCache()).HandleAsync(Build.Request("hey sonarr"));

        // No slots means nothing hedgeable, so the hedge must not add a read on a Pentium.
        Assert.Equal(0, people.FactReads);
    }

    [Fact]
    public async Task A_grudge_cools_by_itself_while_they_stay_away()
    {
        // Two identical people with a held grudge; the only difference is when they were last
        // seen. The pipeline is the only thing that reads a clock, so this is where the wiring
        // from "gone for a week" to ExtraDecaySteps is actually observable.
        static async Task<double> Returning(TimeSpan away)
        {
            FakePersonRepository people = new();
            people.Seed((long)Build.Guild, (long)Build.User, p =>
            {
                p.Registers = new PersonRegisters { Grudge = 8, Trust = -4 };
                p.UpdatedAt = Build.Now - away;
            });

            await Build.Pipeline(people, new FakeSessionCache()).HandleAsync(Build.Request("hey"));
            return people.Writes[^1].Person.Registers.Grudge;
        }

        double sameDay = await Returning(TimeSpan.FromMinutes(5));
        double aWeek = await Returning(TimeSpan.FromDays(7));

        Assert.True(sameDay > 7.5, $"a five-minute gap is one turn of decay: {sameDay}");
        Assert.True(aWeek < sameDay - 1, $"a week away should cool it: {sameDay} → {aWeek}");
    }

    private static async Task<string> Recall(ChatPipeline pipeline)
        => (await pipeline.HandleAsync(Build.Request("what's my name"))).Text ?? string.Empty;

    private static bool IsHedged(string text) =>
        Build.Graph.Pools[ReplyComposer.HedgePool].Lines
            .Select(line => line.Replace("{$value}", string.Empty, StringComparison.Ordinal).Trim())
            .Where(fragment => fragment.Length > 3)
            .Any(fragment => text.Contains(fragment, StringComparison.Ordinal));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_text_means_no_delay(string? text)
    {
        Assert.Equal(TimeSpan.Zero, ChatPipeline.TypingDelayFor(text));
    }

    [Fact]
    public void A_short_reply_still_waits_the_floor()
    {
        Assert.Equal(ChatPipeline.MinTypingDelay, ChatPipeline.TypingDelayFor("k"));
    }

    [Fact]
    public void A_mid_length_reply_scales_with_its_length()
    {
        string text = new('x', 100);

        Assert.Equal(
            text.Length * ChatPipeline.TypingMsPerChar,
            ChatPipeline.TypingDelayFor(text).TotalMilliseconds);
    }

    [Fact]
    public void The_typing_delay_never_exceeds_its_ceiling()
    {
        Assert.Equal(ChatPipeline.MaxTypingDelay, ChatPipeline.TypingDelayFor(new string('x', 10_000)));
    }

    private static double Boredom(Person person)
        => person.Registers.ToDictionary()[Sonarr.Elaine.Conversation.Registers.Names.Boredom];
}
