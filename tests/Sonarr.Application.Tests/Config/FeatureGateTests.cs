using Sonarr.Domain.Configuration;

namespace Sonarr.Application.Tests.Config;

/// <summary>
/// Kill-switch precedence: the guild row overrides the global (guild_id = 0) row, and a feature
/// with no row anywhere is on (docs/checklist.md — "Kill switches &amp; health").
/// </summary>
public sealed class FeatureGateTests
{
    [Fact]
    public async Task IsEnabledAsync_defaults_on_when_no_row_exists()
    {
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository());

        Assert.True(await gate.IsEnabledAsync(FeatureNames.Music, Build.Guild));
    }

    [Fact]
    public async Task IsEnabledAsync_falls_back_to_the_global_row()
    {
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository().With(0, FeatureNames.Music, false));

        Assert.False(await gate.IsEnabledAsync(FeatureNames.Music, Build.Guild));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task IsEnabledAsync_lets_the_guild_row_override_the_global_row(bool global, bool guild)
    {
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository()
            .With(0, FeatureNames.Levels, global)
            .With((long)Build.Guild, FeatureNames.Levels, guild));

        Assert.Equal(guild, await gate.IsEnabledAsync(FeatureNames.Levels, Build.Guild));
    }

    [Fact]
    public async Task IsEnabledAsync_keeps_another_guilds_row_out_of_it()
    {
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository().With(999, FeatureNames.Chat, false));

        Assert.True(await gate.IsEnabledAsync(FeatureNames.Chat, Build.Guild));
    }

    [Fact]
    public async Task GetAllAsync_reports_every_catalog_feature_with_its_source()
    {
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository()
            .With(0, FeatureNames.Tickets, false)
            .With((long)Build.Guild, FeatureNames.Music, false));

        IReadOnlyList<FeatureState> states = await gate.GetAllAsync(Build.Guild);

        Assert.Equal(FeatureNames.All.Count, states.Count);
        Assert.Equal(FeatureStateSource.Guild, states.Single(s => s.Feature == FeatureNames.Music).Source);
        Assert.Equal(FeatureStateSource.Global, states.Single(s => s.Feature == FeatureNames.Tickets).Source);
        Assert.Equal(FeatureStateSource.Default, states.Single(s => s.Feature == FeatureNames.Levels).Source);
        Assert.True(states.Single(s => s.Feature == FeatureNames.Levels).Enabled);

        // Same Default source, opposite state: /feature list has to be able to show both.
        FeatureState sleep = states.Single(s => s.Feature == FeatureNames.Sleep);
        Assert.Equal(FeatureStateSource.Default, sleep.Source);
        Assert.False(sleep.Enabled);
    }

    [Fact]
    public async Task SetAsync_invalidates_the_flag_cache_so_the_switch_applies_immediately()
    {
        (var gate, FakeConfigCache cache) = Build.Gate(new FakeFeatureFlagRepository());

        // Warm the blob, then flip the switch: a stale cache would keep answering "on".
        Assert.True(await gate.IsEnabledAsync(FeatureNames.Welcome, Build.Guild));

        ConfigWriteResult result = await gate.SetAsync(FeatureNames.Welcome, Build.Guild, false, Build.Actor);

        Assert.True(result.Success);
        Assert.Equal(1, cache.FlagInvalidations);
        Assert.False(await gate.IsEnabledAsync(FeatureNames.Welcome, Build.Guild));
    }

    [Fact]
    public async Task SetAsync_rejects_a_feature_outside_the_catalog()
    {
        (var gate, FakeConfigCache cache) = Build.Gate(new FakeFeatureFlagRepository());

        ConfigWriteResult result = await gate.SetAsync("economy", Build.Guild, false, Build.Actor);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
        Assert.Equal(0, cache.FlagInvalidations);
    }

    [Fact]
    public async Task IsEnabledAsync_reads_a_guild_missing_from_the_cached_blob()
    {
        // One blob covers every guild, so a warm cache from guild A must not answer for guild B.
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository().With(777, FeatureNames.Social, false));

        Assert.True(await gate.IsEnabledAsync(FeatureNames.Social, Build.Guild));
        Assert.False(await gate.IsEnabledAsync(FeatureNames.Social, 777));
    }

    [Fact]
    public void All_feature_names_are_unique_and_lowercase()
    {
        Assert.Equal(FeatureNames.All.Count, FeatureNames.All.Distinct().Count());
        Assert.All(FeatureNames.All, f => Assert.Equal(f.ToLowerInvariant(), f));
    }

    /// <summary>
    /// The polarity the legacy env vars shipped with. <c>SLEEP_MODE_ENABLED=False</c> was the
    /// template default and the Python bot logged two separate bugs from code that disagreed with
    /// it, so a fresh install must not go quiet at 22:00 because nobody said anything.
    /// </summary>
    [Fact]
    public async Task A_restriction_with_no_row_is_off_even_though_a_module_would_be_on()
    {
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository());

        Assert.False(await gate.IsEnabledAsync(FeatureNames.Sleep, Build.Guild));
        Assert.False(await gate.IsEnabledAsync(FeatureNames.Midday, Build.Guild));

        // Same call, same absent row, opposite answer — the difference is the catalog, not the store.
        Assert.True(await gate.IsEnabledAsync(FeatureNames.Chat, Build.Guild));
    }

    [Fact]
    public void Every_restriction_is_a_name_the_catalog_knows()
    {
        // A typo here would be a toggle nothing can ever set, defaulting off forever in silence.
        Assert.All(FeatureNames.Restrictions, r => Assert.Contains(r, FeatureNames.All));
        Assert.NotEmpty(FeatureNames.Restrictions);
    }

    /// <summary>
    /// <c>sonarr set sleep false</c> has to land on the <c>sleep_mode</c> row. Writing the alias
    /// verbatim would store a row no reader ever consults — the toggle would report success and
    /// change nothing.
    /// </summary>
    [Fact]
    public async Task SetAsync_writes_the_catalog_name_when_given_an_alias()
    {
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository());

        ConfigWriteResult result = await gate.SetAsync("sleep", GlobalScope, true, Build.Actor);

        Assert.True(result.Success);
        Assert.True(await gate.IsEnabledAsync(FeatureNames.Sleep, Build.Guild));
    }

    [Fact]
    public void Resolve_maps_the_documented_short_names_and_leaves_real_names_alone()
    {
        Assert.Equal(FeatureNames.Sleep, FeatureNames.Resolve("sleep"));
        Assert.Equal(FeatureNames.Midday, FeatureNames.Resolve("midday"));
        Assert.Equal(FeatureNames.Midday, FeatureNames.Resolve("lunch"));

        // Idempotent, so callers can normalize unconditionally.
        Assert.Equal(FeatureNames.Sleep, FeatureNames.Resolve(FeatureNames.Sleep));
        Assert.Equal(FeatureNames.Chat, FeatureNames.Resolve("  CHAT "));

        // An unknown name resolves to itself so IsKnown, not Resolve, is what rejects it.
        Assert.Equal("nonsense", FeatureNames.Resolve("Nonsense"));
        Assert.False(FeatureNames.IsKnown("nonsense"));
    }

    /// <summary>
    /// The global row a bot admin writes, and the per-guild override that still beats it — the
    /// shape <c>/feature global</c> and <c>sonarr set</c> both depend on.
    /// </summary>
    [Fact]
    public async Task A_global_restriction_reaches_every_guild_until_one_opts_out()
    {
        (var gate, _) = Build.Gate(new FakeFeatureFlagRepository());

        await gate.SetAsync(FeatureNames.Sleep, GlobalScope, true, Build.Actor);

        Assert.True(await gate.IsEnabledAsync(FeatureNames.Sleep, Build.Guild));
        Assert.True(await gate.IsEnabledAsync(FeatureNames.Sleep, 777));

        await gate.SetAsync(FeatureNames.Sleep, 777, false, Build.Actor);

        Assert.True(await gate.IsEnabledAsync(FeatureNames.Sleep, Build.Guild));
        Assert.False(await gate.IsEnabledAsync(FeatureNames.Sleep, 777));
    }

    /// <inheritdoc cref="FeatureNames.Restrictions"/>
    private const ulong GlobalScope = 0;
}
