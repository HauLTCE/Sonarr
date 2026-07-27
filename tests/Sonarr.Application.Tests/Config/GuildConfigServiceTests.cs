using Sonarr.Domain.Configuration;

namespace Sonarr.Application.Tests.Config;

/// <summary>
/// The service owns validation and cache invalidation — "changes apply immediately"
/// (docs/04-database.md#coreguild_config) only holds if every write drops the cached blob.
/// </summary>
public sealed class GuildConfigServiceTests
{
    [Fact]
    public async Task SetAsync_writes_the_row_and_invalidates_the_cache()
    {
        (var service, FakeGuildConfigRepository repo, FakeConfigCache cache) = Build.ConfigService();

        // Warm the cache first, so the invalidation has something to drop.
        await service.GetAllAsync(Build.Guild);

        ConfigWriteResult result = await service.SetAsync(
            Build.Guild, ConfigKeys.LogChannel, "<#123456789012345678>", Build.Actor);

        Assert.True(result.Success);
        Assert.Equal([ConfigKeys.LogChannel], repo.WrittenKeys);
        Assert.Equal(1, cache.GuildInvalidations);

        ConfigValue? stored = await service.GetAsync(Build.Guild, ConfigKeys.LogChannel);
        Assert.Equal(123456789012345678UL, stored?.AsSnowflake);
    }

    [Fact]
    public async Task SetAsync_rejects_a_bad_value_without_touching_the_repository()
    {
        (var service, FakeGuildConfigRepository repo, FakeConfigCache cache) = Build.ConfigService();

        ConfigWriteResult result = await service.SetAsync(
            Build.Guild, ConfigKeys.Timezone, "Mars/Olympus", Build.Actor);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
        Assert.Empty(repo.WrittenKeys);
        Assert.Equal(0, cache.GuildInvalidations);
    }

    [Fact]
    public async Task SetAsync_rejects_a_key_outside_the_catalog()
    {
        (var service, FakeGuildConfigRepository repo, _) = Build.ConfigService();

        ConfigWriteResult result = await service.SetAsync(Build.Guild, "economy_channel", "1", Build.Actor);

        Assert.False(result.Success);
        Assert.Empty(repo.WrittenKeys);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unset_key()
        => Assert.Null(await Build.ConfigService().Service.GetAsync(Build.Guild, ConfigKeys.DjRole));

    [Fact]
    public async Task ClearAsync_removes_the_row_and_invalidates_the_cache()
    {
        (var service, _, FakeConfigCache cache) = Build.ConfigService();
        await service.SetAsync(Build.Guild, ConfigKeys.XpMultiplier, "2", Build.Actor);

        ConfigWriteResult result = await service.ClearAsync(Build.Guild, ConfigKeys.XpMultiplier, Build.Actor);

        Assert.True(result.Success);
        Assert.Null(await service.GetAsync(Build.Guild, ConfigKeys.XpMultiplier));
        Assert.Equal(2, cache.GuildInvalidations);
    }

    [Fact]
    public async Task GetAllAsync_serves_the_second_read_from_the_cache()
    {
        (var service, FakeGuildConfigRepository repo, FakeConfigCache cache) = Build.ConfigService();
        await service.SetAsync(Build.Guild, ConfigKeys.LevelupDm, "yes", Build.Actor);

        IReadOnlyDictionary<string, ConfigValue> first = await service.GetAllAsync(Build.Guild);
        IReadOnlyDictionary<string, ConfigValue> second = await service.GetAllAsync(Build.Guild);

        Assert.True(first[ConfigKeys.LevelupDm].AsBoolean);
        Assert.True(second[ConfigKeys.LevelupDm].AsBoolean);
        Assert.Equal(1, repo.GetAllCalls);
        Assert.Equal(1, cache.GuildInvalidations);
    }

    [Fact]
    public async Task ExportAsync_round_trips_through_ImportAsync()
    {
        (var source, _, _) = Build.ConfigService();
        await source.SetAsync(Build.Guild, ConfigKeys.WelcomeChannel, "123456789012345678", Build.Actor);
        await source.SetAsync(Build.Guild, ConfigKeys.Timezone, "Asia/Ho_Chi_Minh", Build.Actor);

        var json = await source.ExportAsync(Build.Guild);

        (var target, _, _) = Build.ConfigService();
        ConfigImportResult result = await target.ImportAsync(Build.Guild, json, Build.Actor);

        Assert.True(result.Applied);
        Assert.Equal(2, result.KeyCount);
        Assert.Equal("Asia/Ho_Chi_Minh", (await target.GetAsync(Build.Guild, ConfigKeys.Timezone))?.Raw);
    }

    [Fact]
    public async Task ImportAsync_rejects_wholesale_when_one_value_is_bad()
    {
        (var service, FakeGuildConfigRepository repo, FakeConfigCache cache) = Build.ConfigService();

        const string json = """
            {
              "welcome_channel": "123456789012345678",
              "timezone": "Mars/Olympus",
              "xp_multiplier": 2
            }
            """;

        ConfigImportResult result = await service.ImportAsync(Build.Guild, json, Build.Actor);

        Assert.False(result.Applied);
        Assert.Single(result.Rejections);
        // The good keys must not have landed — a half-applied config is the failure mode we care about.
        Assert.Empty(repo.WrittenKeys);
        Assert.Equal(0, cache.GuildInvalidations);
        Assert.Null(await service.GetAsync(Build.Guild, ConfigKeys.WelcomeChannel));
    }

    [Fact]
    public async Task ImportAsync_reports_every_rejection_not_just_the_first()
    {
        (var service, _, _) = Build.ConfigService();

        const string json = """
            {"timezone": "Mars/Olympus", "xp_multiplier": 99, "not_a_key": "x"}
            """;

        ConfigImportResult result = await service.ImportAsync(Build.Guild, json, Build.Actor);

        Assert.False(result.Applied);
        Assert.Equal(3, result.Rejections.Count);
    }

    [Fact]
    public async Task ImportAsync_accepts_json_numbers_and_booleans()
    {
        (var service, _, _) = Build.ConfigService();

        ConfigImportResult result = await service.ImportAsync(
            Build.Guild, """{"xp_multiplier": 4, "levelup_dm": true}""", Build.Actor);

        Assert.True(result.Applied);
        Assert.Equal(4, (await service.GetAsync(Build.Guild, ConfigKeys.XpMultiplier))?.AsInteger);
        Assert.True((await service.GetAsync(Build.Guild, ConfigKeys.LevelupDm))?.AsBoolean);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("")]
    public async Task ImportAsync_rejects_input_that_is_not_a_json_object(string json)
    {
        (var service, FakeGuildConfigRepository repo, _) = Build.ConfigService();

        ConfigImportResult result = await service.ImportAsync(Build.Guild, json, Build.Actor);

        Assert.False(result.Applied);
        Assert.NotEmpty(result.Rejections);
        Assert.Empty(repo.WrittenKeys);
    }
}
