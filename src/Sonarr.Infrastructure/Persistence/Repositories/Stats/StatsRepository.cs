using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Infrastructure.Persistence.Repositories.Stats;

/// <inheritdoc cref="IStatsRepository"/>
/// <remarks>
/// Both writes are single-statement upserts (<c>ON CONFLICT</c>) rather than read-modify-write:
/// the callers are a per-interaction counter and a 5-minute timer, and a J2900 does not need a
/// round trip per increment (docs/03-stack.md — the box is the budget).
/// </remarks>
public sealed class StatsRepository(SonarrDbContext db) : IStatsRepository
{
    public Task IncrementCommandAsync(
        long guildId,
        string command,
        DateOnly day,
        long delta = 1,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        // Truncated to the column width so an over-long name can't fail the whole write.
        var name = command.Trim();
        if (name.Length > 64)
        {
            name = name[..64];
        }

        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO stats.command_usage (guild_id, command, day, count, created_at, updated_at)
            VALUES ({guildId}, {name}, {day}, {delta}, now(), now())
            ON CONFLICT (guild_id, command, day) DO UPDATE
            SET count = stats.command_usage.count + {delta},
                updated_at = now()
            """,
            ct);
    }

    public Task RecordActivityAsync(
        long guildId,
        DateTimeOffset hourBucket,
        int onlineEstimate,
        int voiceUsers,
        int messageDelta = 0,
        CancellationToken ct = default)
    {
        // Presence is a sample — the newest one wins. Messages are a count — they add.
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO stats.activity_sample
                (guild_id, hour_bucket, messages, voice_users, online_estimate, created_at, updated_at)
            VALUES ({guildId}, {hourBucket}, {messageDelta}, {voiceUsers}, {onlineEstimate}, now(), now())
            ON CONFLICT (guild_id, hour_bucket) DO UPDATE
            SET messages = stats.activity_sample.messages + {messageDelta},
                voice_users = {voiceUsers},
                online_estimate = {onlineEstimate},
                updated_at = now()
            """,
            ct);
    }

    /// <summary>Window bounds. 400 d is the retention ceiling, so asking for more returns the same rows.</summary>
    public const int MaxDays = 400;

    /// <summary>Commands are ranked, not listed — a guild can have a long tail of one-offs.</summary>
    public const int TopCommands = 20;

    public async Task<GuildStats> GetStatsAsync(long guildId, int days, CancellationToken ct = default)
    {
        var window = Math.Clamp(days, 1, MaxDays);
        DateTimeOffset since = DateTimeOffset.UtcNow.AddDays(-window);
        DateOnly sinceDay = DateOnly.FromDateTime(since.UtcDateTime);

        List<CommandUsageCount> commands = await db.CommandUsage
            .AsNoTracking()
            .Where(c => c.GuildId == guildId && c.Day >= sinceDay)
            .GroupBy(c => c.Command)
            .Select(g => new CommandUsageCount(g.Key, g.Sum(c => c.Count)))
            .OrderByDescending(c => c.Count)
            .Take(TopCommands)
            .ToListAsync(ct);

        List<ActivityPoint> activity = await db.ActivitySamples
            .AsNoTracking()
            .Where(a => a.GuildId == guildId && a.HourBucket >= since)
            .OrderBy(a => a.HourBucket)
            .Select(a => new ActivityPoint(a.HourBucket, a.Messages, a.VoiceUsers, a.OnlineEstimate))
            .ToListAsync(ct);

        // Growth comes from core.member.first_seen_at rather than a stats table: the join date is
        // already recorded per member, and a second copy of it is a second thing to keep true.
        // The dates come back raw and the day grouping happens here — a GroupBy over a
        // DateTimeOffset's .Date is exactly the shape that compiles and then throws at runtime,
        // and a window's worth of join dates is a few hundred timestamps at most.
        List<DateTimeOffset> joins = await db.Members
            .AsNoTracking()
            .Where(m => m.GuildId == guildId && m.FirstSeenAt >= since)
            .Select(m => m.FirstSeenAt)
            .ToListAsync(ct);

        List<MemberGrowthPoint> growth = [.. joins
            .GroupBy(at => DateOnly.FromDateTime(at.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new MemberGrowthPoint(g.Key, g.Count()))];

        return new GuildStats(window, commands, activity, growth);
    }
}
