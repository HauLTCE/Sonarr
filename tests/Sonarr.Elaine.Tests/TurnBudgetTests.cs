using System.Diagnostics;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The lexical turn has to be cheap, because it is the half of the ~50 ms budget that is
/// <em>not</em> the embedding (docs/checklist.md, "worst-case budget check").
/// </summary>
/// <remarks>
/// The semantic tier's own ~10–50 ms lands in Phase 4 and gets benchmarked on the J2900
/// itself; what this pins is that the lexical pass leaves room for it. The ceiling is
/// deliberately loose — a shared CI runner is not a J2900, and a perf test that fails on
/// a noisy neighbour teaches people to ignore it.
/// </remarks>
public class TurnBudgetTests
{
    /// <summary>Every intent's patterns run on every turn, so the worst case is a full miss.</summary>
    private const string WorstCase =
        "so anyway i was thinking about the quarterly logistics report and whether the "
        + "throughput numbers actually justify the extra warehouse capacity we leased";

    private const int Turns = 200;

    /// <summary>Generous enough for a cold shared runner, tight enough to catch a regression.</summary>
    private const double CeilingMs = 5.0;

    [Fact]
    public void AFullMissStaysWellUnderTheLexicalHalfOfTheBudget()
    {
        PersonaGraph graph = SeedPersona.Graph;
        ChatEngine engine = new(graph);
        ConversationState fresh = ConversationState.Fresh(graph.Root, 7);
        TurnInput input = new() { Text = WorstCase };

        engine.Turn(fresh, input); // JIT and the regex cache warm on the first call, not the measured ones.

        Stopwatch clock = Stopwatch.StartNew();
        for (int i = 0; i < Turns; i++)
        {
            engine.Turn(fresh, input);
        }

        double perTurn = clock.Elapsed.TotalMilliseconds / Turns;
        Assert.True(perTurn < CeilingMs, $"{perTurn:F2} ms per turn, ceiling {CeilingMs} ms");
    }
}
