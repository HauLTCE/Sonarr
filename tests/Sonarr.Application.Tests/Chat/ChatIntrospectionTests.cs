using Sonarr.Application.Chat;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// <c>/relationship</c> and <c>/memories</c> (docs/07). Two things are load-bearing here: the
/// answer is authored, never a number, and it is read-only — asking her how she feels must not
/// spend a turn or move a register.
/// </summary>
public class ChatIntrospectionTests
{
    private const long Guild = (long)Build.Guild;
    private const long User = (long)Build.User;
    private const long Other = 999L;

    [Fact]
    public async Task Relationship_for_a_stranger_uses_the_lowest_tier_pool()
    {
        FakePersonRepository people = new();

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        // No row at all: trust is the baseline, which is the bottom tier by construction.
        Assert.Contains(line, Pool("relationship_stranger"));
    }

    [Theory]
    // The trust thresholds from persona/sonarr.yaml. A tier the persona moves has to move here.
    [InlineData(0, "relationship_stranger")]
    [InlineData(2.9, "relationship_stranger")]
    [InlineData(3, "relationship_acquaintance")]
    [InlineData(8, "relationship_regular")]
    [InlineData(12.9, "relationship_regular")]
    [InlineData(13, "relationship_favorite")]
    [InlineData(18, "relationship_inner_circle")]
    [InlineData(20, "relationship_inner_circle")]
    [InlineData(-10, "relationship_nemesis")]
    [InlineData(-20, "relationship_nemesis")]
    public async Task Relationship_describes_the_tier_the_trust_value_lands_in(double trust, string pool)
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = trust });

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.Contains(line, Pool(pool));
    }

    [Fact]
    public async Task Her_favorite_is_told_they_are_and_the_runner_up_is_not()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = 15 });
        people.Seed(Guild, Other, p => p.Registers = new PersonRegisters { Trust = 9 });
        ChatIntrospection chat = Build.Introspection(people);

        string top = await chat.DescribeRelationshipAsync(Guild, User, User);
        string second = await chat.DescribeRelationshipAsync(Guild, Other, Other);

        Assert.Contains(Pool(ChatIntrospection.TopStandingPool), top.EndsWith);
        Assert.DoesNotContain(Pool(ChatIntrospection.TopStandingPool), second.EndsWith);
    }

    [Fact]
    public async Task The_least_favorite_hears_about_it_from_the_other_end_of_the_ordering()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = -12 });
        people.Seed(Guild, Other, p => p.Registers = new PersonRegisters { Trust = -4 });

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.Contains(Pool(ChatIntrospection.BottomStandingPool), line.EndsWith);
    }

    [Fact]
    public async Task Topping_a_list_of_one_is_not_a_ranking()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = 15 });

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.Contains(line, Pool("relationship_favorite"));
    }

    [Fact]
    public async Task A_standing_line_never_names_anybody_else()
    {
        // Her favorite being told they're her favorite is fine. Telling them who came second
        // would publish that person's standing to someone it doesn't belong to (docs/06).
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = 15 });
        people.Seed(Guild, Other, p => p.Registers = new PersonRegisters { Trust = 9 });

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.DoesNotContain(Other.ToString(System.Globalization.CultureInfo.InvariantCulture), line, StringComparison.Ordinal);
        Assert.DoesNotContain("<@", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Someone_she_feels_nothing_about_costs_no_ordering_query()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = 1 });

        await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        // Below the threshold there is no list to be at the top of, so there is no query.
        Assert.Equal(0, people.TrustRankReads);
    }

    [Fact]
    public async Task Relationship_never_leaks_a_register_number()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters
        {
            Trust = 17,
            Anger = 8,
            Fondness = 6,
            Grudge = 3,
        });

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.DoesNotContain("17", line, StringComparison.Ordinal);
        Assert.DoesNotContain("trust", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anger", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Relationship_recomputes_the_tier_instead_of_trusting_the_stored_column()
    {
        // The column is written for SQL to read; a stale value must not let her describe a tier
        // her trust has already left.
        FakePersonRepository people = new();
        people.Seed(Guild, User, p =>
        {
            p.Registers = new PersonRegisters { Trust = 13 };
            p.RelationshipTier = "nemesis";
        });

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.Contains(line, Pool("relationship_favorite"));
        Assert.DoesNotContain(line, Pool("relationship_nemesis"));
    }

    [Fact]
    public async Task Relationship_about_someone_else_deflects_instead_of_reporting_their_tier()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, Other, p => p.Registers = new PersonRegisters { Trust = 30 });

        string line = await Build.Introspection(people)
            .DescribeRelationshipAsync(Guild, Other, askerId: User);

        Assert.Contains(line, Pool("relationship_third_party"));
        Assert.DoesNotContain(line, Pool("relationship_favorite"));
    }

    [Fact]
    public async Task Relationship_is_stable_while_her_state_is()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p =>
        {
            p.Registers = new PersonRegisters { Trust = 15 };
            p.LogicalClock = 7;
        });
        ChatIntrospection chat = Build.Introspection(people);

        string first = await chat.DescribeRelationshipAsync(Guild, User, User);
        string second = await chat.DescribeRelationshipAsync(Guild, User, User);

        // Asking twice must not shuffle her opinion; the draw is seeded on her turn counter.
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Relationship_reads_only()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.LogicalClock = 12);

        await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.Empty(people.Writes);
        Assert.Equal(12, (await people.GetAsync(Guild, User))!.LogicalClock);
    }

    [Fact]
    public async Task Memories_of_nobody_gets_the_empty_line_and_no_facts()
    {
        FakePersonRepository people = new();

        MemoryReport report = await Build.Introspection(people).ListMemoriesAsync(Guild, User);

        Assert.Empty(report.Facts);
        Assert.Contains(report.Opener, Pool("recall_empty"));
    }

    [Fact]
    public async Task Memories_lists_her_facts_newest_first_under_an_authored_opener()
    {
        FakePersonRepository people = new();
        people.SeedFact(Guild, User, "job", "barista", turn: 1);
        people.SeedFact(Guild, User, "pet", "a cat called noodle", turn: 5);

        MemoryReport report = await Build.Introspection(people).ListMemoriesAsync(Guild, User);

        Assert.Contains(report.Opener, Pool("memory_retrieve"));
        Assert.Equal(["pet", "job"], report.Facts.Select(f => f.Predicate));
        Assert.Equal("a cat called noodle", report.Facts[0].Value);
    }

    [Fact]
    public async Task Memories_never_returns_another_persons_facts()
    {
        FakePersonRepository people = new();
        people.SeedFact(Guild, Other, "secret", "they hate mondays");

        MemoryReport report = await Build.Introspection(people).ListMemoriesAsync(Guild, User);

        Assert.Empty(report.Facts);
    }

    [Fact]
    public async Task Forget_drops_the_fact_and_says_so()
    {
        FakePersonRepository people = new();
        people.SeedFact(Guild, User, "job", "barista");
        ChatIntrospection chat = Build.Introspection(people);

        string line = await chat.ForgetAsync(Guild, User, "job");

        Assert.Contains(line, Pool("memory_forget"));
        Assert.Empty((await chat.ListMemoriesAsync(Guild, User)).Facts);
    }

    [Fact]
    public async Task Forget_something_she_never_knew_is_not_an_error()
    {
        FakePersonRepository people = new();

        string line = await Build.Introspection(people).ForgetAsync(Guild, User, "birthday");

        Assert.Contains(line, Pool("memory_forget_missing"));
    }

    [Fact]
    public async Task Forget_only_touches_your_own_facts()
    {
        FakePersonRepository people = new();
        people.SeedFact(Guild, Other, "job", "pilot");
        ChatIntrospection chat = Build.Introspection(people);

        string line = await chat.ForgetAsync(Guild, User, "job");

        Assert.Contains(line, Pool("memory_forget_missing"));
        Assert.Single(await people.GetFactsAsync(Guild, Other));
    }

    [Fact]
    public async Task Relationship_adds_a_trajectory_line_for_a_week_that_moved()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = 8 });
        people.SeedEvent(Guild, User, 1.0, Build.Now.AddDays(-2));
        people.SeedEvent(Guild, User, 1.0, Build.Now.AddDays(-1));

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        // Level and trend are both in the answer: where you stand, then which way you're going.
        Assert.Contains(Pool("relationship_regular"), l => line.StartsWith(l, StringComparison.Ordinal));
        Assert.Contains(Pool(ChatIntrospection.TrendUpPool), line.EndsWith);
    }

    [Fact]
    public async Task A_week_of_losing_ground_says_so()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = 8 });
        people.SeedEvent(Guild, User, -2.0, Build.Now.AddHours(-3));

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.Contains(Pool(ChatIntrospection.TrendDownPool), line.EndsWith);
    }

    [Fact]
    public async Task A_flat_week_gets_no_trajectory_line_at_all()
    {
        FakePersonRepository people = new();
        people.Seed(Guild, User, p => p.Registers = new PersonRegisters { Trust = 8 });

        // Movement below the threshold, and movement that happened before the window: neither is
        // a trajectory, and a line about no movement is worse than no line.
        people.SeedEvent(Guild, User, 0.4, Build.Now.AddDays(-1));
        people.SeedEvent(Guild, User, 9.0, Build.Now - ChatIntrospection.TrendWindow.Add(TimeSpan.FromDays(1)));

        string line = await Build.Introspection(people).DescribeRelationshipAsync(Guild, User, User);

        Assert.Contains(line, Pool("relationship_regular"));
    }

    [Fact]
    public void Every_tier_the_persona_declares_has_an_authored_description()
    {
        // The command looks pools up by tier id, so a tier added to sonarr.yaml without a pool
        // would answer with the fallback instead of a description of that tier.
        List<string> missing =
            [.. Build.Graph.Root.Tiers
                .Select(t => ChatIntrospection.TierPoolPrefix + t.Id)
                .Where(pool => !Build.Graph.Pools.ContainsKey(pool))];

        Assert.Empty(missing);
    }

    private static IReadOnlyList<string> Pool(string poolId) => Build.Graph.Pools[poolId].Lines;
}
