using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The persisted state pieces: activity stack, registers, topic stack, pending questions,
/// fired log. Each one exists to fix a specific old-engine bug, so each gets a test that
/// fails if the bug comes back.
/// </summary>
public class ConversationStateTests
{
    private static PersonaRoot Root => SeedPersona.Graph.Root;

    [Fact]
    public void ActivityStack_PopRestoresWhatWasUnderneath()
    {
        // The whole point of a stack: quitting rps must not lose the argument.
        ActivityStack stack = ActivityStack.StartingAt("idle").Push("argument").Push("rps");

        Assert.Equal("rps", stack.Current);
        Assert.Equal("argument", stack.Pop().Current);
        Assert.Equal("idle", stack.Pop().Pop().Current);
    }

    [Fact]
    public void ActivityStack_NeverEmptiesAndNeverGrowsPastMaxDepth()
    {
        Assert.Equal("idle", ActivityStack.StartingAt("idle").Pop().Pop().Current);

        ActivityStack deep = ActivityStack.StartingAt("a");
        for (int i = 0; i < ActivityStack.MaxDepth * 2; i++)
        {
            deep = deep.Push($"layer{i}");
        }

        Assert.Equal(ActivityStack.MaxDepth, deep.Depth);
        Assert.Equal($"layer{(ActivityStack.MaxDepth * 2) - 1}", deep.Current);
    }

    [Fact]
    public void ActivityStack_RepushingTheCurrentActivityIsANoOp()
    {
        ActivityStack stack = ActivityStack.StartingAt("idle").Push("rps");
        Assert.Equal(2, stack.Push("rps").Depth);
    }

    [Fact]
    public void ActivityStack_RestoreFallsBackToStartOnGarbage()
    {
        Assert.Equal("idle", ActivityStack.Restore(null, "idle").Current);
        Assert.Equal("idle", ActivityStack.Restore([], "idle").Current);
        Assert.Equal("idle", ActivityStack.Restore(["", "  "], "idle").Current);
    }

    [Fact]
    public void Registers_AreOrthogonal_AngryAndFondAtOnce()
    {
        // Unrepresentable in the old FSM, which is why every mood combination needed a state.
        Registers registers = Registers.FromBaselines(Root)
            .With([new AffectDelta("anger", 8), new AffectDelta("trust", 20)]);

        Assert.Equal(8, registers["anger"]);
        Assert.Equal(Registers.Max, registers["trust"]);
    }

    [Fact]
    public void Registers_DecayMovesTowardBaselineAndStops()
    {
        Registers hot = Registers.FromBaselines(Root).With([new AffectDelta("anger", 10)]);
        double baseline = Root.Personality.Baselines["anger"];

        Registers cooled = hot.Decay(Root, 1);
        Assert.True(cooled["anger"] < hot["anger"]);

        // Many steps at once (a week away) lands exactly on the baseline, never past it.
        Assert.Equal(baseline, hot.Decay(Root, 500)["anger"]);
    }

    [Fact]
    public void Registers_UnknownRegisterReadsAsZeroInsteadOfThrowing()
    {
        Assert.Equal(0, Registers.Empty["not_a_register"]);
    }

    [Fact]
    public void ModeSelector_FallsBackToTheAlwaysMode()
    {
        ModeDef mode = ModeSelector.Select(Root, Registers.FromBaselines(Root));
        Assert.True(Root.Modes.Single(m => m.Always).Id == mode.Id);
    }

    [Fact]
    public void ModeSelector_RagePicksTheFirstMatchingModeInDeclaredOrder()
    {
        Registers seething = Registers.FromBaselines(Root)
            .With([new AffectDelta("anger", 10)]);
        Assert.Equal("SEETHING", ModeSelector.Select(Root, seething).Id);
    }

    [Fact]
    public void ModeSelector_TierPicksTheHighestTrustThresholdMet()
    {
        Assert.Equal("stranger", ModeSelector.SelectTier(Root, 0)?.Id);
        Assert.Equal("regular", ModeSelector.SelectTier(Root, 8)?.Id);
        Assert.Equal("inner_circle", ModeSelector.SelectTier(Root, 99)?.Id);

        // Being disliked is a tier, not the absence of one.
        Assert.Equal("nemesis", ModeSelector.SelectTier(Root, -10)?.Id);

        // Every tier threshold has to sit inside the clamp, or it is authored text nobody can
        // ever reach: the top of the ladder must be selectable at Registers.Max.
        Assert.Equal(
            Root.Tiers.OrderByDescending(t => t.MinTrust).First().Id,
            ModeSelector.SelectTier(Root, Registers.Max)?.Id);

        // Trust clamps at Registers.Min (-20), below every authored min_trust. "Highest met"
        // alone leaves the people who hate her most with no tier at all — no description to
        // read and no guard that admits them — so the bottom tier is the floor.
        Assert.Equal("nemesis", ModeSelector.SelectTier(Root, Registers.Min)?.Id);
    }
}
