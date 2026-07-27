using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sonarr.Bot.Configuration;

namespace Sonarr.Application.Tests.Configuration;

/// <summary>
/// Boot must fail loudly on bad config, not on the first Discord call
/// (docs/11-deployment.md).
/// </summary>
public sealed class SonarrOptionsSetupTests
{
    private static readonly Dictionary<string, string?> Valid = new()
    {
        ["DISCORD_TOKEN"] = new string('t', 60),
        ["LAVALINK_URI"] = "http://127.0.0.1:2333",
        ["LAVALINK_PASSWORD"] = "youshallnotpass",
        ["PG_CONNECTION"] = "Host=localhost;Database=sonarr;Username=sonarr;Password=x",
        ["REDIS_CONNECTION"] = "localhost:6379",
        ["ADMIN_USER_IDS"] = "123456789012345678",
        ["PANEL_BASE_URL"] = "https://sonarr.hault.io.vn",
    };

    private static SonarrOptions Bind(Action<Dictionary<string, string?>>? mutate = null)
    {
        var values = new Dictionary<string, string?>(Valid);
        mutate?.Invoke(values);
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddSonarrOptions(config);
    }

    [Fact]
    public void Binds_a_complete_environment()
    {
        var options = Bind();

        Assert.Equal("localhost:6379", options.RedisConnection);
        Assert.Equal(5088, options.ApiPort);
        Assert.Equal([123456789012345678UL], options.AdminUserIds);
        Assert.Null(options.DiscordDevGuildId);
    }

    [Theory]
    [InlineData("DISCORD_TOKEN")]
    [InlineData("LAVALINK_URI")]
    [InlineData("LAVALINK_PASSWORD")]
    [InlineData("PG_CONNECTION")]
    [InlineData("REDIS_CONNECTION")]
    [InlineData("PANEL_BASE_URL")]
    [InlineData("ADMIN_USER_IDS")]
    public void Missing_required_value_fails_boot(string key)
        => Assert.Throws<OptionsValidationException>(() => Bind(v => v.Remove(key)));

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("ws://127.0.0.1:2333")]
    public void Rejects_a_lavalink_uri_it_cannot_call(string uri)
        => Assert.Throws<OptionsValidationException>(() => Bind(v => v["LAVALINK_URI"] = uri));

    [Fact]
    public void Rejects_a_token_too_short_to_be_real()
        => Assert.Throws<OptionsValidationException>(() => Bind(v => v["DISCORD_TOKEN"] = "abc123"));

    [Fact]
    public void Validation_message_never_echoes_the_token()
    {
        var ex = Assert.Throws<OptionsValidationException>(() => Bind(v => v["DISCORD_TOKEN"] = "abc123"));

        Assert.DoesNotContain("abc123", ex.Message, StringComparison.Ordinal);
        Assert.Contains("DISCORD_TOKEN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_ids_are_parsed_deduplicated_and_junk_dropped()
    {
        var options = Bind(v => v["ADMIN_USER_IDS"] = " 111, 222 ,111, , nonsense ,0 ");

        Assert.Equal([111UL, 222UL], options.AdminUserIds);
    }

    [Fact]
    public void Dev_guild_id_scopes_command_registration()
    {
        var options = Bind(v => v["DISCORD_DEV_GUILD_ID"] = "987654321098765432");

        Assert.Equal(987654321098765432UL, options.DiscordDevGuildId);
    }
}
