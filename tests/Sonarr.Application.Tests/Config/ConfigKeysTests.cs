using Sonarr.Domain.Configuration;
using Sonarr.Domain.Levels;

namespace Sonarr.Application.Tests.Config;

/// <summary>
/// The closed key catalog is the trust boundary for <c>/config</c>: a value that gets past here
/// becomes a jsonb row, so every kind is checked good and bad.
/// </summary>
public sealed class ConfigKeysTests
{
    [Theory]
    [InlineData(ConfigKeys.WelcomeChannel, "123456789012345678", "123456789012345678")]
    [InlineData(ConfigKeys.LogChannel, "<#123456789012345678>", "123456789012345678")]
    [InlineData(ConfigKeys.AutoroleId, "<@&987654321098765432>", "987654321098765432")]
    [InlineData(ConfigKeys.DjRole, "987654321098765432", "987654321098765432")]
    [InlineData(ConfigKeys.Timezone, "Asia/Ho_Chi_Minh", "Asia/Ho_Chi_Minh")]
    [InlineData(ConfigKeys.LevelupDm, "on", "true")]
    [InlineData(ConfigKeys.LevelupDm, "FALSE", "false")]
    [InlineData(ConfigKeys.XpMultiplier, "3", "3")]
    [InlineData(ConfigKeys.MusicChannel, "  123456789012345678  ", "123456789012345678")]
    [InlineData(LevelsConfigKeys.XpDecay, "yes", "true")]
    // Stored sorted and de-spaced, so the reader never has to care how it was typed.
    [InlineData(LevelsConfigKeys.XpChannelWeights, "456:0, 123:150", "123:150,456:0")]
    [InlineData(LevelsConfigKeys.XpChannelWeights, "123:500", "123:500")]
    public void TryValidate_accepts_and_canonicalises_good_values(string key, string raw, string expected)
    {
        Assert.True(ConfigKeys.TryValidate(key, raw, out var canonical, out var error), error);
        Assert.Equal(expected, canonical);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(ConfigKeys.WelcomeChannel, "general")]
    [InlineData(ConfigKeys.WelcomeChannel, "0")]
    [InlineData(ConfigKeys.LogChannel, "-5")]
    [InlineData(ConfigKeys.AutoroleId, "@everyone")]
    [InlineData(ConfigKeys.Timezone, "Mars/Olympus Mons")]
    [InlineData(ConfigKeys.Timezone, "GMT+7")]
    [InlineData(ConfigKeys.LevelupDm, "maybe")]
    [InlineData(ConfigKeys.XpMultiplier, "99")]
    [InlineData(ConfigKeys.XpMultiplier, "0")]
    [InlineData(ConfigKeys.XpMultiplier, "2.5")]
    [InlineData(LevelsConfigKeys.XpDecay, "sometimes")]
    // A typo'd pair must be an error, not a channel that silently keeps full XP.
    [InlineData(LevelsConfigKeys.XpChannelWeights, "123:150,general:50")]
    [InlineData(LevelsConfigKeys.XpChannelWeights, "123:9000")]
    [InlineData(LevelsConfigKeys.XpChannelWeights, "123:-1")]
    [InlineData(LevelsConfigKeys.XpChannelWeights, "123")]
    public void TryValidate_rejects_bad_values_with_a_reason(string key, string raw)
    {
        Assert.False(ConfigKeys.TryValidate(key, raw, out var canonical, out var error));
        Assert.Empty(canonical);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("nonsense_key")]
    [InlineData("economy_channel")]
    [InlineData("")]
    public void TryValidate_rejects_keys_outside_the_catalog(string key)
    {
        Assert.False(ConfigKeys.TryValidate(key, "123456789012345678", out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryValidate_rejects_an_empty_value()
    {
        Assert.False(ConfigKeys.TryValidate(ConfigKeys.Timezone, "   ", out _, out var error));
        Assert.Contains("needs a value", error);
    }

    [Fact]
    public void TryGet_is_case_insensitive_on_the_key()
    {
        Assert.True(ConfigKeys.TryGet("  LOG_CHANNEL ", out ConfigKeyDefinition? definition));
        Assert.Equal(ConfigKeys.LogChannel, definition.Key);
    }

    [Fact]
    public void All_keys_are_unique_and_lowercase()
    {
        Assert.Equal(ConfigKeys.All.Count, ConfigKeys.All.Select(d => d.Key).Distinct().Count());
        Assert.All(ConfigKeys.All, d => Assert.Equal(d.Key.ToLowerInvariant(), d.Key));
    }
}
