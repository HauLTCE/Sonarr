using Sonarr.Domain.Utility;

namespace Sonarr.Application.Tests.Utility;

/// <summary>
/// Timezone handling that works with ICU stripped. <c>InvariantGlobalization=true</c> means
/// <c>TimeZoneInfo.FindSystemTimeZoneById("Europe/London")</c> fails on a Windows dev box and
/// succeeds in the Linux container, so <c>/timezone</c> validates the <b>shape</b> of an id and
/// autocompletes from a curated list. These tests assert exactly that, and never resolve an id.
/// </summary>
public sealed class IanaIdTests
{
    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Ho_Chi_Minh")]
    [InlineData("Europe/London")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("Etc/GMT+7")]
    public void Accepts_a_well_shaped_id(string id) => Assert.True(IanaId.LooksValid(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("London")]
    [InlineData("Mars/Olympus")]
    [InlineData("Europe/")]
    [InlineData("Europe/London/Extra/Bits")]
    [InlineData("Europe/Lon don")]
    [InlineData("GMT+7")]
    [InlineData("Pacific Standard Time")]
    public void Rejects_anything_else(string? id) => Assert.False(IanaId.LooksValid(id));

    [Fact]
    public void Autocomplete_offers_the_curated_list_when_nothing_is_typed()
    {
        IReadOnlyList<string> matches = IanaId.Match(string.Empty);

        Assert.NotEmpty(matches);
        // Discord's hard cap on autocomplete choices.
        Assert.True(matches.Count <= 25, $"{matches.Count} suggestions is more than Discord accepts");
    }

    [Fact]
    public void Autocomplete_filters_case_insensitively()
    {
        Assert.Contains("Asia/Ho_Chi_Minh", IanaId.Match("ho_chi"));
        Assert.Contains("Europe/London", IanaId.Match("LONDON"));
    }

    [Fact]
    public void Autocomplete_never_returns_more_than_discord_allows()
        => Assert.True(IanaId.Match("a").Count <= 25);

    [Fact]
    public void An_unlisted_but_well_shaped_id_is_still_offered_back()
    {
        // The curated list can't hold all ~600 zones, so a valid id nobody listed stays settable.
        IReadOnlyList<string> matches = IanaId.Match("Antarctica/Rothera");

        Assert.Equal(["Antarctica/Rothera"], matches);
    }

    [Fact]
    public void A_malformed_guess_produces_no_suggestions()
        => Assert.Empty(IanaId.Match("qwertyuiop"));

    [Fact]
    public void Every_curated_suggestion_passes_its_own_validator()
    {
        foreach (var id in IanaId.Suggestions)
        {
            Assert.True(IanaId.LooksValid(id), id);
        }
    }
}
