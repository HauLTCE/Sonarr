using Sonarr.Domain.Levels;

namespace Sonarr.Application.Tests.Levels;

/// <summary>
/// The curve carried over from the Python bot: a level costs <c>5L² + 50L + 100</c> XP, XP is
/// never reset, and levels start at 1. Every legacy row has to land on the same level here.
/// </summary>
public sealed class LevelCurveTests
{
    [Fact]
    public void Levels_start_at_one_with_no_xp()
        => Assert.Equal(LevelCurve.FirstLevel, LevelCurve.LevelFor(0));

    [Theory]
    [InlineData(1, 155)]
    [InlineData(2, 220)]
    [InlineData(3, 295)]
    [InlineData(10, 1100)]
    public void ThresholdFor_matches_the_legacy_formula(int level, long expected)
        => Assert.Equal(expected, LevelCurve.ThresholdFor(level));

    [Fact]
    public void LevelFor_promotes_exactly_at_the_threshold_and_not_one_xp_before()
    {
        var cost = LevelCurve.ThresholdFor(LevelCurve.FirstLevel);

        Assert.Equal(1, LevelCurve.LevelFor(cost - 1));
        Assert.Equal(2, LevelCurve.LevelFor(cost));
    }

    [Fact]
    public void LevelFor_never_goes_down_as_xp_goes_up()
    {
        var last = LevelCurve.FirstLevel;
        for (long xp = 0; xp <= 20_000; xp += 37)
        {
            var level = LevelCurve.LevelFor(xp);
            Assert.True(level >= last, $"level dropped from {last} to {level} at {xp} XP");
            last = level;
        }
    }

    [Fact]
    public void Progress_splits_total_xp_into_this_level_only()
    {
        var firstLevel = LevelCurve.ThresholdFor(1);
        var xp = firstLevel + 40;

        LevelCurve.Progress(xp, LevelCurve.LevelFor(xp), out var into, out var span);

        Assert.Equal(40, into);

        // A span is the distance between two cumulative thresholds, not a threshold itself.
        Assert.Equal(LevelCurve.ThresholdFor(2) - firstLevel, span);
    }

    [Fact]
    public void Progress_of_a_brand_new_member_is_zero_over_the_first_level_cost()
    {
        LevelCurve.Progress(0, LevelCurve.FirstLevel, out var into, out var span);

        Assert.Equal(0, into);
        Assert.Equal(LevelCurve.ThresholdFor(LevelCurve.FirstLevel), span);
        Assert.Equal(0d, LevelCurve.Fraction(0, LevelCurve.FirstLevel));
    }

    [Fact]
    public void Fraction_stays_inside_zero_and_one()
    {
        for (long xp = 0; xp <= 5_000; xp += 13)
        {
            var fraction = LevelCurve.Fraction(xp, LevelCurve.LevelFor(xp));
            Assert.InRange(fraction, 0d, 1d);
        }
    }
}
