using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Application.Tests.Chat;
using Sonarr.Application.Utility;
using Sonarr.Bot.Modules;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Social;

/// <summary>
/// <c>/ship</c>. The whole feature is one hash and one pool draw, so the tests are about the two
/// properties the joke depends on: the number never moves, and it does not care which order you
/// named them in.
/// </summary>
public sealed class ShipMeterTests
{
    private static readonly ShipMeter Meter = new(Build.Persona());

    [Fact]
    public void The_same_pair_always_gets_the_same_number()
        => Assert.Equal(ShipMeter.Percent(111, 222), ShipMeter.Percent(111, 222));

    /// <summary>Asking in the other order is the same question.</summary>
    [Fact]
    public void Order_does_not_matter()
        => Assert.Equal(ShipMeter.Percent(111, 222), ShipMeter.Percent(222, 111));

    [Fact]
    public void Different_pairs_get_different_numbers()
        => Assert.NotEqual(ShipMeter.Percent(111, 222), ShipMeter.Percent(111, 333));

    [Fact]
    public void Every_percentage_is_a_percentage()
    {
        for (long a = 1; a < 60; a++)
        {
            int percent = ShipMeter.Percent(a, a * 7717 + 3);
            Assert.InRange(percent, 0, 100);
        }
    }

    /// <summary>
    /// Snowflakes are large and adjacent — accounts made seconds apart share a long prefix. If the
    /// hash only stirred the low bits, neighbours would land in the same band.
    /// </summary>
    [Fact]
    public void Adjacent_snowflakes_do_not_all_land_together()
    {
        const long baseId = 1_398_000_000_000_000_000;
        HashSet<int> seen = [.. Enumerable.Range(0, 40).Select(i => ShipMeter.Percent(baseId, baseId + 1 + i))];

        Assert.True(seen.Count > 20, $"only {seen.Count} distinct values across 40 neighbours");
    }

    [Fact]
    public void The_verdict_carries_an_authored_line()
    {
        ShipVerdict verdict = Meter.Rate(111, 222);

        Assert.Equal(ShipMeter.Percent(111, 222), verdict.Percent);
        Assert.NotEmpty(verdict.Line);
    }

    /// <summary>
    /// The line has to be as stable as the number — she does not change her mind about arithmetic.
    /// </summary>
    [Fact]
    public void The_line_is_stable_for_a_pair()
        => Assert.Equal(Meter.Rate(111, 222).Line, Meter.Rate(222, 111).Line);

    /// <summary>
    /// Every band the code can select has to be authored, or some pairs get a bare number. Walks
    /// enough pairs to cross all three thresholds.
    /// </summary>
    [Fact]
    public void Every_band_is_authored()
    {
        HashSet<string> bands = [];
        for (long a = 1; a < 200; a++)
        {
            ShipVerdict verdict = Meter.Rate(a, a * 31 + 7);
            Assert.NotEmpty(verdict.Line);
            bands.Add(verdict.Percent <= ShipMeter.LowCeiling ? "low"
                : verdict.Percent <= ShipMeter.MidCeiling ? "mid" : "high");
        }

        Assert.Equal(3, bands.Count);
    }

    /// <summary>The three pools exist under the names the code asks for.</summary>
    [Theory]
    [InlineData(ShipMeter.LowPool)]
    [InlineData(ShipMeter.MidPool)]
    [InlineData(ShipMeter.HighPool)]
    public void The_pool_is_in_the_shipped_persona(string pool)
    {
        Assert.True(Build.Graph.Pools.TryGetValue(pool, out PoolDef? def), pool);
        Assert.NotEmpty(def!.Lines);
    }

    [Theory]
    [InlineData("sonarr", "elaine", "sonine")]
    [InlineData("ab", "cd", "ad")]
    public void The_portmanteau_is_half_and_half(string a, string b, string expected)
        => Assert.Equal(expected, ShipMeter.Name(a, b));

    /// <summary>A name with nothing to cut gets none rather than a one-letter mash.</summary>
    [Theory]
    [InlineData("x", "elaine")]
    [InlineData("sonarr", "!")]
    [InlineData("", "")]
    public void Too_little_to_cut_gets_no_name(string a, string b)
        => Assert.Null(ShipMeter.Name(a, b));

    /// <summary>Emoji-only display names are common and must not produce a mangled title.</summary>
    [Fact]
    public void A_nameless_display_name_gets_no_portmanteau()
        => Assert.Null(ShipMeter.Name("🐟🐟", "elaine"));

    [Fact]
    public async Task The_module_registers_the_command()
    {
        using var rest = new DiscordRestClient();
        using var interactions = new InteractionService(rest);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(Meter)
            .BuildServiceProvider();

        ModuleInfo module = await interactions.AddModuleAsync(typeof(ShipModule), services);

        Assert.Contains(module.SlashCommands, c => c.Name == "ship");
    }
}
