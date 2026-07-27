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
}
