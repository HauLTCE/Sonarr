using Sonarr.Bot.Discord;

namespace Sonarr.Application.Tests.Health;

/// <summary>
/// docs/07-commands.md#design-rules: input is validated at the controller layer and a failure
/// is one friendly line. Every guard returns <c>null</c> for "fine".
/// </summary>
public sealed class InputGuardsTests
{
    [Fact]
    public void BelongsToGuild_accepts_an_entity_from_this_guild()
        => Assert.Null(InputGuards.BelongsToGuild(42UL, 42UL, "channel"));

    [Fact]
    public void BelongsToGuild_rejects_an_entity_from_elsewhere()
    {
        var problem = InputGuards.BelongsToGuild(1UL, 2UL, "channel");

        Assert.NotNull(problem);
        Assert.Contains("channel", problem);
        Assert.DoesNotContain("1", problem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(75)]
    [InlineData(150)]
    public void InRange_accepts_the_inclusive_bounds_and_the_middle(long value)
        => Assert.Null(InputGuards.InRange(value, 0, 150, "volume"));

    [Theory]
    [InlineData(-1)]
    [InlineData(151)]
    public void InRange_rejects_values_outside_the_bounds(long value)
    {
        var problem = InputGuards.InRange(value, 0, 150, "volume");

        Assert.NotNull(problem);
        Assert.StartsWith("Volume", problem);
        Assert.Contains("between 0 and 150", problem);
    }

    [Fact]
    public void Length_accepts_text_within_the_limit()
        => Assert.Null(InputGuards.Length("being rude in general", 200, "reason"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Length_rejects_blank_input(string? value)
        => Assert.NotNull(InputGuards.Length(value, 200, "reason"));

    [Fact]
    public void Length_measures_the_trimmed_value()
    {
        // 200 characters plus padding: trimming is what keeps this inside the limit.
        var text = "  " + new string('x', 200) + "  ";

        Assert.Null(InputGuards.Length(text, 200, "reason"));
    }

    [Fact]
    public void Length_rejects_text_over_the_limit_and_says_how_long_it_is()
    {
        var problem = InputGuards.Length(new string('x', 201), 200, "reason");

        Assert.NotNull(problem);
        Assert.Contains("200 characters max", problem);
        Assert.Contains("201", problem);
    }

    [Fact]
    public void Length_enforces_a_custom_minimum()
    {
        Assert.NotNull(InputGuards.Length("ab", 32, "playlist name", min: 3));
        Assert.Null(InputGuards.Length("abc", 32, "playlist name", min: 3));
    }

    [Fact]
    public void TimeZone_accepts_utc_everywhere()
    {
        var problem = InputGuards.TimeZone("UTC", out var zone);

        Assert.Null(problem);
        Assert.NotNull(zone);
    }

    /// <summary>
    /// Which ids exist is the host's business: prod is Linux with tzdata (IANA names), a Windows
    /// dev box under <c>InvariantGlobalization</c> only has Windows ids. The guard's contract is
    /// "whatever this platform installed resolves", so the test asks the platform.
    /// </summary>
    [Fact]
    public void TimeZone_accepts_every_id_the_platform_installed()
    {
        foreach (var installed in TimeZoneInfo.GetSystemTimeZones().Take(5))
        {
            Assert.Null(InputGuards.TimeZone(installed.Id, out var zone));
            Assert.NotNull(zone);
        }
    }

    [Fact]
    public void TimeZone_trims_before_resolving()
    {
        Assert.Null(InputGuards.TimeZone("  UTC  ", out var zone));
        Assert.NotNull(zone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("GMT+7")]
    public void TimeZone_rejects_anything_the_platform_cannot_resolve(string? id)
    {
        var problem = InputGuards.TimeZone(id, out var zone);

        Assert.NotNull(problem);
        Assert.Null(zone);
    }
}
