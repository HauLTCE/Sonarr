using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;

namespace Sonarr.Application.Levels;

/// <inheritdoc cref="ILevelService"/>
public sealed class LevelService(
    ILevelProgressRepository progress,
    ILevelRewardRepository rewards,
    ISeasonRepository seasons,
    IMemberRepository members,
    IGuildConfigService config,
    ICooldownStore cooldowns,
    ILogger<LevelService> log) : ILevelService
{
    /// <summary>Rows per <c>/leaderboard</c> page — one Discord message, no scrolling.</summary>
    public const int LeaderboardPageSize = 10;

    /// <summary>Level rewards above this are a config mistake, not an ambition.</summary>
    public const int MaxRewardLevel = 500;

    public async Task<XpAward> AwardMessageXpAsync(
        ulong guildId,
        ulong userId,
        ulong channelId,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        LevelsPolicy policy = await GetPolicyAsync(guildId, cancellationToken);

        var weight = policy.WeightFor(channelId);
        if (weight == 0)
        {
            // Weight 0 is an admin muting XP in a channel. Checked before the cooldown so a
            // muted channel does not burn the user's 60 s window.
            return XpAward.Skip(XpSkipReason.ChannelExcluded);
        }

        // FAILS CLOSED by contract (ICooldownStore): Redis down = no XP, never unlimited XP.
        if (!await cooldowns.TryAcquireXpAsync(guildId, userId, cancellationToken))
        {
            return XpAward.Skip(XpSkipReason.Cooldown);
        }

        LevelProgress row = await progress.GetOrCreateAsync((long)guildId, (long)userId, cancellationToken);

        StreakRules.StreakTouch streak = StreakRules.Touch(row.StreakDays, row.StreakLastDay, today);
        var bonusDue = StreakRules.FirstMessageBonusDue(row.FirstMsgBonusDay, today);

        var baseXp = XpRules.MessageXp + (bonusDue ? XpRules.FirstMessageBonusXp : 0);
        var gained = XpRules.Scale(baseXp, policy.XpMultiplier, weight);

        var previousLevel = row.Level;
        row.Xp += gained;
        row.Level = LevelCurve.LevelFor(row.Xp);
        row.LastMessageXpAt = DateTimeOffset.UtcNow;
        row.StreakDays = streak.Days;
        row.StreakLastDay = today;
        if (bonusDue)
        {
            row.FirstMsgBonusDay = today;
        }

        await progress.SaveAsync(row, cancellationToken);

        return new XpAward(
            gained,
            row.Xp,
            previousLevel,
            row.Level,
            row.StreakDays,
            streak.Changed,
            bonusDue,
            await EarnedRolesAsync(guildId, previousLevel, row.Level, cancellationToken));
    }

    public async Task<XpAward> AwardVoiceXpAsync(
        ulong guildId,
        ulong userId,
        ulong channelId,
        int humanCount,
        bool muted,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        // The anti-AFK rule (docs/08): ≥2 humans in the channel AND the user unmuted.
        if (!XpRules.VoiceTimeCounts(humanCount, muted))
        {
            return XpAward.Skip(XpSkipReason.Cooldown);
        }

        LevelsPolicy policy = await GetPolicyAsync(guildId, cancellationToken);
        var weight = policy.WeightFor(channelId);
        if (weight == 0)
        {
            return XpAward.Skip(XpSkipReason.ChannelExcluded);
        }

        var gained = XpRules.Scale(XpRules.VoiceXpFor(elapsed), policy.XpMultiplier, weight);
        if (gained <= 0)
        {
            return XpAward.Skip(XpSkipReason.Cooldown);
        }

        LevelProgress current = await progress.GetOrCreateAsync((long)guildId, (long)userId, cancellationToken);
        var previousLevel = current.Level;
        var newLevel = LevelCurve.LevelFor(current.Xp + gained);

        LevelProgress row = await progress.AddVoiceAsync(
            (long)guildId,
            (long)userId,
            (long)elapsed.TotalSeconds,
            gained,
            newLevel,
            cancellationToken);

        return new XpAward(
            gained,
            row.Xp,
            previousLevel,
            row.Level,
            row.StreakDays,
            StreakExtended: false,
            FirstMessageBonus: false,
            await EarnedRolesAsync(guildId, previousLevel, row.Level, cancellationToken));
    }

    public async Task<LevelCard> GetCardAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        LevelProgress? row = await progress.GetAsync((long)guildId, (long)userId, cancellationToken);
        if (row is null)
        {
            return LevelCard.Empty(guildId, userId);
        }

        var rank = await progress.GetRankAsync((long)guildId, (long)userId, cancellationToken);
        LevelCurve.Progress(row.Xp, row.Level, out var into, out var span);

        // Derived, not stored: the row's streak is only true as of StreakLastDay, and nothing
        // rewrites it when a day passes in silence. See StreakRules.CurrentDays for why there is
        // no nightly sweep.
        //
        // UTC, matching the day XpOnMessage stamps the row with. Reading in the guild's zone would
        // be nicer to look at and wrong: west of UTC the stored day can be "tomorrow" by that
        // clock, and a member who just spoke would be shown a streak of zero.
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new LevelCard(
            guildId,
            userId,
            row.Xp,
            row.Level,
            into,
            span,
            rank,
            StreakRules.CurrentDays(row.StreakDays, row.StreakLastDay, today),
            row.VoiceSeconds,
            row.LastMessageXpAt);
    }

    public async Task<LevelComparison> CompareAsync(
        ulong guildId, ulong leftUserId, ulong rightUserId, CancellationToken cancellationToken = default)
        => new(
            await GetCardAsync(guildId, leftUserId, cancellationToken),
            await GetCardAsync(guildId, rightUserId, cancellationToken));

    public async Task<MemberStats> GetStatsAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        Member? member = await members.GetAsync((long)guildId, (long)userId, cancellationToken);
        LevelCard card = await GetCardAsync(guildId, userId, cancellationToken);

        return new MemberStats(
            guildId,
            userId,
            member?.MessageCount ?? 0,
            member?.FirstSeenAt,
            member?.LastActiveAt,
            card);
    }

    public async Task<LeaderboardPage> GetLeaderboardAsync(
        ulong guildId, long? seasonId, int page, CancellationToken cancellationToken = default)
    {
        var requested = Math.Max(page, 1);
        var skip = (requested - 1) * LeaderboardPageSize;

        if (seasonId is { } id)
        {
            Season? season = await seasons.GetAsync(id, cancellationToken);
            if (season is null || season.GuildId != (long)guildId)
            {
                // Another guild's season id, or a stale autocomplete pick: empty rather than leak.
                return new LeaderboardPage([], 1, 1, id, "Season not found");
            }

            var total = await seasons.CountResultsAsync(id, cancellationToken);
            IReadOnlyList<LeaderboardEntry> results =
                await seasons.GetResultsAsync(id, skip, LeaderboardPageSize, cancellationToken);

            return new LeaderboardPage(
                results,
                requested,
                PageCount(total),
                id,
                $"Season {id} — {season.StartsAt:yyyy-MM-dd} to {season.EndsAt:yyyy-MM-dd}");
        }

        var count = await progress.CountAsync((long)guildId, cancellationToken);
        IReadOnlyList<LeaderboardEntry> live =
            await progress.GetTopAsync((long)guildId, skip, LeaderboardPageSize, cancellationToken);

        return new LeaderboardPage(live, requested, PageCount(count), null, "All time");
    }

    public async Task<IReadOnlyList<SeasonSummary>> GetSeasonsAsync(
        ulong guildId, CancellationToken cancellationToken = default)
        => [.. (await seasons.GetAllAsync((long)guildId, cancellationToken))
            .Select(s => new SeasonSummary(s.SeasonId, s.StartsAt, s.EndsAt, s.Status))];

    public async Task<LevelsPolicy> GetPolicyAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, ConfigValue> settings = await config.GetAllAsync(guildId, cancellationToken);

        return new LevelsPolicy(
            LevelupChannelId: Value(settings, ConfigKeys.LevelupChannel)?.AsSnowflake,
            AnnounceByDm: Value(settings, ConfigKeys.LevelupDm)?.AsBoolean ?? LevelsPolicy.Default.AnnounceByDm,
            XpMultiplier: Value(settings, ConfigKeys.XpMultiplier)?.AsInteger ?? LevelsPolicy.Default.XpMultiplier,
            DecayEnabled: Value(settings, LevelsConfigKeys.XpDecay)?.AsBoolean ?? LevelsPolicy.Default.DecayEnabled,
            ChannelWeights: LevelsConfigKeys.ParseWeights(Value(settings, LevelsConfigKeys.XpChannelWeights)?.Raw));
    }

    public async Task<IReadOnlyList<RoleReward>> GetRewardsAsync(
        ulong guildId, CancellationToken cancellationToken = default)
        => [.. (await rewards.GetAllAsync((long)guildId, cancellationToken))
            .Select(r => new RoleReward(r.Level, (ulong)r.RoleId))];

    public async Task<ConfigWriteResult> SetRewardAsync(
        ulong guildId, int level, ulong roleId, CancellationToken cancellationToken = default)
    {
        if (level < LevelCurve.FirstLevel || level > MaxRewardLevel)
        {
            return ConfigWriteResult.Rejected(
                $"Reward levels run from {LevelCurve.FirstLevel} to {MaxRewardLevel} — {level} isn't one.");
        }

        if (roleId == 0)
        {
            return ConfigWriteResult.Rejected("I need an actual role to hand out.");
        }

        await rewards.SetAsync((long)guildId, level, (long)roleId, cancellationToken);
        log.LogInformation("Level reward set: guild {GuildId} level {Level} role {RoleId}", guildId, level, roleId);

        return ConfigWriteResult.Ok($"Level {level} now grants <@&{roleId}>.");
    }

    public async Task<ConfigWriteResult> RemoveRewardAsync(
        ulong guildId, int level, CancellationToken cancellationToken = default)
    {
        var removed = await rewards.RemoveAsync((long)guildId, level, cancellationToken);
        if (!removed)
        {
            return ConfigWriteResult.Ok($"Nothing was set for level {level} — nothing to remove.");
        }

        log.LogInformation("Level reward removed: guild {GuildId} level {Level}", guildId, level);
        return ConfigWriteResult.Ok($"Level {level} no longer grants a role.");
    }

    /// <summary>
    /// Every reward the member is entitled to at <paramref name="level"/>, but only when the level
    /// actually changed — no point re-granting roles on every message.
    /// </summary>
    /// <remarks>
    /// Returns the full earned set, not just the new one: a member who joined the guild after the
    /// rewards were configured (or lost a role) gets caught up on their next level-up. The caller
    /// grants only what is missing.
    /// </remarks>
    private async Task<IReadOnlyList<ulong>> EarnedRolesAsync(
        ulong guildId, int previousLevel, int level, CancellationToken cancellationToken)
    {
        if (level <= previousLevel)
        {
            return [];
        }

        IReadOnlyList<LevelReward> earned = await rewards.GetEarnedAsync((long)guildId, level, cancellationToken);
        return [.. earned.Select(r => (ulong)r.RoleId)];
    }

    private static ConfigValue? Value(IReadOnlyDictionary<string, ConfigValue> settings, string key)
        => settings.TryGetValue(key, out ConfigValue? value) ? value : null;

    private static int PageCount(int total) => Math.Max(1, (total + LeaderboardPageSize - 1) / LeaderboardPageSize);
}
