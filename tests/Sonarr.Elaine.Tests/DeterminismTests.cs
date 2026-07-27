using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Same state + same input + same turn must produce an identical reply, forever. Without
/// this a support report ("she said something weird") is not reproducible.
/// </summary>
public class DeterminismTests
{
    private const int Runs = 100;

    [Fact]
    public void SameTurnAndInput_ProducesTheIdenticalLine_AcrossManyRuns()
    {
        PersonaGraph graph = SeedPersona.Graph;
        LexicalMatcher matcher = new(graph);
        Normalized input = Normalizer.Normalize("hey there, you're an idiot");
        MatchContext context = MatchContext.Empty with { ModeId = "ANNOYED", TierId = "regular" };

        string Reply(long turn)
        {
            MatchOutcome outcome = matcher.Match(input, context);
            IntentDef intent = outcome.Primary!.Intent;
            PoolDef pool = graph.Pools[intent.Pool];
            IDeterministicRandom rng = new TurnSeededRandom(turn, salt: 0xC0FFEE);
            return $"{intent.Id}|{rng.Pick(intent.Pool, pool.For(context.ModeId))}";
        }

        string expected = Reply(42);
        for (int i = 0; i < Runs; i++)
        {
            Assert.Equal(expected, Reply(42));
        }

        // And a different turn must be free to differ, or she repeats herself forever.
        List<string> overTime = [.. Enumerable.Range(0, 20).Select(t => Reply(t))];
        Assert.True(overTime.Distinct().Count() > 1, "every turn produced the same line");
    }

    [Fact]
    public void DrawsAreIndependentOfCallOrder()
    {
        // A stateful Random would make each draw depend on how many came before it, so
        // adding one unrelated lookup would silently change every later line in the turn.
        TurnSeededRandom rng = new(7);
        int a = rng.Next("greeting", 100);
        int b = rng.Next("mood", 100);

        TurnSeededRandom same = new(7);
        Assert.Equal(b, same.Next("mood", 100));
        Assert.Equal(a, same.Next("greeting", 100));
    }

    [Fact]
    public void DifferentSalts_DivergeOnTheSameTurn()
    {
        // Two users on the same turn should not hear the same line.
        List<int> a = [.. Enumerable.Range(0, 30)
            .Select(t => new TurnSeededRandom(t, salt: 1).Next("pool", 50))];
        List<int> b = [.. Enumerable.Range(0, 30)
            .Select(t => new TurnSeededRandom(t, salt: 2).Next("pool", 50))];

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Advance_KeepsTheSaltAndMovesTheTurn()
    {
        TurnSeededRandom rng = new(5, salt: 99);
        TurnSeededRandom next = rng.Advance();

        Assert.Equal(6, next.Turn);
        Assert.Equal(new TurnSeededRandom(6, salt: 99).Next("x", 1000), next.Next("x", 1000));
    }

    [Fact]
    public void DrawsStayInRange()
    {
        for (int t = 0; t < 200; t++)
        {
            TurnSeededRandom rng = new(t);
            int n = rng.Next("p", 7);
            Assert.InRange(n, 0, 6);
            Assert.InRange(rng.NextInclusive("p", -3, 3), -3, 3);
        }
    }

    [Fact]
    public void StableHash_DoesNotVaryLikeStringGetHashCode()
    {
        // string.GetHashCode() is randomized per process, so it can never appear on a
        // determinism path. This value is pinned: change it and stored traces stop replaying.
        Assert.Equal(StableHash.Of("greeting"), StableHash.Of("greeting"));
        Assert.NotEqual(StableHash.Of("greeting"), StableHash.Of("greetinh"));
        Assert.Equal(0xCBF29CE484222325UL, StableHash.Of(""));
    }

    [Fact]
    public void Pick_RejectsAnEmptyPool()
    {
        TurnSeededRandom rng = new(1);
        Assert.Throws<ArgumentException>(() => rng.Pick<string>("p", []));
    }

    [Fact]
    public void FixedClock_IsTheOnlyWayTimeEntersTheEngine()
    {
        DateTimeOffset when = new(2026, 10, 31, 3, 15, 0, TimeSpan.Zero);
        IClock clock = new FixedClock(when);
        Assert.Equal(when, clock.UtcNow);
        Assert.Equal(when, clock.UtcNow);
    }
}
