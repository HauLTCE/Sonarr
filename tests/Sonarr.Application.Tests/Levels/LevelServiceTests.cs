using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;

namespace Sonarr.Application.Tests.Levels;

/// <summary>
/// The XP rules as the service enforces them: one award per 60 s window, fail closed when Redis is
/// gone, the anti-AFK voice gate, and the streak rolling over a day.
/// </summary>
public sealed class LevelServiceTests
{
    [Fact]
    public async Task First_message_of_the_day_pays_the_bonus_and_starts_the_streak()
    {
        Build.Fixture fixture = Build.Levels();

        XpAward award = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.True(award.Granted);
        Assert.Equal(XpRules.MessageXp + XpRules.FirstMessageBonusXp, award.XpGained);
        Assert.True(award.FirstMessageBonus);
        Assert.Equal(1, award.StreakDays);
        Assert.True(award.StreakExtended);
    }

    [Fact]
    public async Task Second_message_inside_the_cooldown_earns_nothing()
    {
        Build.Fixture fixture = Build.Levels();

        await fixture.Service.AwardMessageXpAsync(Build.Guild, Build.User, Build.Channel, Build.Today);
        XpAward second = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.False(second.Granted);
        Assert.Equal(XpSkipReason.Cooldown, second.Skipped);
        Assert.Equal(0, second.XpGained);

        // The cooldown was rejected before Postgres was touched.
        Assert.Equal(1, fixture.Progress.Saves);
    }

    [Fact]
    public async Task The_cooldown_is_per_user_not_per_guild()
    {
        Build.Fixture fixture = Build.Levels();

        await fixture.Service.AwardMessageXpAsync(Build.Guild, Build.User, Build.Channel, Build.Today);
        XpAward other = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, 999UL, Build.Channel, Build.Today);

        Assert.True(other.Granted);
    }

    [Fact]
    public async Task No_redis_means_no_xp_rather_than_unlimited_xp()
    {
        Build.Fixture fixture = Build.Levels();
        fixture.Cooldowns.Available = false;

        XpAward award = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.False(award.Granted);
        Assert.Equal(XpSkipReason.Cooldown, award.Skipped);
        Assert.Equal(0, fixture.Progress.Saves);
    }

    [Fact]
    public async Task A_zero_weight_channel_earns_nothing_and_does_not_burn_the_cooldown()
    {
        Build.Fixture fixture = Build.Levels(
            config: new FakeGuildConfigService().With(LevelsConfigKeys.XpChannelWeights, $"{Build.Channel}:0"));

        XpAward muted = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.Equal(XpSkipReason.ChannelExcluded, muted.Skipped);
        Assert.Equal(0, fixture.Cooldowns.Attempts);

        // The same user in a normal channel still earns, because the window was never opened.
        XpAward elsewhere = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, 444UL, Build.Today);

        Assert.True(elsewhere.Granted);
    }

    [Fact]
    public async Task The_event_multiplier_scales_the_award()
    {
        Build.Fixture fixture = Build.Levels(
            config: new FakeGuildConfigService().With(ConfigKeys.XpMultiplier, "3"));

        XpAward award = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.Equal((XpRules.MessageXp + XpRules.FirstMessageBonusXp) * 3, award.XpGained);
    }

    [Fact]
    public async Task A_message_the_next_day_extends_the_streak_and_pays_the_bonus_again()
    {
        Build.Fixture fixture = Build.Levels();
        fixture.Progress.Seed((long)Build.Guild, (long)Build.User, row =>
        {
            row.Xp = 60;
            row.StreakDays = 3;
            row.StreakLastDay = Build.Today.AddDays(-1);
            row.FirstMsgBonusDay = Build.Today.AddDays(-1);
        });

        XpAward award = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.Equal(4, award.StreakDays);
        Assert.True(award.StreakExtended);
        Assert.True(award.FirstMessageBonus);
    }

    [Fact]
    public async Task A_gap_day_resets_the_streak_but_never_the_xp()
    {
        Build.Fixture fixture = Build.Levels();
        fixture.Progress.Seed((long)Build.Guild, (long)Build.User, row =>
        {
            row.Xp = 5_000;
            row.Level = LevelCurve.LevelFor(5_000);
            row.StreakDays = 12;
            row.StreakLastDay = Build.Today.AddDays(-4);
        });

        XpAward award = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.Equal(1, award.StreakDays);
        Assert.True(award.TotalXp > 5_000);
    }

    [Fact]
    public async Task Crossing_a_threshold_reports_the_level_up_and_the_earned_roles()
    {
        Build.Fixture fixture = Build.Levels(
            rewards: new FakeLevelRewardRepository()
                .With((long)Build.Guild, 2, 7001L)
                .With((long)Build.Guild, 5, 7002L));

        // One XP short of level 2.
        var threshold = LevelCurve.ThresholdFor(1);
        fixture.Progress.Seed((long)Build.Guild, (long)Build.User, row =>
        {
            row.Xp = threshold - 1;
            row.Level = 1;
        });

        XpAward award = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.True(award.LeveledUp);
        Assert.Equal(1, award.PreviousLevel);
        Assert.Equal(2, award.Level);

        // The full earned set, so a member missing an older role gets caught up.
        Assert.Equal([7001UL], award.RewardRoleIds);
    }

    [Fact]
    public async Task No_level_up_means_no_roles_to_grant()
    {
        Build.Fixture fixture = Build.Levels(
            rewards: new FakeLevelRewardRepository().With((long)Build.Guild, 1, 7001L));

        XpAward award = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.False(award.LeveledUp);
        Assert.Empty(award.RewardRoleIds);
    }

    [Fact]
    public async Task Alone_in_a_voice_channel_earns_nothing()
    {
        Build.Fixture fixture = Build.Levels();

        XpAward award = await fixture.Service.AwardVoiceXpAsync(
            Build.Guild, Build.User, Build.Channel, humanCount: 1, muted: false, XpRules.VoiceTick);

        Assert.False(award.Granted);
        Assert.Equal(0, award.XpGained);
    }

    [Fact]
    public async Task Muted_in_a_busy_voice_channel_earns_nothing()
    {
        Build.Fixture fixture = Build.Levels();

        XpAward award = await fixture.Service.AwardVoiceXpAsync(
            Build.Guild, Build.User, Build.Channel, humanCount: 6, muted: true, XpRules.VoiceTick);

        Assert.False(award.Granted);
    }

    [Fact]
    public async Task Unmuted_with_company_earns_the_per_minute_rate()
    {
        Build.Fixture fixture = Build.Levels();

        XpAward award = await fixture.Service.AwardVoiceXpAsync(
            Build.Guild, Build.User, Build.Channel, humanCount: 2, muted: false, TimeSpan.FromMinutes(3));

        Assert.True(award.Granted);
        Assert.Equal(XpRules.VoiceXpPerMinute * 3, award.XpGained);
    }

    [Fact]
    public async Task Voice_xp_does_not_touch_the_message_cooldown()
    {
        Build.Fixture fixture = Build.Levels();

        await fixture.Service.AwardVoiceXpAsync(
            Build.Guild, Build.User, Build.Channel, humanCount: 2, muted: false, XpRules.VoiceTick);

        Assert.Equal(0, fixture.Cooldowns.Attempts);

        XpAward message = await fixture.Service.AwardMessageXpAsync(
            Build.Guild, Build.User, Build.Channel, Build.Today);

        Assert.True(message.Granted);
    }

    [Fact]
    public async Task An_unknown_member_gets_an_empty_card_rather_than_an_error()
    {
        LevelCard card = await Build.Levels().Service.GetCardAsync(Build.Guild, Build.User);

        Assert.Equal(LevelCurve.FirstLevel, card.Level);
        Assert.Equal(0, card.Xp);
        Assert.Equal(0, card.Rank);
        Assert.Equal(0d, card.Fraction);
    }

    [Fact]
    public async Task The_live_leaderboard_ranks_by_xp()
    {
        Build.Fixture fixture = Build.Levels();
        fixture.Progress.Seed((long)Build.Guild, 1L, r => r.Xp = 100);
        fixture.Progress.Seed((long)Build.Guild, 2L, r => r.Xp = 900);
        fixture.Progress.Seed((long)Build.Guild, 3L, r => r.Xp = 500);

        LeaderboardPage page = await fixture.Service.GetLeaderboardAsync(Build.Guild, null, 1);

        Assert.Equal([2UL, 3UL, 1UL], page.Entries.Select(e => e.UserId));
        Assert.Equal([1, 2, 3], page.Entries.Select(e => e.Rank));
        Assert.Null(page.SeasonId);
    }

    [Fact]
    public async Task Another_guilds_season_id_returns_nothing_instead_of_leaking()
    {
        Build.Fixture fixture = Build.Levels(
            seasons: new FakeSeasonRepository().With(
                seasonId: 42, guildId: 999_999L, SeasonStatus.Closed, new LeaderboardEntry(1, 5UL, 10, 2)));

        LeaderboardPage page = await fixture.Service.GetLeaderboardAsync(Build.Guild, 42, 1);

        Assert.Empty(page.Entries);
    }

    [Fact]
    public async Task A_reward_level_outside_the_range_is_rejected_without_a_write()
    {
        Build.Fixture fixture = Build.Levels();

        ConfigWriteResult tooHigh = await fixture.Service.SetRewardAsync(Build.Guild, 5_000, 7001UL);
        ConfigWriteResult tooLow = await fixture.Service.SetRewardAsync(Build.Guild, 0, 7001UL);

        Assert.False(tooHigh.Success);
        Assert.False(tooLow.Success);
        Assert.Empty(await fixture.Service.GetRewardsAsync(Build.Guild));
    }

    [Fact]
    public async Task Policy_falls_back_to_defaults_when_nothing_is_configured()
    {
        LevelsPolicy policy = await Build.Levels().Service.GetPolicyAsync(Build.Guild);

        Assert.Equal(LevelsPolicy.Default.XpMultiplier, policy.XpMultiplier);
        Assert.Null(policy.LevelupChannelId);
        Assert.False(policy.AnnounceByDm);
        Assert.Empty(policy.ChannelWeights);
        Assert.Equal(100, policy.WeightFor(Build.Channel));
    }

    [Fact]
    public async Task Policy_reads_the_configured_channel_and_weights()
    {
        Build.Fixture fixture = Build.Levels(config: new FakeGuildConfigService()
            .With(ConfigKeys.LevelupChannel, "555")
            .With(ConfigKeys.LevelupDm, "true")
            .With(LevelsConfigKeys.XpChannelWeights, "333:150,444:0"));

        LevelsPolicy policy = await fixture.Service.GetPolicyAsync(Build.Guild);

        Assert.Equal(555UL, policy.LevelupChannelId);
        Assert.True(policy.AnnounceByDm);
        Assert.Equal(150, policy.WeightFor(333UL));
        Assert.Equal(0, policy.WeightFor(444UL));
        Assert.Equal(100, policy.WeightFor(999UL));
    }
}
